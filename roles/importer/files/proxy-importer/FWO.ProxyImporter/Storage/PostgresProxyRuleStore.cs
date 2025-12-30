using System.Text.Json;
using FWO.ProxyImporter.Models;
using Npgsql;
using NpgsqlTypes;

namespace FWO.ProxyImporter.Storage
{
    public class PostgresProxyRuleStore : IProxyRuleStore
    {
        private readonly string connectionString;
        private readonly string schema;

        public PostgresProxyRuleStore(ProxyImporterOptions options)
        {
            connectionString = options.ConnectionString;
            schema = string.IsNullOrWhiteSpace(options.Schema) ? "generic_gateway" : options.Schema;
        }

        public void UpsertRules(IEnumerable<ProxyRuleDocument> rules)
        {
            using var connection = OpenConnection();
            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id))
                {
                    continue;
                }
                string sql = $@"
                    INSERT INTO {schema}.rulebase
                    (rule_id, management_id, management_name, owner_id, owner_name, next_recert_date, last_recertified, last_recertifier, recertified, comment, rule_json, imported_at)
                    VALUES
                    (@rule_id, @management_id, @management_name, @owner_id, @owner_name, @next_recert_date, @last_recertified, @last_recertifier, @recertified, @comment, @rule_json, @imported_at)
                    ON CONFLICT (rule_id) DO UPDATE SET
                        management_id = EXCLUDED.management_id,
                        management_name = EXCLUDED.management_name,
                        owner_id = EXCLUDED.owner_id,
                        owner_name = EXCLUDED.owner_name,
                        next_recert_date = EXCLUDED.next_recert_date,
                        last_recertified = EXCLUDED.last_recertified,
                        last_recertifier = EXCLUDED.last_recertifier,
                        recertified = EXCLUDED.recertified,
                        comment = EXCLUDED.comment,
                        rule_json = EXCLUDED.rule_json,
                        imported_at = EXCLUDED.imported_at;";

                using var command = new NpgsqlCommand(sql, connection);
                command.Parameters.AddWithValue("rule_id", rule.Id);
                command.Parameters.AddWithValue("management_id", rule.ManagementId);
                command.Parameters.AddWithValue("management_name", (object?)rule.ManagementName ?? DBNull.Value);
                command.Parameters.AddWithValue("owner_id", (object?)rule.OwnerId ?? DBNull.Value);
                command.Parameters.AddWithValue("owner_name", (object?)rule.OwnerName ?? DBNull.Value);
                command.Parameters.AddWithValue("next_recert_date", (object?)rule.NextRecertDate ?? DBNull.Value);
                command.Parameters.AddWithValue("last_recertified", (object?)rule.LastRecertified ?? DBNull.Value);
                command.Parameters.AddWithValue("last_recertifier", (object?)rule.LastRecertifier ?? DBNull.Value);
                command.Parameters.AddWithValue("recertified", rule.Recertified);
                command.Parameters.AddWithValue("comment", (object?)rule.Comment ?? DBNull.Value);
                command.Parameters.Add("rule_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(rule);
                command.Parameters.AddWithValue("imported_at", rule.ImportedAt);
                command.ExecuteNonQuery();
            }
        }

        public List<ProxyRuleDocument> GetRules(List<int> managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified)
        {
            using var connection = OpenConnection();
            using var command = new NpgsqlCommand(BuildRulesQuery(managementIds, ownerId, dueWithinDays, includeRecertified), connection);
            AddRulesQueryParameters(command, managementIds, ownerId, dueWithinDays);

            List<ProxyRuleDocument> result = [];
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string json = reader.GetString(0);
                ProxyRuleDocument? rule = JsonSerializer.Deserialize<ProxyRuleDocument>(json);
                if (rule != null)
                {
                    if (string.IsNullOrWhiteSpace(rule.ManagementName))
                    {
                        rule.ManagementName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    }
                    result.Add(rule);
                }
            }
            return result;
        }

        public List<ProxyRecertOwnerOverview> GetRecertOverview(List<int> managementIds, int? dueWithinDays)
        {
            using var connection = OpenConnection();
            string sql = BuildRecertOverviewQuery(managementIds, dueWithinDays);
            using var command = new NpgsqlCommand(sql, connection);
            AddRecertOverviewParameters(command, managementIds, dueWithinDays);

            Dictionary<string, ProxyRecertOwnerOverview> owners = new();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string json = reader.GetString(0);
                ProxyRuleDocument? rule = JsonSerializer.Deserialize<ProxyRuleDocument>(json);
                if (rule == null)
                {
                    continue;
                }
                string ownerKey = rule.OwnerId?.ToString() ?? rule.OwnerName ?? "unassigned";
                if (!owners.TryGetValue(ownerKey, out ProxyRecertOwnerOverview? overview))
                {
                    overview = new ProxyRecertOwnerOverview
                    {
                        OwnerId = rule.OwnerId,
                        OwnerName = string.IsNullOrWhiteSpace(rule.OwnerName) ? "Unassigned" : rule.OwnerName,
                        NextRecertDate = rule.NextRecertDate
                    };
                    owners.Add(ownerKey, overview);
                }
                overview.RulesDue.Add(rule);
                if (rule.NextRecertDate != null && (overview.NextRecertDate == null || rule.NextRecertDate < overview.NextRecertDate))
                {
                    overview.NextRecertDate = rule.NextRecertDate;
                }
            }
            return owners.Values.OrderBy(o => o.NextRecertDate).ThenBy(o => o.OwnerName).ToList();
        }

        public int RecertifyRules(ProxyRecertificationRequest request, int defaultRecertDays)
        {
            if (request.RuleIds.Count == 0)
            {
                return 0;
            }

            using var connection = OpenConnection();
            int updated = 0;
            DateTime now = DateTime.UtcNow;
            DateTime nextRecert = request.NextRecertDate ?? now.AddDays(defaultRecertDays);

            foreach (var ruleId in request.RuleIds.Distinct())
            {
                string selectSql = $"SELECT rule_json FROM {schema}.rulebase WHERE rule_id = @rule_id";
                using var selectCommand = new NpgsqlCommand(selectSql, connection);
                selectCommand.Parameters.AddWithValue("rule_id", ruleId);
                object? jsonObj = selectCommand.ExecuteScalar();
                if (jsonObj == null)
                {
                    continue;
                }
                ProxyRuleDocument? rule = JsonSerializer.Deserialize<ProxyRuleDocument>(jsonObj.ToString() ?? "");
                if (rule == null)
                {
                    continue;
                }
                rule.Recertified = true;
                rule.LastRecertified = now;
                rule.LastRecertifier = request.RecertifierDn;
                rule.Comment = request.Comment;
                rule.NextRecertDate = nextRecert;

                string updateSql = $@"
                    UPDATE {schema}.rulebase
                    SET recertified = @recertified,
                        last_recertified = @last_recertified,
                        last_recertifier = @last_recertifier,
                        comment = @comment,
                        next_recert_date = @next_recert_date,
                        rule_json = @rule_json
                    WHERE rule_id = @rule_id";

                using var updateCommand = new NpgsqlCommand(updateSql, connection);
                updateCommand.Parameters.AddWithValue("recertified", rule.Recertified);
                updateCommand.Parameters.AddWithValue("last_recertified", (object?)rule.LastRecertified ?? DBNull.Value);
                updateCommand.Parameters.AddWithValue("last_recertifier", (object?)rule.LastRecertifier ?? DBNull.Value);
                updateCommand.Parameters.AddWithValue("comment", (object?)rule.Comment ?? DBNull.Value);
                updateCommand.Parameters.AddWithValue("next_recert_date", (object?)rule.NextRecertDate ?? DBNull.Value);
                updateCommand.Parameters.AddWithValue("rule_id", ruleId);
                updateCommand.Parameters.Add("rule_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(rule);
                updated += updateCommand.ExecuteNonQuery();
            }
            return updated;
        }

        private NpgsqlConnection OpenConnection()
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Proxy importer database connection string is missing.");
            }
            var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private string BuildRulesQuery(List<int> managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified)
        {
            List<string> conditions = [];
            if (managementIds.Count > 0)
            {
                conditions.Add("management_id = ANY(@management_ids)");
            }
            if (ownerId != null)
            {
                conditions.Add("owner_id = @owner_id");
            }
            if (!includeRecertified)
            {
                conditions.Add("recertified = false");
            }
            if (dueWithinDays != null)
            {
                conditions.Add("next_recert_date IS NOT NULL AND next_recert_date <= @due_date");
            }

            string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            return $"SELECT rule_json, management_id, management_name FROM {schema}.rulebase {whereClause} ORDER BY next_recert_date NULLS LAST";
        }

        private void AddRulesQueryParameters(NpgsqlCommand command, List<int> managementIds, int? ownerId, int? dueWithinDays)
        {
            if (managementIds.Count > 0)
            {
                command.Parameters.AddWithValue("management_ids", managementIds.ToArray());
            }
            if (ownerId != null)
            {
                command.Parameters.AddWithValue("owner_id", ownerId.Value);
            }
            if (dueWithinDays != null)
            {
                command.Parameters.AddWithValue("due_date", DateTime.UtcNow.AddDays(dueWithinDays.Value));
            }
        }

        private string BuildRecertOverviewQuery(List<int> managementIds, int? dueWithinDays)
        {
            List<string> conditions = [];
            if (managementIds.Count > 0)
            {
                conditions.Add("management_id = ANY(@management_ids)");
            }
            if (dueWithinDays != null)
            {
                conditions.Add("next_recert_date IS NOT NULL AND next_recert_date <= @due_date");
            }
            string whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            return $"SELECT rule_json FROM {schema}.rulebase {whereClause} ORDER BY next_recert_date NULLS LAST";
        }

        private void AddRecertOverviewParameters(NpgsqlCommand command, List<int> managementIds, int? dueWithinDays)
        {
            if (managementIds.Count > 0)
            {
                command.Parameters.AddWithValue("management_ids", managementIds.ToArray());
            }
            if (dueWithinDays != null)
            {
                command.Parameters.AddWithValue("due_date", DateTime.UtcNow.AddDays(dueWithinDays.Value));
            }
        }
    }
}
