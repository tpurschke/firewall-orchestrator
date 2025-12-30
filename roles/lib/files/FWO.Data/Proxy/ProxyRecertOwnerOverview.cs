using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Proxy
{
    public class ProxyRecertOwnerOverview
    {
        [JsonProperty("owner_id"), JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonProperty("owner_name"), JsonPropertyName("owner_name")]
        public string OwnerName { get; set; } = "";

        [JsonProperty("next_recert_date"), JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonProperty("rules_due"), JsonPropertyName("rules_due")]
        public List<ProxyRule> RulesDue { get; set; } = [];
    }
}
