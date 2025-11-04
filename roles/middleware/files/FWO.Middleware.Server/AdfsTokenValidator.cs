using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Linq;
using FWO.Config.Api;
using FWO.Config.Api.Data;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace FWO.Middleware.Server
{
    public class AdfsTokenValidator
    {
        private readonly GlobalConfig globalConfig;
        private readonly SemaphoreSlim configLock = new(1, 1);
        private ConfigurationManager<OpenIdConnectConfiguration>? configurationManager;

        public AdfsTokenValidator(GlobalConfig globalConfig)
        {
            this.globalConfig = globalConfig;
            this.globalConfig.OnChange += (_, _) => ResetConfiguration();
        }

        public async Task<ClaimsPrincipal> ValidateIdTokenAsync(string idToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idToken))
            {
                throw new ArgumentException("idToken must not be empty.", nameof(idToken));
            }

            AdfsSettings adfsSettings = globalConfig.AdfsSettings;
            if (!adfsSettings.Enabled)
            {
                throw new InvalidOperationException("ADFS SSO is disabled.");
            }

            JwtSecurityTokenHandler handler = new();
            if (!handler.CanReadToken(idToken))
            {
                throw new SecurityTokenException("Unable to read ADFS id token.");
            }

            OpenIdConnectConfiguration configuration = await GetConfigurationAsync(adfsSettings, cancellationToken);

            TokenValidationParameters validationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = configuration.Issuer ?? adfsSettings.Authority,
                ValidateAudience = true,
                ValidAudience = adfsSettings.ClientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            return handler.ValidateToken(idToken, validationParameters, out _);
        }

        private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(AdfsSettings adfsSettings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(adfsSettings.MetadataAddress))
            {
                throw new InvalidOperationException("ADFS metadata address is not configured.");
            }

            await configLock.WaitAsync(cancellationToken);
            try
            {
                configurationManager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                    adfsSettings.MetadataAddress,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = adfsSettings.MetadataAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase) });

                return await configurationManager.GetConfigurationAsync(cancellationToken);
            }
            finally
            {
                configLock.Release();
            }
        }

        private void ResetConfiguration()
        {
            configurationManager = null;
        }

        public string? GetUserIdentifier(ClaimsPrincipal principal)
        {
            if (principal == null)
            {
                return null;
            }

            AdfsSettings settings = globalConfig.AdfsSettings;
            List<string> claimCandidates = [];
            if (!string.IsNullOrWhiteSpace(settings.UserIdClaim))
            {
                claimCandidates.Add(settings.UserIdClaim);
            }

            claimCandidates.AddRange(new[]
            {
                ClaimTypes.Upn,
                ClaimTypes.Email,
                ClaimTypes.Name
            });

            foreach (string claim in claimCandidates)
            {
                Claim? match = principal.Claims.FirstOrDefault(c => string.Equals(c.Type, claim, StringComparison.OrdinalIgnoreCase));
                if (match != null && !string.IsNullOrWhiteSpace(match.Value))
                {
                    return match.Value;
                }
            }

            return principal.Identity?.Name;
        }
    }
}
