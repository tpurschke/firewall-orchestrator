using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FWO.GenericGatewayImporter.Models;
using Microsoft.Extensions.Options;

namespace FWO.GenericGatewayImporter.Importers
{
    public class SkyhighGenericGatewayImporter : IGatewayConfigImporter
    {
        private readonly HttpClient httpClient;
        private readonly SkyhighOptions options;

        public SkyhighGenericGatewayImporter(HttpClient httpClient, IOptions<SkyhighOptions> options)
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

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                return [];
            }

            if (UseLegacyRestApi())
            {
                return await ImportLegacyRulesetsAsync(managementId, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(options.Token))
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

        private bool UseLegacyRestApi()
        {
            return !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password);
        }

        private async Task<List<ProxyRuleDocument>> ImportLegacyRulesetsAsync(int managementId, CancellationToken cancellationToken)
        {
            Uri baseUri = BuildBaseUri(options.BaseUrl);
            using HttpClientHandler handler = BuildLegacyHandler();
            using HttpClient client = new(handler) { BaseAddress = baseUri };

            await LoginAsync(client, baseUri, cancellationToken);
            try
            {
                return await FetchRulesetsAsync(client, baseUri, managementId, cancellationToken);
            }
            finally
            {
                await LogoutAsync(client, baseUri, cancellationToken);
            }
        }

        private HttpClientHandler BuildLegacyHandler()
        {
            HttpClientHandler handler = new()
            {
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            if (!options.VerifyTls)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        }

        private async Task LoginAsync(HttpClient client, Uri baseUri, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(baseUri, options.LoginEndpoint));
            string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private async Task LogoutAsync(HttpClient client, Uri baseUri, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(baseUri, options.LogoutEndpoint));
            await client.SendAsync(request, cancellationToken);
        }

        private async Task<List<ProxyRuleDocument>> FetchRulesetsAsync(HttpClient client, Uri baseUri, int managementId, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(baseUri, options.RulesetsEndpoint));
            HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            List<LegacyRulesetEntry> entries = ParseLegacyRulesetEntries(payload);

            List<ProxyRuleDocument> rulesets = [];
            foreach (LegacyRulesetEntry entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Href))
                {
                    continue;
                }

                Uri exportUri = BuildExportUri(baseUri, entry.Href, options.RulesetExportSuffix);
                using HttpRequestMessage exportRequest = new(HttpMethod.Post, exportUri);
                HttpResponseMessage exportResponse = await client.SendAsync(exportRequest, cancellationToken);
                exportResponse.EnsureSuccessStatusCode();

                string rulesetPayload = await exportResponse.Content.ReadAsStringAsync(cancellationToken);
                string id = !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id : entry.Title;
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = Guid.NewGuid().ToString("N");
                }

                rulesets.Add(new ProxyRuleDocument
                {
                    Id = id,
                    ManagementId = managementId,
                    Name = !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : id,
                    Action = "skyhigh-ruleset",
                    RawJson = rulesetPayload
                });
            }

            return rulesets;
        }

        private static Uri BuildBaseUri(string baseUrl)
        {
            if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUrl += "/";
            }
            return new Uri(baseUrl);
        }

        private static Uri BuildExportUri(Uri baseUri, string href, string suffix)
        {
            Uri hrefUri = Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute)
                ? absolute
                : new Uri(baseUri, href);
            string exportUrl = CombineExportUrl(hrefUri.ToString(), suffix);
            return new Uri(exportUrl, UriKind.Absolute);
        }

        private static string CombineExportUrl(string url, string suffix)
        {
            string trimmed = url.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return trimmed;
            }
            string normalizedSuffix = suffix.StartsWith("/", StringComparison.Ordinal)
                ? suffix
                : "/" + suffix;
            return trimmed + normalizedSuffix;
        }

        private static List<LegacyRulesetEntry> ParseLegacyRulesetEntries(string payload)
        {
            XDocument document = XDocument.Parse(payload);
            List<LegacyRulesetEntry> entries = [];
            foreach (XElement entry in document.Descendants().Where(e => e.Name.LocalName == "entry"))
            {
                string id = GetElementValue(entry, "id");
                string title = GetElementValue(entry, "title");
                string href = entry.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "link")
                    ?.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName == "href")
                    ?.Value ?? "";
                entries.Add(new LegacyRulesetEntry(id, title, href));
            }
            return entries;
        }

        private static string GetElementValue(XElement element, string name)
        {
            return element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
        }

        private readonly record struct LegacyRulesetEntry(string Id, string Title, string Href);

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
