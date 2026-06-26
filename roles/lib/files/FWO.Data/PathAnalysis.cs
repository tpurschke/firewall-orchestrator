using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace FWO.Data
{
    public enum PathAnalysisMode
    {
        GatewayRoutingTable = 0,
        StaticImport = 1
    }

    public class PathAnalysisImportParameters
    {
        [JsonProperty("source_name"), JsonPropertyName("source_name")]
        public string SourceName { get; set; } = "";

        [JsonProperty("entries"), JsonPropertyName("entries")]
        public List<PathAnalysisImportEntry> Entries { get; set; } = [];
    }

    public class PathAnalysisImportResult
    {
        [JsonProperty("imported_entries"), JsonPropertyName("imported_entries")]
        public int ImportedEntries { get; set; }

        [JsonProperty("mapped_gateways"), JsonPropertyName("mapped_gateways")]
        public int MappedGateways { get; set; }
    }

    public class PathAnalysisImportEntry
    {
        [JsonProperty("version"), JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonProperty("zone"), JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        [JsonProperty("network"), JsonPropertyName("network")]
        public string Network { get; set; } = "";

        [JsonProperty("root_path"), JsonPropertyName("root_path")]
        public string RootPath { get; set; } = "";

        [JsonProperty("internet_path"), JsonPropertyName("internet_path")]
        public string InternetPath { get; set; } = "";
    }
}
