using FWO.Basics.Interfaces;
using FWO.Data.Proxy;

namespace FWO.Data.Report
{
    public class ReportData
    {
        public List<ManagementReport> ManagementData { get; set; } = [];
        public List<OwnerConnectionReport> OwnerData { get; set; } = [];
        public List<GlobalCommonSvcReport> GlobalComSvc { get; set; } = [];
        public ManagementReport GlobalStats { get; set; } = new();
        public List<Rule> RulesFlat = [];
        public IEnumerable<IRuleViewData> RuleViewData = [];
        public List<ProxyRule> ProxyRules { get; set; } = [];
        public List<ProxyRecertOwnerOverview> ProxyRecertOverview { get; set; } = [];
        public int ElementsCount { get; set; }
        public int RecertificationDisplayPeriod { get; set; } = 0;

        public ReportData()
        { }

        public ReportData(ReportData reportData)
        {
            ManagementData = reportData.ManagementData;
            OwnerData = reportData.OwnerData;
            GlobalComSvc = reportData.GlobalComSvc;
            GlobalStats = reportData.GlobalStats;
            ProxyRules = reportData.ProxyRules;
            ProxyRecertOverview = reportData.ProxyRecertOverview;
            RecertificationDisplayPeriod = reportData.RecertificationDisplayPeriod;
        }
    }
}
