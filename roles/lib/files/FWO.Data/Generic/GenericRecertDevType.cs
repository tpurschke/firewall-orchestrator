using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Generic
{
    /// <summary>
    /// Generic device type whose items users of an owner responsible type may recertify
    /// (generic.responsible_type_dev_type). A responsible type without entries may not recertify generic items.
    /// </summary>
    public class GenericRecertDevType
    {
        [JsonProperty("owner_responsible_type_id"), JsonPropertyName("owner_responsible_type_id")]
        public int OwnerResponsibleTypeId { get; set; }

        [JsonProperty("dev_typ_id"), JsonPropertyName("dev_typ_id")]
        public int DevTypId { get; set; }
    }
}
