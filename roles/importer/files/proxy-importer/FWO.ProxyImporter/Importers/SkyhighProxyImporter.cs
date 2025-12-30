using System.Text.Json;
using FWO.ProxyImporter.Models;
using Microsoft.Extensions.Options;

namespace FWO.ProxyImporter.Importers
{
    public class SkyhighProxyImporter : IGatewayConfigImporter
    {
        private readonly HttpClient httpClient;
        private readonly SkyhighOptions options;

        public SkyhighProxyImporter(HttpClient httpClient, IOptions<SkyhighOptions> options)
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
                    return ParseRules(filePayload, managementId);
                }
            }

            if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.Token))
            {
                return [];
            }

            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(options.BaseUrl), options.RulesEndpoint));
            string authValue = string.IsNullOrWhiteSpace(options.AuthScheme)
                ? options.Token
                : $"{options.AuthScheme} {options.Token}";
            request.Headers.TryAddWithoutValidation(options.AuthHeaderName, authValue);

            HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseRules(payload, managementId);
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

        private static List<ProxyRuleDocument> ParseRules(string payload, int managementId)
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            string rulebaseId = root.ValueKind == JsonValueKind.Object
                ? GetString(root, "id") ?? GetString(root, "rulebaseId") ?? GetString(root, "name") ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(rulebaseId))
            {
                rulebaseId = $"proxy-rulebase-{managementId}";
            }

            string rulebaseName = root.ValueKind == JsonValueKind.Object
                ? GetString(root, "name") ?? GetString(root, "rulebaseName") ?? GetString(root, "policyName") ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(rulebaseName))
            {
                rulebaseName = $"Proxy rulebase {managementId}";
            }

            ProxyRuleDocument rulebase = new()
            {
                Id = rulebaseId,
                ManagementId = managementId,
                Name = rulebaseName,
                RawJson = payload
            };

            return [rulebase];
        }

        private static List<string> GetStringList(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            List<string> values = [];
            foreach (JsonElement entry in array.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    values.Add(entry.GetString() ?? "");
                }
                else if (entry.ValueKind == JsonValueKind.Object)
                {
                    string? name = GetString(entry, "name") ?? GetString(entry, "value");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        values.Add(name);
                    }
                }
            }
            return values;
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
