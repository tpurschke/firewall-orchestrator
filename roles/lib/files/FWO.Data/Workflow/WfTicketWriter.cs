﻿using System.Text.Json.Serialization; 
using Newtonsoft.Json;

namespace FWO.Data.Workflow
{
    public class WfTicketWriter
    {

        [JsonProperty("data"), JsonPropertyName("data")]
        public List<WfReqTaskWriter> Tasks { get; set; } = [];


        public WfTicketWriter(WfTicket ticket)
        {
            foreach(var reqtask in ticket.Tasks)
            {
                Tasks.Add(new WfReqTaskWriter(reqtask));
            }
        }
    }
}
