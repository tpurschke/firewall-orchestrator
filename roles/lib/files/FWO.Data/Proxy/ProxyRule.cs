using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Proxy
{
    public class ProxyRule
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonProperty("management_id"), JsonPropertyName("management_id")]
        public int ManagementId { get; set; }

        [JsonProperty("management_name"), JsonPropertyName("management_name")]
        public string ManagementName { get; set; } = "";

        [JsonProperty("name"), JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonProperty("action"), JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonProperty("sources"), JsonPropertyName("sources")]
        public List<string> Sources { get; set; } = [];

        [JsonProperty("destinations"), JsonPropertyName("destinations")]
        public List<string> Destinations { get; set; } = [];

        [JsonProperty("services"), JsonPropertyName("services")]
        public List<string> Services { get; set; } = [];

        [JsonProperty("owner_id"), JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonProperty("owner_name"), JsonPropertyName("owner_name")]
        public string? OwnerName { get; set; }

        [JsonProperty("next_recert_date"), JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonProperty("last_recertified"), JsonPropertyName("last_recertified")]
        public DateTime? LastRecertified { get; set; }

        [JsonProperty("last_recertifier"), JsonPropertyName("last_recertifier")]
        public string? LastRecertifier { get; set; }

        [JsonProperty("recertified"), JsonPropertyName("recertified")]
        public bool Recertified { get; set; }

        [JsonProperty("comment"), JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        [JsonProperty("raw_json"), JsonPropertyName("raw_json")]
        public string RawJson { get; set; } = "";
    }
}
