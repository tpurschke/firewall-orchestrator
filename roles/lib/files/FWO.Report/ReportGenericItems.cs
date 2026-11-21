using FWO.Api.Client;
using FWO.Basics;
using FWO.Config.Api;
using FWO.Data.Generic;
using FWO.Data.Report;
using FWO.Report.Filter;
using FWO.Services.Generic;
using Newtonsoft.Json;
using System.Text;

namespace FWO.Report
{
    /// <summary>
    /// Reports on generic (non-firewall) config items such as proxy rules:
    /// all items, items due for recertification, and unused items.
    /// </summary>
    public class ReportGenericItems(DynGraphqlQuery query, UserConfig userConfig, ReportType reportType, ReportParams reportParams)
        : ReportBase(query, userConfig, reportType)
    {
        private readonly GenericFilter genericFilter = new(reportParams.GenericFilter ?? new());
        private readonly RecertFilter recertFilter = new(reportParams.RecertFilter ?? new());
        private readonly UnusedFilter unusedFilter = reportParams.UnusedFilter ?? new();

        /// <summary>
        /// Largest day offset used for date calculations; the unused filter defaults to int.MaxValue days.
        /// </summary>
        private const int kMaxDayOffset = 36500;

        /// <summary>
        /// Reference time of the due and unused checks; fixed per report so all items use the same one.
        /// </summary>
        public DateTime ReferenceTime { get; set; } = DateTime.Now;

        public override async Task Generate(int elementsPerFetch, ApiConnection apiConnection, Func<ReportData, Task> callback, CancellationToken ct)
        {
            GenericItemService service = new(apiConnection);
            List<GenericKeyConfig> keyConfigs = await service.GetKeyConfigs();
            List<GenericItemManagement> managements = await service.GetGenericManagements(keyConfigs);
            List<int> mgmIds = [.. managements
                .Select(management => management.Id)
                .Where(mgmId => genericFilter.ManagementIds.Count == 0 || genericFilter.ManagementIds.Contains(mgmId))];
            List<int>? ownerIds = ReportType == ReportType.GenericRecertification && recertFilter.RecertOwnerList.Count > 0
                ? [.. recertFilter.RecertOwnerList]
                : null;
            ct.ThrowIfCancellationRequested();
            List<GenericConfigItem> items = await service.GetItemsWithRecertState(mgmIds, ownerIds, userConfig.InitialRecertificationPeriod, elementsPerFetch);
            ReportData = new()
            {
                GenericKeyConfigs = keyConfigs,
                GenericItems = SelectItems(items, keyConfigs),
                RecertificationDisplayPeriod = recertFilter.RecertificationDisplayPeriod
            };
            await callback(ReportData);
        }

        /// <summary>
        /// Keeps the items the report type is about.
        /// </summary>
        public List<GenericConfigItem> SelectItems(List<GenericConfigItem> items, List<GenericKeyConfig> keyConfigs)
        {
            return ReportType switch
            {
                ReportType.GenericRecertification => [.. items.Where(item => GenericRecertCalculator.IsDueWithin(item, ReferenceTime, Math.Clamp(recertFilter.RecertificationDisplayPeriod, 0, kMaxDayOffset)))],
                ReportType.GenericUnusedItems => [.. items.Where(item => IsUnusedItem(item, keyConfigs))],
                _ => items
            };
        }

        public override string ExportToCsv()
        {
            StringBuilder report = new();
            report.AppendLine($"# report type: {userConfig.GetText(ReportType.ToString())}");
            report.AppendLine($"# report generation date: {DateTime.Now.ToUniversalTime():yyyy-MM-ddTHH:mm:ssK} (UTC)");
            foreach (IGrouping<int, GenericConfigItem> managementItems in GroupByManagement())
            {
                List<GenericItemColumn> columns = ColumnsFor(managementItems.First());
                report.AppendLine($"# {userConfig.GetText("management")}: {managementItems.First().Management?.Name}");
                report.Append(OutputCsv(userConfig.GetText("management")));
                columns.ForEach(column => report.Append(OutputCsv(column.Header)));
                report.AppendLine();
                foreach (GenericConfigItem item in managementItems)
                {
                    report.Append(OutputCsv(item.Management?.Name));
                    columns.ForEach(column => report.Append(OutputCsv(column.Value(item))));
                    report.AppendLine();
                }
            }
            return report.ToString();
        }

        public override string ExportToJson()
        {
            var exportItems = ReportData.GenericItems.Select(item => new
            {
                management = item.Management?.Name,
                uid = item.ItemUid,
                name = item.ItemName,
                owner = item.Owner?.Name,
                first_seen = item.FirstSeen,
                last_used = item.LastUsed,
                next_recert_date = item.NextRecertDate,
                last_recert_date = item.LastRecertification?.RecertDate,
                last_recertifier = item.LastRecertification?.UserDn,
                decertified = item.IsDecertified,
                config_line = item.ConfigLine
            });
            return JsonConvert.SerializeObject(exportItems, Formatting.Indented);
        }

        public override string ExportToHtml()
        {
            StringBuilder report = new();
            foreach (IGrouping<int, GenericConfigItem> managementItems in GroupByManagement())
            {
                List<GenericItemColumn> columns = ColumnsFor(managementItems.First());
                report.AppendLine($"<h3>{EncodeHtml(managementItems.First().Management?.Name)}</h3>");
                report.AppendLine("<table>");
                report.AppendLine("<tr>");
                columns.ForEach(column => report.AppendLine($"<th>{EncodeHtml(column.Header)}</th>"));
                report.AppendLine("</tr>");
                foreach (GenericConfigItem item in managementItems)
                {
                    report.AppendLine("<tr>");
                    columns.ForEach(column => report.AppendLine($"<td>{FormatHtmlCell(column.Value(item))}</td>"));
                    report.AppendLine("</tr>");
                }
                report.AppendLine("</table>");
            }
            if (ReportData.GenericItems.Count == 0)
            {
                report.AppendLine($"<p>{EncodeHtml(userConfig.GetText("generic_no_items"))}</p>");
            }
            return GenerateHtmlFrameBase(userConfig.GetText(ReportType.ToString()), Query.RawFilter, DateTime.Now, report);
        }

        public override string SetDescription()
        {
            return $"{ReportData.GenericItems.Count} {userConfig.GetText("generic_items")}";
        }

        /// <summary>
        /// Items grouped per management, in management and item order.
        /// </summary>
        public IEnumerable<IGrouping<int, GenericConfigItem>> GroupByManagement()
        {
            return ReportData.GenericItems
                .OrderBy(item => item.Management?.Name)
                .ThenBy(item => item.MgmId)
                .ThenBy(item => item.ItemOrder)
                .GroupBy(item => item.MgmId);
        }

        private List<GenericItemColumn> ColumnsFor(GenericConfigItem item)
        {
            GenericKeyConfig? keyConfig = GenericItemTableLayout.FindKeyConfig(ReportData.GenericKeyConfigs, item.Management?.DevTypId);
            return GenericItemTableLayout.BuildColumns(ReportType, keyConfig, userConfig);
        }

        private bool IsUnusedItem(GenericConfigItem item, List<GenericKeyConfig> keyConfigs)
        {
            GenericKeyConfig? keyConfig = GenericItemTableLayout.FindKeyConfig(keyConfigs, item.Management?.DevTypId);
            if (keyConfig == null || !keyConfig.HasLastUsed)
            {
                return false;
            }
            DateTime cutDate = ReferenceTime.AddDays(-Math.Clamp(unusedFilter.UnusedForDays, 0, kMaxDayOffset));
            DateTime creationToleranceDate = ReferenceTime.AddDays(-Math.Clamp(unusedFilter.CreationTolerance, 0, kMaxDayOffset));
            return GenericRecertCalculator.IsUnused(item, cutDate, creationToleranceDate);
        }
    }
}
