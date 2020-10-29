using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FWO.Ui.Data.API
{
    public class DeviceType
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        // [JsonPropertyName("predefinedObjects")]
        // public ??? PredefinedObjects { get; set; }

        public string NameVersion()
        {
            return Name + " " + Version;
        }
    }
}