using FWO.GenericGatewayImporter.Models;

namespace FWO.GenericGatewayImporter.Importers
{
    public interface IGatewayConfigImporter
    {
        Task<List<ProxyRuleDocument>> ImportProxyRulesAsync(int managementId, CancellationToken cancellationToken);
    }
}
