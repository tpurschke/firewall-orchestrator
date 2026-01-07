namespace FWO.GenericGatewayImporter.Models
{
    public class F5BigIpOptions
    {
        public string BaseUrl { get; set; } = "";
        public List<int> ManagementIds { get; set; } = [];
        public string ManagementNamePattern { get; set; } = "";
        public string RulesEndpoint { get; set; } = "/mgmt/tm/ltm";
        public string AuthHeaderName { get; set; } = "Authorization";
        public string AuthScheme { get; set; } = "Bearer";
        public string Token { get; set; } = "";
        public bool VerifyTls { get; set; } = true;
        public string RulesJsonPath { get; set; } = "";
    }
}
