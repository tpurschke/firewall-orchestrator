using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Identity of a generic config item (management and uid), which stays the same across item versions.
    /// </summary>
    public class GenericItemRef
    {
        [JsonProperty("mgm_id"), JsonPropertyName("mgm_id")]
        public int MgmId { get; set; }

        [JsonProperty("item_uid"), JsonPropertyName("item_uid")]
        public string ItemUid { get; set; } = "";
    }

    /// <summary>
    /// Recertification decision for one generic config item (generic.recertification, append-only).
    /// </summary>
    public class GenericRecertification
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonProperty("config_item_id"), JsonPropertyName("config_item_id")]
        public long ConfigItemId { get; set; }

        [JsonProperty("owner_id"), JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonProperty("user_dn"), JsonPropertyName("user_dn")]
        public string UserDn { get; set; } = "";

        [JsonProperty("recertified"), JsonPropertyName("recertified")]
        public bool Recertified { get; set; }

        [JsonProperty("recert_date"), JsonPropertyName("recert_date")]
        public DateTime RecertDate { get; set; }

        [JsonProperty("comment"), JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonProperty("next_recert_date"), JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonProperty("owner_recert_id"), JsonPropertyName("owner_recert_id")]
        public long? OwnerRecertId { get; set; }

        [JsonProperty("config_item"), JsonPropertyName("config_item")]
        public GenericItemRef? ConfigItem { get; set; }
    }

    /// <summary>
    /// Owner level recertification of all generic config items of an owner (generic.owner_recertification).
    /// </summary>
    public class GenericOwnerRecertification
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonProperty("owner_id"), JsonPropertyName("owner_id")]
        public int OwnerId { get; set; }

        [JsonProperty("user_dn"), JsonPropertyName("user_dn")]
        public string UserDn { get; set; } = "";

        [JsonProperty("recertified"), JsonPropertyName("recertified")]
        public bool Recertified { get; set; }

        [JsonProperty("recert_date"), JsonPropertyName("recert_date")]
        public DateTime RecertDate { get; set; }

        [JsonProperty("comment"), JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonProperty("next_recert_date"), JsonPropertyName("next_recert_date")]
        public DateTime? NextRecertDate { get; set; }

        [JsonProperty("report_id"), JsonPropertyName("report_id")]
        public long? ReportId { get; set; }
    }
}
