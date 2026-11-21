using FWO.Basics;
using FWO.Data.Generic;
using FWO.Report;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class GenericItemTableLayoutTest
    {
        private static readonly GenericKeyConfig kKeyConfig = new() { DevTypId = 33, NameKey = "name", ReportableFields = ["name", "action", "meta.path"] };

        private static GenericConfigItem CreateItem()
        {
            return new GenericConfigItem
            {
                ItemUid = "u1",
                ItemName = "rule one",
                Owner = new GenericItemOwner { Name = "App" },
                FirstSeen = new DateTime(2026, 1, 2),
                LastUsed = new DateTime(2026, 3, 4),
                NextRecertDate = new DateTime(2026, 5, 6),
                LastRecertification = new GenericRecertification { Recertified = true, RecertDate = new DateTime(2025, 5, 6), UserDn = "cn=r" },
                ConfigLine = new() { ["action"] = new JValue("block"), ["meta"] = new JObject { ["path"] = "A > B" } }
            };
        }

        private static readonly SimulatedUserConfig kUserConfig = new();

        private static string T(string key) => kUserConfig.GetText(key);

        private static Dictionary<string, string> Render(ReportType reportType, GenericConfigItem item, GenericKeyConfig? keyConfig = null)
        {
            return GenericItemTableLayout.BuildColumns(reportType, keyConfig ?? kKeyConfig, kUserConfig)
                .ToDictionary(column => column.Header, column => column.Value(item));
        }

        [Test]
        public void ItemsReport_ShowsReportableFieldsWithoutRepeatingTheName()
        {
            Dictionary<string, string> row = Render(ReportType.GenericItems, CreateItem());

            Assert.That(row.Keys, Is.EqualTo([T("uid"), T("name"), T("owner"), "action", "meta.path", T("first_seen")]));
            Assert.That(row["action"], Is.EqualTo("block"));
            Assert.That(row["meta.path"], Is.EqualTo("A > B"));
            Assert.That(row[T("first_seen")], Is.EqualTo("2026-01-02"));
        }

        [Test]
        public void RecertReport_ShowsDueDateAndLastDecision()
        {
            GenericConfigItem item = CreateItem();
            Dictionary<string, string> row = Render(ReportType.GenericRecertification, item);

            Assert.That(row[T("next_recert_date")], Is.EqualTo("2026-05-06"));
            Assert.That(row[T("last_recertified")], Is.EqualTo("2025-05-06"));
            Assert.That(row[T("last_recertifier")], Is.EqualTo("cn=r"));

            item.LastRecertification = new GenericRecertification { Recertified = false };
            Assert.That(Render(ReportType.GenericRecertification, item)[T("next_recert_date")], Is.EqualTo(T("decertified")));
        }

        [Test]
        public void UnusedReport_ShowsLastUseAndFirstSeen()
        {
            GenericConfigItem item = CreateItem();
            item.Owner = null;
            item.ItemName = null;

            Dictionary<string, string> row = Render(ReportType.GenericUnusedItems, item, new GenericKeyConfig());

            Assert.That(row.Keys, Is.EqualTo([T("uid"), T("name"), T("owner"), T("last_used"), T("first_seen")]));
            Assert.That(row[T("last_used")], Is.EqualTo("2026-03-04"));
            Assert.That(row[T("owner")], Is.Empty);
            Assert.That(row[T("name")], Is.Empty);
        }

        [Test]
        public void FindKeyConfigAndFormatDate()
        {
            Assert.That(GenericItemTableLayout.FindKeyConfig([kKeyConfig], 33), Is.SameAs(kKeyConfig));
            Assert.That(GenericItemTableLayout.FindKeyConfig([kKeyConfig], null), Is.Null);
            Assert.That(GenericItemTableLayout.FormatDate(null), Is.Empty);
        }
    }
}
