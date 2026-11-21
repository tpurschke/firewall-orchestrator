using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Meaning of the JSON keys of the config items of one generic (non-firewall) device type
    /// (generic.device_type_key_config). All *Key values are dot-separated paths into the config line.
    /// </summary>
    public class GenericKeyConfig
    {
        [JsonProperty("dev_typ_id"), JsonPropertyName("dev_typ_id")]
        public int DevTypId { get; set; }

        [JsonProperty("uid_key"), JsonPropertyName("uid_key")]
        public string UidKey { get; set; } = "";

        [JsonProperty("name_key"), JsonPropertyName("name_key")]
        public string? NameKey { get; set; }

        [JsonProperty("order_key"), JsonPropertyName("order_key")]
        public string? OrderKey { get; set; }

        [JsonProperty("owner_key"), JsonPropertyName("owner_key")]
        public string? OwnerKey { get; set; }

        [JsonProperty("owner_match"), JsonPropertyName("owner_match")]
        public string OwnerMatch { get; set; } = "name";

        [JsonProperty("last_used_key"), JsonPropertyName("last_used_key")]
        public string? LastUsedKey { get; set; }

        [JsonProperty("last_used_format"), JsonPropertyName("last_used_format")]
        public string? LastUsedFormat { get; set; }

        [JsonProperty("reportable_fields"), JsonPropertyName("reportable_fields")]
        public List<string> ReportableFields { get; set; } = [];

        /// <summary>
        /// Whether the device type delivers a last-used value, which the unused items report needs.
        /// </summary>
        public bool HasLastUsed => !string.IsNullOrWhiteSpace(LastUsedKey);
    }
}
