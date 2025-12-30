using FWO.Basics;
using FWO.Config.Api;
using FWO.Report.Filter;

namespace FWO.Report
{
    public class ReportProxyRules(DynGraphqlQuery query, UserConfig userConfig, ReportType reportType) : GenericRules(query, userConfig, reportType)
    {}
}
