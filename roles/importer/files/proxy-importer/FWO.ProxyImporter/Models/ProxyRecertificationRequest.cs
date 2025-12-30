using System.Text.Json.Serialization;

namespace FWO.ProxyImporter.Models
{
    public class ProxyRecertificationRequest
    {
        [JsonPropertyName("rule_ids")]
        public List<string> RuleIds { get; set; } = [];

        [JsonPropertyName("recertifier_dn")]
        public string RecertifierDn { get; set; } = "";

        [JsonPropertyName("comment")]
        public string Comment { get; set; } = "";

        [JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }
    }
}
