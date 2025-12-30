using FWO.ProxyImporter.Models;

namespace FWO.ProxyImporter.Storage
{
    public interface IProxyRuleStore
    {
        void UpsertRules(IEnumerable<ProxyRuleDocument> rules);
        List<ProxyRuleDocument> GetRules(List<int> managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified);
        List<ProxyRecertOwnerOverview> GetRecertOverview(List<int> managementIds, int? dueWithinDays);
        int RecertifyRules(ProxyRecertificationRequest request, int defaultRecertDays);
    }
}
