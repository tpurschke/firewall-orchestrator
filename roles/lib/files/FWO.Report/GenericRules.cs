using System.Text;
using SystemTextJson = System.Text.Json.JsonSerializer;
using System.Linq;
using System.Net;
using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data.Proxy;
using FWO.Data.Report;
using FWO.Report.Filter;
using Newtonsoft.Json;

namespace FWO.Report
{
    public abstract class GenericRules(DynGraphqlQuery query, UserConfig userConfig, ReportType reportType) : ReportBase(query, userConfig, reportType)
    {
        public List<int> SelectedManagementIds { get; set; } = [];
        public int? OwnerId { get; set; }
        public int? DueWithinDays { get; set; }
        public bool IncludeRecertified { get; set; }

        public override async Task Generate(int elementsPerFetch, ApiConnection apiConnection, Func<ReportData, Task> callback, CancellationToken ct)
        {
            Dictionary<string, object> where = new()
            {
                ["management_id"] = new Dictionary<string, object> { ["_in"] = SelectedManagementIds }
            };
            if (OwnerId != null)
            {
                where["owner_id"] = new Dictionary<string, object> { ["_eq"] = OwnerId };
            }
            if (!IncludeRecertified)
            {
                where["recertified"] = new Dictionary<string, object> { ["_eq"] = false };
            }
            if (DueWithinDays != null)
            {
                DateTime dueDate = DateTime.UtcNow.AddDays(DueWithinDays.Value);
                where["next_recert_date"] = new Dictionary<string, object> { ["_lte"] = dueDate };
            }

            ProxyRuleRecord[] records = await apiConnection.SendQueryAsync<ProxyRuleRecord[]>(
                ReportQueries.getProxyRules,
                new { where });

            List<ProxyRule> rules = records
                .Select(record => new ProxyRule
                {
                    Id = record.RuleId,
                    ManagementId = record.ManagementId,
                    ManagementName = record.ManagementName ?? "",
                    OwnerId = record.OwnerId,
                    OwnerName = record.OwnerName,
                    NextRecertDate = record.NextRecertDate,
                    LastRecertified = record.LastRecertified,
                    LastRecertifier = record.LastRecertifier,
                    Recertified = record.Recertified,
                    Comment = record.Comment ?? "",
                    RawJson = NormalizeRuleJson(record.RuleJson)
                })
                .ToList();
            ReportData.ProxyRules = rules;
            ReportData.ElementsCount = rules.Count;
            await callback(ReportData);
        }

        public override string ExportToCsv()
        {
            StringBuilder sb = new();
            ProxyRuleTableData tableData = ProxyRuleColumnHelper.BuildTableData(ReportData.ProxyRules);
            sb.AppendLine(string.Join(",", tableData.Columns.Select(Csv)));
            for (int index = 0; index < ReportData.ProxyRules.Count; index++)
            {
                ProxyRule rule = ReportData.ProxyRules[index];
                Dictionary<string, string> row = index < tableData.Rows.Count ? tableData.Rows[index] : new Dictionary<string, string>();
                IEnumerable<string> values = tableData.Columns.Select(column => Csv(GetCellValue(rule, row, column, "yyyy-MM-dd")));
                sb.AppendLine(string.Join(",", values));
            }
            return sb.ToString();
        }

        public override string ExportToJson()
        {
            return SystemTextJson.Serialize(ReportData.ProxyRules, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public override string ExportToHtml()
        {
            StringBuilder report = new();
            ProxyRuleTableData tableData = ProxyRuleColumnHelper.BuildTableData(ReportData.ProxyRules);
            report.AppendLine("<table>");
            report.AppendLine("<tr>");
            foreach (string column in tableData.Columns)
            {
                report.AppendLine($"<th>{column}</th>");
            }
            report.AppendLine("</tr>");
            for (int index = 0; index < ReportData.ProxyRules.Count; index++)
            {
                ProxyRule rule = ReportData.ProxyRules[index];
                Dictionary<string, string> row = index < tableData.Rows.Count ? tableData.Rows[index] : new Dictionary<string, string>();
                report.AppendLine("<tr>");
                foreach (string column in tableData.Columns)
                {
                    string value = GetCellValue(rule, row, column, "yyyy-MM-dd");
                    report.AppendLine($"<td>{value}</td>");
                }
                report.AppendLine("</tr>");
            }
            report.AppendLine("</table>");

            foreach (ProxyRule rule in ReportData.ProxyRules)
            {
                if (string.IsNullOrWhiteSpace(rule.RawJson))
                {
                    continue;
                }
                string title = string.IsNullOrWhiteSpace(rule.Name) ? rule.Id : rule.Name;
                report.AppendLine($"<h3>Raw JSON - {WebUtility.HtmlEncode(title)}</h3>");
                report.AppendLine("<pre>");
                report.AppendLine(WebUtility.HtmlEncode(rule.RawJson));
                report.AppendLine("</pre>");
            }
            return GenerateHtmlFrameBase(userConfig.GetText(ReportType.ToString()), Query.RawFilter, DateTime.Now, report);
        }

        public override string SetDescription()
        {
            return userConfig.GetText(ReportType.ToString());
        }

        public override bool NoRuleFound()
        {
            return ReportData.ProxyRules.Count == 0;
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static string GetCellValue(ProxyRule rule, Dictionary<string, string> row, string column, string? dateFormat)
        {
            if (column.Equals("next_recert_date", StringComparison.OrdinalIgnoreCase))
            {
                return rule.NextRecertDate?.ToString(dateFormat) ?? "";
            }
            if (column.Equals("last_recertified", StringComparison.OrdinalIgnoreCase))
            {
                return rule.LastRecertified?.ToString(dateFormat) ?? "";
            }
            return row.TryGetValue(column, out string? value) ? value : "";
        }

        private static string NormalizeRuleJson(object? ruleJson)
        {
            if (ruleJson == null)
            {
                return "";
            }
            if (ruleJson is string text)
            {
                return text;
            }
            return JsonConvert.SerializeObject(ruleJson);
        }

        private sealed class ProxyRuleRecord
        {
            [JsonProperty("rule_id")]
            public string RuleId { get; set; } = "";

            [JsonProperty("management_id")]
            public int ManagementId { get; set; }

            [JsonProperty("management_name")]
            public string? ManagementName { get; set; }

            [JsonProperty("owner_id")]
            public int? OwnerId { get; set; }

            [JsonProperty("owner_name")]
            public string? OwnerName { get; set; }

            [JsonProperty("next_recert_date")]
            public DateTime? NextRecertDate { get; set; }

            [JsonProperty("last_recertified")]
            public DateTime? LastRecertified { get; set; }

            [JsonProperty("last_recertifier")]
            public string? LastRecertifier { get; set; }

            [JsonProperty("recertified")]
            public bool Recertified { get; set; }

            [JsonProperty("comment")]
            public string? Comment { get; set; }

            [JsonProperty("rule_json")]
            public object? RuleJson { get; set; }
        }
    }
}
