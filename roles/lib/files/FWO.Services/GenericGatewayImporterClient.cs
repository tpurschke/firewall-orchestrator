using System.Net.Http.Json;
using FWO.Config.File;
using FWO.Data.Proxy;
using FWO.Logging;

namespace FWO.Services
{
    public class GenericGatewayImporterClient
    {
        private readonly HttpClient httpClient;
        private readonly string baseUri;

        public GenericGatewayImporterClient()
            : this(new HttpClient(), ConfigFile.GenericGatewayImporterUri)
        {
        }

        public GenericGatewayImporterClient(HttpClient httpClient, string baseUri)
        {
            this.httpClient = httpClient;
            this.baseUri = string.IsNullOrWhiteSpace(baseUri) ? "" : baseUri.TrimEnd('/') + "/";
        }

        public async Task<List<ProxyRule>> GetProxyRulesAsync(List<int> managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified, CancellationToken cancellationToken = default)
        {
            string url = BuildUrl("api/generic-gateway/rules", managementIds, ownerId, dueWithinDays, includeRecertified);
            return await GetListAsync<ProxyRule>(url, cancellationToken);
        }

        public async Task<List<ProxyRecertOwnerOverview>> GetRecertOverviewAsync(List<int> managementIds, int? dueWithinDays, CancellationToken cancellationToken = default)
        {
            string url = BuildUrl("api/generic-gateway/recert/overview", managementIds, null, dueWithinDays, null);
            return await GetListAsync<ProxyRecertOwnerOverview>(url, cancellationToken);
        }

        public async Task<int> RecertifyRulesAsync(ProxyRecertificationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
            {
                Log.WriteWarning("Generic Gateway Importer", "Generic gateway importer uri is not configured.");
                return 0;
            }
            string url = new Uri(new Uri(baseUri), "api/generic-gateway/recertify").ToString();
            HttpResponseMessage response = await httpClient.PostAsJsonAsync(url, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.WriteWarning("Generic Gateway Importer", $"Recertify request failed: {response.StatusCode}");
                return 0;
            }
            var result = await response.Content.ReadFromJsonAsync<RecertifyResult>(cancellationToken: cancellationToken);
            return result?.Updated ?? 0;
        }

        private async Task<List<T>> GetListAsync<T>(string url, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    Log.WriteWarning("Generic Gateway Importer", "Generic gateway importer uri is not configured.");
                    return [];
                }
                List<T>? result = await httpClient.GetFromJsonAsync<List<T>>(url, cancellationToken);
                return result ?? [];
            }
            catch (Exception exception)
            {
                Log.WriteError("Generic Gateway Importer", $"Failed to call {url}", exception);
                return [];
            }
        }

        private string BuildUrl(string path, List<int> managementIds, int? ownerId, int? dueWithinDays, bool? includeRecertified)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
            {
                return "";
            }
            List<string> queryParts = [];
            if (managementIds.Count > 0)
            {
                queryParts.Add($"managementIds={string.Join(',', managementIds)}");
            }
            if (ownerId != null)
            {
                queryParts.Add($"ownerId={ownerId}");
            }
            if (dueWithinDays != null)
            {
                queryParts.Add($"dueWithinDays={dueWithinDays}");
            }
            if (includeRecertified != null)
            {
                queryParts.Add($"includeRecertified={includeRecertified.ToString()?.ToLowerInvariant()}");
            }
            string queryString = queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "";
            return new Uri(new Uri(baseUri), path + queryString).ToString();
        }

        private class RecertifyResult
        {
            public int Updated { get; set; }
        }
    }
}
