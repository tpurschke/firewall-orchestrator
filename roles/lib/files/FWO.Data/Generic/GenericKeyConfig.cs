using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Settings of one generic (non-firewall) device type (generic.device_type_key_config).
    /// The config items themselves have the same core fields for all device types, see <see cref="GenericConfigLine"/>.
    /// </summary>
    public class GenericKeyConfig
    {
        [JsonProperty("dev_typ_id"), JsonPropertyName("dev_typ_id")]
        public int DevTypId { get; set; }

        /// <summary>
        /// Whether the owner value of an item is an owner name ("name") or an external app id ("ext_id").
        /// </summary>
        [JsonProperty("owner_match"), JsonPropertyName("owner_match")]
        public string OwnerMatch { get; set; } = "name";

        /// <summary>
        /// Whether the device type delivers the last use of its items, which the unused items report needs.
        /// </summary>
        [JsonProperty("has_last_used"), JsonPropertyName("has_last_used")]
        public bool HasLastUsed { get; set; }

        /// <summary>
        /// Keys of the item attributes shown as additional columns in reports.
        /// </summary>
        [JsonProperty("reportable_fields"), JsonPropertyName("reportable_fields")]
        public List<string> ReportableFields { get; set; } = [];

        [JsonProperty("device_type"), JsonPropertyName("device_type")]
        public DeviceType? DeviceType { get; set; }

        /// <summary>
        /// Name of the device type for display, the id if the device type is not known.
        /// </summary>
        public string DeviceTypeName => DeviceType != null ? $"{DeviceType.Name} {DeviceType.Version}".Trim() : DevTypId.ToString();
    }
}
