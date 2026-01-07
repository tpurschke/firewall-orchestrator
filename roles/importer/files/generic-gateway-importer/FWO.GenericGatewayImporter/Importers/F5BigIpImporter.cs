using System.Text.Json;
using FWO.GenericGatewayImporter.Models;
using Microsoft.Extensions.Options;

namespace FWO.GenericGatewayImporter.Importers
{
    public class F5BigIpImporter : IGatewayConfigImporter
    {
        private readonly HttpClient httpClient;
        private readonly F5BigIpOptions options;

        public F5BigIpImporter(HttpClient httpClient, IOptions<F5BigIpOptions> options)
        {
            this.httpClient = httpClient;
            this.options = options.Value;
        }

        public async Task<List<ProxyRuleDocument>> ImportProxyRulesAsync(int managementId, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(options.RulesJsonPath))
            {
                string? filePayload = await ReadPayloadFromFileAsync(options.RulesJsonPath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(filePayload))
                {
                    return ParseGateways(filePayload, managementId);
                }
            }

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                return [];
            }

            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.BaseUrl), options.RulesEndpoint));
            if (!string.IsNullOrWhiteSpace(options.Token))
            {
                string authValue = string.IsNullOrWhiteSpace(options.AuthScheme)
                    ? options.Token
                    : $"{options.AuthScheme} {options.Token}";
                request.Headers.TryAddWithoutValidation(options.AuthHeaderName, authValue);
            }

            HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseGateways(payload, managementId);
        }

        private static async Task<string?> ReadPayloadFromFileAsync(string rulesJsonPath, CancellationToken cancellationToken)
        {
            string resolvedPath = ResolvePath(rulesJsonPath);
            if (!File.Exists(resolvedPath))
            {
                return null;
            }
            return await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        }

        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }

        private static List<ProxyRuleDocument> ParseGateways(string payload, int managementId)
        {
            List<ProxyRuleDocument> gateways = [];
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (!TryGetArray(root, "gateways", out JsonElement gatewayArray) &&
                !TryGetArray(root, "items", out gatewayArray))
            {
                if (root.ValueKind == JsonValueKind.Array)
                {
                    gatewayArray = root;
                }
                else
                {
                    return gateways;
                }
            }

            foreach (JsonElement element in gatewayArray.EnumerateArray())
            {
                string id = GetString(element, "id") ?? GetString(element, "name") ?? Guid.NewGuid().ToString("N");
                ProxyRuleDocument gateway = new()
                {
                    Id = id,
                    ManagementId = managementId,
                    Name = GetString(element, "name") ?? id,
                    Action = GetString(element, "type") ?? "f5-bigip",
                    OwnerName = GetString(element, "owner") ?? GetString(element, "ownerName"),
                    RawJson = element.GetRawText()
                };

                if (TryGetDateTime(element, "nextRecertDate", out DateTime nextRecert))
                {
                    gateway.NextRecertDate = nextRecert;
                }

                gateways.Add(gateway);
            }

            return gateways;
        }

        private static string? GetString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement value))
            {
                return null;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.ToString();
            }
            return null;
        }

        private static bool TryGetArray(JsonElement element, string property, out JsonElement array)
        {
            if (element.TryGetProperty(property, out array) && array.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
            array = default;
            return false;
        }

        private static bool TryGetDateTime(JsonElement element, string property, out DateTime value)
        {
            value = default;
            if (!element.TryGetProperty(property, out JsonElement dateElement))
            {
                return false;
            }
            if (dateElement.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(dateElement.GetString(), out DateTime parsed))
            {
                value = parsed.ToUniversalTime();
                return true;
            }
            return false;
        }
    }
}
