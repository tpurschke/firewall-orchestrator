using FWO.ProxyImporter.Models;

namespace FWO.ProxyImporter.Importers
{
    public interface IGatewayConfigImporter
    {
        Task<List<ProxyRuleDocument>> ImportProxyRulesAsync(int managementId, CancellationToken cancellationToken);
    }
}
