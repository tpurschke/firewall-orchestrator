using System.Text.Json;
using System.Linq;

namespace FWO.Data.Proxy
{
    public sealed class ProxyRuleTableData
    {
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<Dictionary<string, string>> Rows { get; }

        public ProxyRuleTableData(IReadOnlyList<string> columns, IReadOnlyList<Dictionary<string, string>> rows)
        {
            Columns = columns;
            Rows = rows;
        }
    }

    public static class ProxyRuleColumnHelper
    {
        private static readonly string[] PreferredOrder =
        [
            "id",
            "management_id",
            "management_name",
            "name",
            "action",
            "sources",
            "destinations",
            "services",
            "owner_id",
            "owner_name",
            "next_recert_date",
            "last_recertified",
            "last_recertifier",
            "recertified",
            "comment"
        ];

        private static readonly HashSet<string> ExcludedColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "raw_json"
        };

        public static ProxyRuleTableData BuildTableData(IEnumerable<ProxyRule> rules)
        {
            List<Dictionary<string, string>> rows = [];
            Dictionary<string, string> columns = new(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in rules)
            {
                Dictionary<string, string> row = ExtractValues(rule);
                foreach (var key in row.Keys)
                {
                    if (!columns.ContainsKey(key))
                    {
                        columns[key] = key;
                    }
                }
                rows.Add(row);
            }

            List<string> orderedColumns = OrderColumns(columns);
            if (orderedColumns.Count == 0)
            {
                orderedColumns = PreferredOrder.Where(c => !ExcludedColumns.Contains(c)).ToList();
            }

            List<string> visibleColumns = orderedColumns
                .Where(column => rows.Any(row => row.TryGetValue(column, out string? value) && !IsEmptyJsonValue(value)))
                .ToList();

            return new ProxyRuleTableData(visibleColumns, rows);
        }

        private static List<string> OrderColumns(Dictionary<string, string> columns)
        {
            List<string> ordered = [];
            foreach (string column in PreferredOrder)
            {
                if (columns.TryGetValue(column, out string? actual))
                {
                    ordered.Add(actual);
                    columns.Remove(column);
                }
            }

            foreach (string column in columns.Values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(column);
            }

            return ordered;
        }

        private static Dictionary<string, string> ExtractValues(ProxyRule rule)
        {
            Dictionary<string, string> properties = new(StringComparer.OrdinalIgnoreCase);
            AddPropertiesFromJson(properties, JsonSerializer.Serialize(rule));
            if (!string.IsNullOrWhiteSpace(rule.RawJson))
            {
                AddPropertiesFromJson(properties, rule.RawJson, overwriteIfEmpty: true);
            }
            return properties;
        }

        private static void AddPropertiesFromJson(Dictionary<string, string> target, string json, bool overwriteIfEmpty = false)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (ExcludedColumns.Contains(property.Name))
                    {
                        continue;
                    }

                    string value = FormatJsonValue(property.Value);
                    if (!target.TryGetValue(property.Name, out string? existing))
                    {
                        target[property.Name] = value;
                        continue;
                    }

                    if (overwriteIfEmpty && IsEmptyJsonValue(existing))
                    {
                        target[property.Name] = value;
                    }
                }
            }
            catch (JsonException)
            {
                return;
            }
        }

        private static string FormatJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? "",
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                JsonValueKind.Undefined => "",
                _ => element.GetRawText()
            };
        }

        private static bool IsEmptyJsonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
            return value == "[]" || value == "{}" || value == "null";
        }
    }
}
