using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace FWO.Data.Middleware
{
    public class RefreshTokenInfo
    {
        [JsonProperty("user_id"), JsonPropertyName("user_id")]
        public int UserId { get; set; }

        [JsonProperty("expires_at"), JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }
    }
}
