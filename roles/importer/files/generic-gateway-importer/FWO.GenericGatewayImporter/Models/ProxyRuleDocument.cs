using System.Text.Json.Serialization;

namespace FWO.GenericGatewayImporter.Models
{
    public class ProxyRuleDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("management_id")]
        public int ManagementId { get; set; }

        [JsonPropertyName("management_name")]
        public string ManagementName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonPropertyName("sources")]
        public List<string> Sources { get; set; } = [];

        [JsonPropertyName("destinations")]
        public List<string> Destinations { get; set; } = [];

        [JsonPropertyName("services")]
        public List<string> Services { get; set; } = [];

        [JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonPropertyName("owner_name")]
        public string? OwnerName { get; set; }

        [JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonPropertyName("last_recertified")]
        public DateTime? LastRecertified { get; set; }

        [JsonPropertyName("last_recertifier")]
        public string? LastRecertifier { get; set; }

        [JsonPropertyName("recertified")]
        public bool Recertified { get; set; }

        [JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        [JsonPropertyName("raw_json")]
        public string RawJson { get; set; } = "";

        [JsonPropertyName("imported_at")]
        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    }
}
