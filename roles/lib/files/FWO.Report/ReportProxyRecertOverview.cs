using System.Text;
using System.Text.Json;
using FWO.Api.Client;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data.Proxy;
using FWO.Data.Report;
using FWO.Report.Filter;
using FWO.Services;

namespace FWO.Report
{
    public class ReportProxyRecertOverview(DynGraphqlQuery query, UserConfig userConfig, ReportType reportType) : ReportBase(query, userConfig, reportType)
    {
        public List<int> SelectedManagementIds { get; set; } = [];
        public int? DueWithinDays { get; set; }

        public override async Task Generate(int elementsPerFetch, ApiConnection apiConnection, Func<ReportData, Task> callback, CancellationToken ct)
        {
            ProxyImporterClient client = new();
            List<ProxyRecertOwnerOverview> overview = await client.GetRecertOverviewAsync(SelectedManagementIds, DueWithinDays, ct);
            ReportData.ProxyRecertOverview = overview;
            ReportData.ElementsCount = overview.Sum(entry => entry.RulesDue.Count);
            await callback(ReportData);
        }

        public override string ExportToCsv()
        {
            StringBuilder sb = new();
            sb.AppendLine("owner_id,owner_name,next_recert_date,rule_id,rule_name");
            foreach (var owner in ReportData.ProxyRecertOverview)
            {
                foreach (var rule in owner.RulesDue)
                {
                    sb.AppendLine($"{owner.OwnerId},{Csv(owner.OwnerName)},{owner.NextRecertDate?.ToString("yyyy-MM-dd")},{Csv(rule.Id)},{Csv(rule.Name)}");
                }
            }
            return sb.ToString();
        }

        public override string ExportToJson()
        {
            return System.Text.Json.JsonSerializer.Serialize(ReportData.ProxyRecertOverview, new JsonSerializerOptions { WriteIndented = true });
        }

        public override string ExportToHtml()
        {
            StringBuilder report = new();
            foreach (var owner in ReportData.ProxyRecertOverview)
            {
                report.AppendLine($"<h3>{owner.OwnerName}</h3>");
                report.AppendLine("<table>");
                report.AppendLine("<tr><th>Next Recert</th><th>Rule ID</th><th>Rule Name</th></tr>");
                foreach (var rule in owner.RulesDue)
                {
                    report.AppendLine("<tr>");
                    report.AppendLine($"<td>{owner.NextRecertDate?.ToString("yyyy-MM-dd") ?? ""}</td>");
                    report.AppendLine($"<td>{rule.Id}</td>");
                    report.AppendLine($"<td>{rule.Name}</td>");
                    report.AppendLine("</tr>");
                }
                report.AppendLine("</table>");
            }
            return GenerateHtmlFrameBase(userConfig.GetText(ReportType.ToString()), Query.RawFilter, DateTime.Now, report);
        }

        public override string SetDescription()
        {
            return userConfig.GetText(ReportType.ToString());
        }

        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
