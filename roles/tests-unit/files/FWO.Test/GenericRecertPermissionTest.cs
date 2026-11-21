using System.Text.Json;
using NUnit.Framework;

namespace FWO.Test
{
    /// <summary>
    /// Guards the Hasura permissions of the generic (non-firewall) config items and their recertification:
    /// recertification decisions are append-only, bound to the recertifier's own DN and to owners the recertifier
    /// may recertify; config items are only written by the importer; reporters only see visible managements.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    internal class GenericRecertPermissionTest
    {
        private const string kMetadataFile = "replace_metadata.json";
        private const string kSchema = "generic";
        private const string kMetadataOutOfReach =
            "The Hasura metadata is not reachable in this environment, so the generic recertification permissions cannot be checked.";
        private const string kRecertifier = "recertifier";
        private const string kImporter = "importer";

        private static readonly List<string> kDecisionTables = ["recertification", "owner_recertification"];
        private static readonly List<string> kGenericTables = ["device_type_key_config", "config_item", "recertification", "owner_recertification"];

        private static readonly Lazy<FileInfo?> kMetadataFileInfo = new(LocateMetadata);

        [SetUp]
        public void RequireMetadata()
        {
            if (kMetadataFileInfo.Value is null)
            {
                Assert.Ignore(kMetadataOutOfReach);
            }
        }

        [Test]
        public void AllGenericTablesAreTracked()
        {
            foreach (string table in kGenericTables)
            {
                Assert.That(ReadTable(table), Is.Not.Null, $"{kSchema}.{table} is not tracked");
            }
        }

        [Test]
        public void Decisions_AreAppendOnlyForUserRoles()
        {
            foreach (string table in kDecisionTables)
            {
                JsonElement tableEntry = ReadTable(table)!.Value;
                Assert.That(Roles(tableEntry, "update_permissions"), Is.Empty, $"{table} can be updated");
                Assert.That(Roles(tableEntry, "delete_permissions"), Is.Empty, $"{table} can be deleted");
                Assert.That(Roles(tableEntry, "insert_permissions"), Is.EqualTo([kRecertifier]), $"{table} insert roles");
            }
        }

        [Test]
        public void Decisions_AreBoundToOwnDnAndRecertifiableOwners()
        {
            foreach (string table in kDecisionTables)
            {
                JsonElement permission = Permission(ReadTable(table)!.Value, "insert_permissions", kRecertifier);
                string check = JsonSerializer.Serialize(permission.GetProperty("check"));
                Assert.Multiple(() =>
                {
                    Assert.That(check, Does.Contain("{\"user_dn\":{\"_eq\":\"x-hasura-uuid\"}}"), table);
                    Assert.That(check, Does.Contain("{\"owner_id\":{\"_in\":\"x-hasura-recertifiable-owners\"}}"), table);
                    Assert.That(Columns(permission), Does.Not.Contain("recert_date"), $"{table}: recert date must be set by the database");
                    Assert.That(Columns(permission), Does.Not.Contain("id"), table);
                });
            }
            string itemCheck = JsonSerializer.Serialize(Permission(ReadTable("recertification")!.Value, "insert_permissions", kRecertifier).GetProperty("check"));
            Assert.That(itemCheck, Does.Contain("\"config_item\":{\"owner_id\":{\"_in\":\"x-hasura-recertifiable-owners\"}}"));
        }

        [Test]
        public void ConfigItems_AreOnlyWrittenByTheImporter()
        {
            JsonElement configItem = ReadTable("config_item")!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(Roles(configItem, "insert_permissions"), Is.EqualTo([kImporter]));
                Assert.That(Roles(configItem, "update_permissions"), Is.EqualTo([kImporter]));
                Assert.That(Roles(configItem, "delete_permissions"), Is.EqualTo([kImporter]));
                Assert.That(Columns(Permission(configItem, "update_permissions", kImporter)), Is.EquivalentTo(["item_order", "last_used", "removed"]));
                Assert.That(Roles(ReadTable("device_type_key_config")!.Value, "insert_permissions"), Is.Empty);
            });
        }

        [TestCase("config_item", "reporter", "x-hasura-visible-managements")]
        [TestCase("config_item", kRecertifier, "x-hasura-recertifiable-owners")]
        [TestCase("config_item", "modeller", "x-hasura-editable-owners")]
        [TestCase("recertification", "reporter", "x-hasura-visible-managements")]
        [TestCase("owner_recertification", "reporter", "x-hasura-tenant-id")]
        public void Select_OfScopedRolesIsFiltered(string table, string role, string sessionVariable)
        {
            JsonElement permission = Permission(ReadTable(table)!.Value, "select_permissions", role);

            Assert.That(JsonSerializer.Serialize(permission.GetProperty("filter")), Does.Contain(sessionVariable));
        }

        private static JsonElement Permission(JsonElement table, string kind, string role)
        {
            return table.GetProperty(kind).EnumerateArray().Single(entry => entry.GetProperty("role").GetString() == role).GetProperty("permission");
        }

        private static List<string> Roles(JsonElement table, string kind)
        {
            return table.TryGetProperty(kind, out JsonElement permissions)
                ? [.. permissions.EnumerateArray().Select(entry => entry.GetProperty("role").GetString() ?? "")]
                : [];
        }

        private static List<string> Columns(JsonElement permission)
        {
            return [.. permission.GetProperty("columns").EnumerateArray().Select(column => column.GetString() ?? "")];
        }

        private static JsonElement? ReadTable(string tableName)
        {
            FileInfo metadataFile = kMetadataFileInfo.Value ?? throw new InvalidOperationException(kMetadataOutOfReach);
            using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataFile.FullName));
            foreach (JsonElement table in metadata.RootElement.GetProperty("args").GetProperty("metadata")
                .GetProperty("sources").EnumerateArray().SelectMany(source => source.GetProperty("tables").EnumerateArray()))
            {
                JsonElement declaration = table.GetProperty("table");
                if (declaration.GetProperty("name").GetString() == tableName && declaration.GetProperty("schema").GetString() == kSchema)
                {
                    return table.Clone();
                }
            }
            return null;
        }

        private static FileInfo? LocateMetadata()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                FileInfo repository = new(Path.Combine(directory.FullName, "roles", "api", "files", kMetadataFile));
                FileInfo installed = new(Path.Combine(directory.FullName, kMetadataFile));
                if (repository.Exists)
                {
                    return repository;
                }
                if (installed.Exists)
                {
                    return installed;
                }
                directory = directory.Parent;
            }
            return null;
        }
    }
}
