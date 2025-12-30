using System.Net.Http.Json;
using FWO.Config.File;
using FWO.Data.Proxy;
using FWO.Logging;

namespace FWO.Services
{
    public class ProxyImporterClient
    {
        private readonly HttpClient httpClient;
        private readonly string baseUri;

        public ProxyImporterClient()
            : this(new HttpClient(), ConfigFile.ProxyImporterUri)
        {
        }

        public ProxyImporterClient(HttpClient httpClient, string baseUri)
        {
            this.httpClient = httpClient;
            this.baseUri = string.IsNullOrWhiteSpace(baseUri) ? "" : baseUri.TrimEnd('/') + "/";
        }

        public async Task<List<ProxyRule>> GetProxyRulesAsync(List<int> managementIds, int? ownerId, int? dueWithinDays, bool includeRecertified, CancellationToken cancellationToken = default)
        {
            string url = BuildUrl("api/proxy/rules", managementIds, ownerId, dueWithinDays, includeRecertified);
            return await GetListAsync<ProxyRule>(url, cancellationToken);
        }

        public async Task<List<ProxyRecertOwnerOverview>> GetRecertOverviewAsync(List<int> managementIds, int? dueWithinDays, CancellationToken cancellationToken = default)
        {
            string url = BuildUrl("api/proxy/recert/overview", managementIds, null, dueWithinDays, null);
            return await GetListAsync<ProxyRecertOwnerOverview>(url, cancellationToken);
        }

        public async Task<int> RecertifyRulesAsync(ProxyRecertificationRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
            {
                Log.WriteWarning("Proxy Importer", "Proxy importer uri is not configured.");
                return 0;
            }
            string url = new Uri(new Uri(baseUri), "api/proxy/recertify").ToString();
            HttpResponseMessage response = await httpClient.PostAsJsonAsync(url, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.WriteWarning("Proxy Importer", $"Recertify request failed: {response.StatusCode}");
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
                    Log.WriteWarning("Proxy Importer", "Proxy importer uri is not configured.");
                    return [];
                }
                List<T>? result = await httpClient.GetFromJsonAsync<List<T>>(url, cancellationToken);
                return result ?? [];
            }
            catch (Exception exception)
            {
                Log.WriteError("Proxy Importer", $"Failed to call {url}", exception);
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
