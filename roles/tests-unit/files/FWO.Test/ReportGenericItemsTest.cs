using FWO.Basics;
using FWO.Data.Generic;
using FWO.Data.Report;
using FWO.Report;
using FWO.Report.Filter;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class ReportGenericItemsTest
    {
        private const string kPayload = "<b>x</b>\" onmouseover=\"x'";
        private static readonly DateTime kNow = new(2026, 6, 1);

        private static SimulatedGenericApiConnection CreateApi()
        {
            GenericItemManagement proxy = new() { Id = 1, Name = "proxy-1", DevTypId = 33 };
            GenericItemManagement other = new() { Id = 2, Name = "proxy-2", DevTypId = 33 };
            return new SimulatedGenericApiConnection
            {
                KeyConfigs = [new GenericKeyConfig { DevTypId = 33, HasLastUsed = true, ReportableFields = ["pool"] }],
                Managements = [proxy, other],
                Items =
                [
                    new GenericConfigItem
                    {
                        Id = 1, MgmId = 1, ItemUid = "r1", ItemName = "due", OwnerId = 7, FirstSeen = kNow.AddDays(-400), Management = proxy,
                        LastUsed = kNow.AddDays(-100), ConfigLine = new() { ["action"] = JToken.FromObject(kPayload), ["attributes"] = new JObject { ["pool"] = kPayload + "pool" } }
                    },
                    new GenericConfigItem { Id = 2, MgmId = 1, ItemUid = "r2", ItemName = "fresh", OwnerId = 8, FirstSeen = kNow, Management = proxy, LastUsed = kNow },
                    new GenericConfigItem { Id = 3, MgmId = 2, ItemUid = "r3", ItemName = "other", OwnerId = 7, FirstSeen = kNow.AddDays(-400), Management = other }
                ]
            };
        }

        private static ReportGenericItems CreateReport(ReportType reportType, ReportParams? reportParams = null)
        {
            ReportParams parameters = reportParams ?? new() { ReportType = (int)reportType };
            parameters.ReportType = (int)reportType;
            ReportTemplate template = new("", parameters);
            ReportGenericItems report = (ReportGenericItems)ReportBase.ConstructReport(template, new SimulatedUserConfig { InitialRecertificationPeriod = 30 });
            report.ReferenceTime = kNow;
            return report;
        }

        private static async Task<ReportGenericItems> Generate(ReportType reportType, ReportParams? reportParams = null)
        {
            ReportGenericItems report = CreateReport(reportType, reportParams);
            await report.Generate(100, CreateApi(), _ => Task.CompletedTask, CancellationToken.None);
            return report;
        }

        [TestCase(ReportType.GenericItems)]
        [TestCase(ReportType.GenericRecertification)]
        [TestCase(ReportType.GenericUnusedItems)]
        public void ConstructReport_BuildsGenericReport(ReportType reportType)
        {
            Assert.Multiple(() =>
            {
                Assert.That(CreateReport(reportType), Is.TypeOf<ReportGenericItems>());
                Assert.That(reportType.IsGenericReport(), Is.True);
                Assert.That(reportType.SupportsCsvExport(), Is.True);
                Assert.That(reportType.IsVisibleTemplateType(new ReportVisibility(true, false, false, false, false)), Is.True);
                Assert.That(reportType.IsVisibleTemplateType(new ReportVisibility(false, true, true, true, true)), Is.False);
                Assert.That(ReportTypeGroups.ReportTypeSelection(), Does.Contain(reportType));
            });
        }

        [Test]
        public async Task Generate_ItemsReportListsAllItemsOfSelectedManagements()
        {
            ReportGenericItems all = await Generate(ReportType.GenericItems);
            ReportGenericItems selected = await Generate(ReportType.GenericItems, new ReportParams { GenericFilter = new() { ManagementIds = [2] } });

            Assert.That(all.ReportData.GenericItems.Select(item => item.ItemUid), Is.EquivalentTo(["r1", "r2", "r3"]));
            Assert.That(selected.ReportData.GenericItems.Select(item => item.ItemUid), Is.EqualTo(["r3"]));
            Assert.That(all.SetDescription(), Does.StartWith("3 "));
        }

        [Test]
        public async Task Generate_RecertReportListsDueItemsOfSelectedOwners()
        {
            ReportParams reportParams = new() { RecertFilter = new() { RecertOwnerList = [7], RecertificationDisplayPeriod = 0 } };

            ReportGenericItems report = await Generate(ReportType.GenericRecertification, reportParams);

            Assert.That(report.ReportData.GenericItems.Select(item => item.ItemUid), Is.EquivalentTo(["r1", "r3"]));
        }

        [Test]
        public async Task Generate_UnusedReportOnlyListsDeviceTypesWithLastUse()
        {
            ReportParams reportParams = new() { UnusedFilter = new() { UnusedForDays = 30, CreationTolerance = 10 } };

            ReportGenericItems report = await Generate(ReportType.GenericUnusedItems, reportParams);

            // r3 has no last use but was first seen long ago, r2 was used today
            Assert.That(report.ReportData.GenericItems.Select(item => item.ItemUid), Is.EquivalentTo(["r1", "r3"]));
        }

        [Test]
        public void SelectItems_IgnoresUnknownDeviceTypesAndHandlesDefaultUnusedDays()
        {
            // the default unused filter has int.MaxValue days, which must not overflow the date calculation
            ReportGenericItems report = CreateReport(ReportType.GenericUnusedItems, new ReportParams { UnusedFilter = new() });
            List<GenericConfigItem> items = [new GenericConfigItem { ItemUid = "x", Management = new() { DevTypId = 99 } }];

            Assert.That(report.SelectItems(items, []), Is.Empty);
            Assert.That(report.SelectItems(items, [new GenericKeyConfig { DevTypId = 99 }]), Is.Empty);
            Assert.That(report.SelectItems(items, [new GenericKeyConfig { DevTypId = 99, HasLastUsed = true }]), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExportToHtml_EncodesItemValues()
        {
            ReportGenericItems report = await Generate(ReportType.GenericItems);

            string html = report.ExportToHtml();

            Assert.Multiple(() =>
            {
                Assert.That(html, Does.Not.Contain(kPayload));
                Assert.That(html, Does.Not.Contain("<b>x</b>"));
                Assert.That(html, Does.Contain(System.Net.WebUtility.HtmlEncode(kPayload)));
                Assert.That(html, Does.Contain("<h3>proxy-1</h3>"));
                Assert.That(html, Does.Contain("<th>Action</th>"));
                Assert.That(html, Does.Contain("<th>pool</th>"));
                Assert.That(html, Does.Contain(System.Net.WebUtility.HtmlEncode(kPayload + "pool")));
            });
        }

        [Test]
        public void ExportToHtml_WithoutItemsShowsHint()
        {
            ReportGenericItems report = CreateReport(ReportType.GenericItems);

            Assert.That(report.ExportToHtml(), Does.Contain("generic_no_items"));
        }

        [Test]
        public async Task ExportToCsv_HasOneSectionPerManagement()
        {
            ReportGenericItems report = await Generate(ReportType.GenericRecertification, new ReportParams { RecertFilter = new() { RecertificationDisplayPeriod = 0 } });

            string csv = report.ExportToCsv();

            SimulatedUserConfig userConfig = new();
            Assert.Multiple(() =>
            {
                Assert.That(csv, Does.Contain($"# {userConfig.GetText("management")}: proxy-1"));
                Assert.That(csv, Does.Contain($"# {userConfig.GetText("management")}: proxy-2"));
                Assert.That(csv, Does.Contain(userConfig.GetText("next_recert_date")));
            });
        }

        [Test]
        public async Task ExportToJson_ContainsConfigLine()
        {
            ReportGenericItems report = await Generate(ReportType.GenericItems);

            JArray json = JArray.Parse(report.ExportToJson());

            Assert.That(json, Has.Count.EqualTo(3));
            Assert.That(json.First(item => item["uid"]!.ToString() == "r1")["config_line"]!["action"]!.ToString(), Is.EqualTo(kPayload));
        }
    }
}
