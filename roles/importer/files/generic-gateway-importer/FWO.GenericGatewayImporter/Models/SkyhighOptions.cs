namespace FWO.GenericGatewayImporter.Models
{
    public class SkyhighOptions
    {
        public string BaseUrl { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public List<int> ManagementIds { get; set; } = [];
        public string ManagementNamePattern { get; set; } = "";
        public string RulesEndpoint { get; set; } = "/api/v1/proxy/rules";
        public string RulesetsEndpoint { get; set; } = "rulesets?topLevelOnly=true";
        public string RulesetExportSuffix { get; set; } = "/export";
        public string LoginEndpoint { get; set; } = "login";
        public string LogoutEndpoint { get; set; } = "logout";
        public string AuthHeaderName { get; set; } = "Authorization";
        public string AuthScheme { get; set; } = "Bearer";
        public string Token { get; set; } = "";
        public bool VerifyTls { get; set; } = true;
        public string RulesJsonPath { get; set; } = "";
    }
}
