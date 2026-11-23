using FWO.Basics;
using FWO.Config.Api;
using FWO.Data.Generic;

namespace FWO.Report
{
    /// <summary>
    /// Column of a table of generic config items: header and plain-text value of an item.
    /// Values are plain text; encode them where HTML is built.
    /// </summary>
    public sealed record GenericItemColumn(string Header, Func<GenericConfigItem, string> Value);

    /// <summary>
    /// Columns of the reports on generic (non-firewall) config items, shared by exports and UI.
    /// The short view shows the core fields, which are the same for all device types, and the reportable
    /// attributes of the device type; the detailed view shows the whole config line.
    /// </summary>
    public static class GenericItemTableLayout
    {
        private const string kDateFormat = "yyyy-MM-dd";
        private const char kPathSeparator = '.';

        /// <summary>
        /// Columns for the items of one device type in a report of the given type.
        /// </summary>
        public static List<GenericItemColumn> BuildColumns(ReportType reportType, GenericKeyConfig? keyConfig, UserConfig userConfig)
        {
            List<GenericItemColumn> columns =
            [
                new(userConfig.GetText("uid"), item => item.ItemUid),
                new(userConfig.GetText("name"), item => item.ItemName ?? ""),
                new(userConfig.GetText("owner"), item => item.Owner?.Name ?? ""),
                new(userConfig.GetText("scope"), item => GenericConfigLine.GetDisplayValue(item.ConfigLine, GenericConfigLine.kScopeField)),
                new(userConfig.GetText("enabled"), item => FormatEnabled(item, userConfig)),
                new(userConfig.GetText("generic_match"), item => GenericConfigLine.GetDisplayValue(item.ConfigLine, GenericConfigLine.kMatchField)),
                new(userConfig.GetText("action"), item => GenericConfigLine.GetDisplayValue(item.ConfigLine, GenericConfigLine.kActionField))
            ];
            foreach (string field in keyConfig?.ReportableFields ?? [])
            {
                string attributePath = $"{GenericConfigLine.kAttributesField}{kPathSeparator}{field}";
                columns.Add(new(field, item => GenericConfigLine.GetDisplayValue(item.ConfigLine, attributePath)));
            }
            columns.AddRange(BuildTypeSpecificColumns(reportType, userConfig));
            return columns;
        }

        /// <summary>
        /// Enabled flag of an item as localized yes/no, empty if the item has none.
        /// </summary>
        public static string FormatEnabled(GenericConfigItem item, UserConfig userConfig)
        {
            string enabledText = GenericConfigLine.GetDisplayValue(item.ConfigLine, GenericConfigLine.kEnabledField);
            if (!bool.TryParse(enabledText, out bool enabled))
            {
                return "";
            }
            return userConfig.GetText(enabled ? "yes" : "no");
        }

        /// <summary>
        /// Key configuration of the device type of an item's management, if known.
        /// </summary>
        public static GenericKeyConfig? FindKeyConfig(IEnumerable<GenericKeyConfig> keyConfigs, int? devTypId)
        {
            return keyConfigs.FirstOrDefault(keyConfig => keyConfig.DevTypId == devTypId);
        }

        /// <summary>
        /// Date as shown in the generic reports, empty for no date.
        /// </summary>
        public static string FormatDate(DateTime? date)
        {
            return date?.ToString(kDateFormat) ?? "";
        }

        private static List<GenericItemColumn> BuildTypeSpecificColumns(ReportType reportType, UserConfig userConfig)
        {
            return reportType switch
            {
                ReportType.GenericRecertification =>
                [
                    new(userConfig.GetText("next_recert_date"), item => item.IsDecertified ? userConfig.GetText("decertified") : FormatDate(item.NextRecertDate)),
                    new(userConfig.GetText("last_recertified"), item => FormatDate(item.LastRecertification?.RecertDate)),
                    new(userConfig.GetText("last_recertifier"), item => item.LastRecertification?.UserDn ?? "")
                ],
                ReportType.GenericUnusedItems =>
                [
                    new(userConfig.GetText("last_used"), item => FormatDate(item.LastUsed)),
                    new(userConfig.GetText("first_seen"), item => FormatDate(item.FirstSeen))
                ],
                _ =>
                [
                    new(userConfig.GetText("first_seen"), item => FormatDate(item.FirstSeen))
                ]
            };
        }
    }
}
