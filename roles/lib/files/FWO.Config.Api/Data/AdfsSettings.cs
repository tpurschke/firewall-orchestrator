using FWO.Encryption;

namespace FWO.Config.Api.Data
{
    public class AdfsSettings
    {
        public bool Enabled { get; init; }
        public bool AutoRedirect { get; init; }
        public string Authority { get; init; } = "";
        public string MetadataAddress { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientSecret { get; init; } = "";
        public string CallbackPath { get; init; } = "";
        public string SignedOutCallbackPath { get; init; } = "";
        public string UserIdClaim { get; init; } = "";
        public string[] Scopes { get; init; } = [];

        public static AdfsSettings FromConfig(ConfigData config)
        {
            string metadataAddress = string.IsNullOrWhiteSpace(config.AdfsMetadataAddress) && !string.IsNullOrWhiteSpace(config.AdfsAuthority)
                ? $"{config.AdfsAuthority.TrimEnd('/')}/.well-known/openid-configuration"
                : config.AdfsMetadataAddress;

            string[] scopes = string.IsNullOrWhiteSpace(config.AdfsScopes)
                ? []
                : config.AdfsScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new AdfsSettings
            {
                Enabled = config.AdfsEnabled && !string.IsNullOrWhiteSpace(config.AdfsAuthority) && !string.IsNullOrWhiteSpace(config.AdfsClientId),
                AutoRedirect = config.AdfsAutoRedirect,
                Authority = config.AdfsAuthority,
                MetadataAddress = metadataAddress,
                ClientId = config.AdfsClientId,
                ClientSecret = AesEnc.TryDecrypt(config.AdfsClientSecret ?? "", true),
                CallbackPath = config.AdfsCallbackPath,
                SignedOutCallbackPath = config.AdfsSignedOutCallbackPath,
                UserIdClaim = string.IsNullOrWhiteSpace(config.AdfsUserIdClaim)
                    ? "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn"
                    : config.AdfsUserIdClaim,
                Scopes = scopes
            };
        }
    }
}
