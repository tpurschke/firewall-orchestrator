using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace FWO.Data.Report
{
    /// <summary>
    /// Selection of the reports on generic (non-firewall) config items.
    /// </summary>
    public class GenericFilter
    {
        /// <summary>
        /// Managements of generic device types to report on; empty means all the user may see.
        /// </summary>
        [JsonProperty("management_ids"), JsonPropertyName("management_ids")]
        public List<int> ManagementIds { get; set; } = [];

        public GenericFilter()
        { }

        public GenericFilter(GenericFilter genericFilter)
        {
            ManagementIds = [.. genericFilter.ManagementIds];
        }
    }
}
