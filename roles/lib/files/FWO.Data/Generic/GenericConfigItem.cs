using System.Text.Json.Serialization;
using Newtonsoft.Json;
using NewtonsoftJsonIgnore = Newtonsoft.Json.JsonIgnoreAttribute;
using SystemTextJsonIgnore = System.Text.Json.Serialization.JsonIgnoreAttribute;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Management a generic config item belongs to.
    /// </summary>
    public class GenericItemManagement
    {
        [JsonProperty("mgm_id"), JsonPropertyName("mgm_id")]
        public int Id { get; set; }

        [JsonProperty("mgm_name"), JsonPropertyName("mgm_name")]
        public string Name { get; set; } = "";

        [JsonProperty("dev_typ_id"), JsonPropertyName("dev_typ_id")]
        public int DevTypId { get; set; }
    }

    /// <summary>
    /// Owner of a generic config item, with what the recertification needs.
    /// </summary>
    public class GenericItemOwner
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonProperty("name"), JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonProperty("app_id_external"), JsonPropertyName("app_id_external")]
        public string? ExtAppId { get; set; }

        [JsonProperty("recert_interval"), JsonPropertyName("recert_interval")]
        public int? RecertInterval { get; set; }
    }

    /// <summary>
    /// Decision a recertifier selected for a generic config item on the recertification page.
    /// </summary>
    public enum GenericRecertAction
    {
        None = 0,
        Recertify = 1,
        Decertify = 2
    }

    /// <summary>
    /// Active version of a rule-like config item of a generic (non-firewall) device (generic.config_item).
    /// </summary>
    public class GenericConfigItem
    {
        [JsonProperty("id"), JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonProperty("mgm_id"), JsonPropertyName("mgm_id")]
        public int MgmId { get; set; }

        [JsonProperty("item_uid"), JsonPropertyName("item_uid")]
        public string ItemUid { get; set; } = "";

        [JsonProperty("item_name"), JsonPropertyName("item_name")]
        public string? ItemName { get; set; }

        [JsonProperty("item_order"), JsonPropertyName("item_order")]
        public int? ItemOrder { get; set; }

        /// <summary>
        /// The config item as delivered by the device. Nested values are JSON tokens of the deserializer,
        /// read them through <see cref="GenericConfigLine"/>.
        /// </summary>
        [JsonProperty("config_line"), JsonPropertyName("config_line")]
        public Dictionary<string, object?> ConfigLine { get; set; } = [];

        [JsonProperty("owner_id"), JsonPropertyName("owner_id")]
        public int? OwnerId { get; set; }

        [JsonProperty("last_used"), JsonPropertyName("last_used")]
        public DateTime? LastUsed { get; set; }

        [JsonProperty("first_seen"), JsonPropertyName("first_seen")]
        public DateTime FirstSeen { get; set; }

        [JsonProperty("created"), JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonProperty("removed"), JsonPropertyName("removed")]
        public long? Removed { get; set; }

        [JsonProperty("management"), JsonPropertyName("management")]
        public GenericItemManagement? Management { get; set; }

        [JsonProperty("owner"), JsonPropertyName("owner")]
        public GenericItemOwner? Owner { get; set; }

        /// <summary>
        /// Latest recertification decision of the item, over all of its versions.
        /// </summary>
        [NewtonsoftJsonIgnore, SystemTextJsonIgnore]
        public GenericRecertification? LastRecertification { get; set; }

        /// <summary>
        /// Date the item is due for recertification; null for decertified items.
        /// </summary>
        [NewtonsoftJsonIgnore, SystemTextJsonIgnore]
        public DateTime? NextRecertDate { get; set; }

        [NewtonsoftJsonIgnore, SystemTextJsonIgnore]
        public GenericRecertAction SelectedAction { get; set; } = GenericRecertAction.None;

        /// <summary>
        /// Whether the latest decision for the item was a decertification.
        /// </summary>
        public bool IsDecertified => LastRecertification != null && !LastRecertification.Recertified;

        /// <summary>
        /// Name for display: the item name, or the uid when the item has no name.
        /// </summary>
        public string DisplayName => string.IsNullOrWhiteSpace(ItemName) ? ItemUid : ItemName;
    }
}
