using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Proxy
{
    public class ProxyRecertificationRequest
    {
        [JsonProperty("rule_ids"), JsonPropertyName("rule_ids")]
        public List<string> RuleIds { get; set; } = [];

        [JsonProperty("recertifier_dn"), JsonPropertyName("recertifier_dn")]
        public string RecertifierDn { get; set; } = "";

        [JsonProperty("comment"), JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        [JsonProperty("next_recert_date"), JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }
    }
}
