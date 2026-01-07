using System.Text.Json.Serialization;

namespace FWO.GenericGatewayImporter.Models
{
    public class ProxyRecertOwnerOverview
    {
        [JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonPropertyName("owner_name")]
        public string OwnerName { get; set; } = "";

        [JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonPropertyName("rules_due")]
        public List<ProxyRuleDocument> RulesDue { get; set; } = [];
    }
}
