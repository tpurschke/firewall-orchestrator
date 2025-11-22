using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Config.File;
using FWO.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace FWO.Api.Client;

public class TrustedIssuer
{
    [JsonProperty("issuer"), JsonPropertyName("issuer")]
    public string Issuer { get; set; } = "";

    [JsonProperty("publicKey"), JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = "";
}

public record TrustedIssuerValidation(List<string> Issuers, Dictionary<string, SecurityKey> SigningKeys);

/// <summary>
/// Helper utilities to resolve trusted JWT issuers from configuration.
/// </summary>
public static class JwtIssuerHelper
{
    public const string ConfigKey = "trustedJwtIssuers";
    private const string LogContext = "Jwt issuers";

    /// <summary>
    /// Parse issuer list from a JSON string while ensuring the built-in issuer is present.
    /// </summary>
    public static List<TrustedIssuer> ParseIssuers(string? rawIssuerList)
    {
        List<TrustedIssuer> issuers = [];

        if (!string.IsNullOrWhiteSpace(rawIssuerList))
        {
            try
            {
                issuers = System.Text.Json.JsonSerializer.Deserialize<List<TrustedIssuer>>(rawIssuerList) ?? [];
            }
            catch (Exception exception)
            {
                Log.WriteError(LogContext, "Could not parse trusted JWT issuers from config. Trying legacy format.", exception);
                try
                {
                    List<string> legacy = System.Text.Json.JsonSerializer.Deserialize<List<string>>(rawIssuerList) ?? [];
                    issuers = legacy.Select(l => new TrustedIssuer { Issuer = l }).ToList();
                }
                catch (Exception second)
                {
                    Log.WriteError(LogContext, "Legacy issuer parsing failed. Falling back to defaults.", second);
                }
            }
        }

        return NormalizeIssuers(issuers);
    }

    public static List<TrustedIssuer> ParseIssuers(IEnumerable<TrustedIssuer>? issuers)
    {
        return NormalizeIssuers(issuers?.ToList() ?? []);
    }

    private static List<TrustedIssuer> NormalizeIssuers(List<TrustedIssuer> issuers)
    {
        Dictionary<string, TrustedIssuer> issuerMap = new(StringComparer.Ordinal);

        foreach (TrustedIssuer issuer in issuers)
        {
            if (issuer == null || string.IsNullOrWhiteSpace(issuer.Issuer))
            {
                continue;
            }
            string trimmedIssuer = issuer.Issuer.Trim();
            issuerMap[trimmedIssuer] = new TrustedIssuer
            {
                Issuer = trimmedIssuer,
                PublicKey = issuer.PublicKey?.Trim() ?? ""
            };
        }

        if (!issuerMap.ContainsKey(FWO.Basics.JwtConstants.Issuer))
        {
            issuerMap[FWO.Basics.JwtConstants.Issuer] = new TrustedIssuer
            {
                Issuer = FWO.Basics.JwtConstants.Issuer,
                PublicKey = ConfigFile.JwtPublicKeyPem
            };
        }
        else if (string.IsNullOrWhiteSpace(issuerMap[FWO.Basics.JwtConstants.Issuer].PublicKey))
        {
            issuerMap[FWO.Basics.JwtConstants.Issuer].PublicKey = ConfigFile.JwtPublicKeyPem;
        }

        return issuerMap.Values.ToList();
    }

    /// <summary>
    /// Retrieve the trusted issuer list from the config service.
    /// </summary>
    public static async Task<List<TrustedIssuer>> FetchIssuers(ApiConnection apiConnection)
    {
        try
        {
            ConfigItemValue[]? configItems = await apiConnection.SendQueryAsync<ConfigItemValue[]>(ConfigQueries.getConfigItemByKey, new { key = ConfigKey });
            string? rawIssuerList = configItems.FirstOrDefault()?.Value;
            return ParseIssuers(rawIssuerList);
        }
        catch (Exception exception)
        {
            Log.WriteError(LogContext, "Could not fetch trusted JWT issuers from API. Falling back to defaults.", exception);
            return ParseIssuers((string?)null);
        }
    }

    public static TrustedIssuerValidation BuildValidationData(IEnumerable<TrustedIssuer> issuers, RsaSecurityKey defaultKey)
    {
        List<TrustedIssuer> normalized = ParseIssuers(issuers);
        Dictionary<string, SecurityKey> signingKeys = new(StringComparer.Ordinal);

        foreach (TrustedIssuer issuer in normalized)
        {
            if (string.IsNullOrWhiteSpace(issuer.Issuer))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(issuer.PublicKey))
            {
                signingKeys[issuer.Issuer] = defaultKey;
                continue;
            }

            try
            {
                signingKeys[issuer.Issuer] = KeyImporter.ExtractKeyFromPem(issuer.PublicKey, false)
                    ?? defaultKey;
            }
            catch (Exception exception)
            {
                Log.WriteError(LogContext, $"Could not import public key for issuer {issuer.Issuer}. Falling back to default key.", exception);
                signingKeys[issuer.Issuer] = defaultKey;
            }
        }

        List<string> validIssuers = signingKeys.Keys.ToList();
        return new TrustedIssuerValidation(validIssuers, signingKeys);
    }

    public static SecurityKey ResolveSigningKey(string token, TrustedIssuerValidation validation)
    {
        JsonWebToken parsedToken = new(token);
        if (validation.SigningKeys.TryGetValue(parsedToken.Issuer, out SecurityKey? signingKey))
        {
            return signingKey;
        }
        return validation.SigningKeys.Values.First();
    }

    private sealed class ConfigItemValue
    {
        [JsonProperty("config_value"), JsonPropertyName("config_value")]
        public string? Value { get; set; }
    }
}
