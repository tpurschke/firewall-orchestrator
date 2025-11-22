using FWO.Api.Client;
using FWO.Basics;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using System.Security.Cryptography;
using System.Text;

namespace FWO.Test
{
    [TestFixture]
    [Parallelizable]
    internal class JwtIssuerHelperTest
    {
        [Test]
        public void UsesAllTrustedIssuerKeysForValidation()
        {
            const string secondaryIssuer = "https://issuer-two.example";

            using RSA defaultRsa = RSA.Create(2048);
            using RSA secondaryRsa = RSA.Create(2048);
            RsaSecurityKey defaultKey = new(defaultRsa);
            RsaSecurityKey secondaryKey = new(secondaryRsa);

            List<TrustedIssuer> issuers = new()
            {
                new TrustedIssuer { Issuer = FwoJwtConstants.Issuer, PublicKey = "" }, // should fall back to default key
                new TrustedIssuer { Issuer = secondaryIssuer, PublicKey = ToPem(secondaryRsa) }
            };

            TrustedIssuerValidation validationData = JwtIssuerHelper.BuildValidationData(issuers, defaultKey);

            string tokenFromDefaultIssuer = CreateToken(defaultKey, FwoJwtConstants.Issuer);
            string tokenFromSecondaryIssuer = CreateToken(secondaryKey, secondaryIssuer);

            Assert.That(ValidateToken(tokenFromDefaultIssuer, validationData), Is.True, "Default issuer token should validate with default key.");
            Assert.That(ValidateToken(tokenFromSecondaryIssuer, validationData), Is.True, "Secondary issuer token should validate with its configured key.");

            // Using a wrong key for the secondary issuer must break validation.
            List<TrustedIssuer> issuersWithWrongKey = new()
            {
                new TrustedIssuer { Issuer = FwoJwtConstants.Issuer, PublicKey = "" },
                new TrustedIssuer { Issuer = secondaryIssuer, PublicKey = ToPem(defaultRsa) } // wrong public key on purpose
            };
            TrustedIssuerValidation wrongValidation = JwtIssuerHelper.BuildValidationData(issuersWithWrongKey, defaultKey);
            Assert.That(ValidateToken(tokenFromSecondaryIssuer, wrongValidation), Is.False, "Mismatching public key should prevent validation.");
        }

        private static string CreateToken(RsaSecurityKey signingKey, string issuer)
        {
            JsonWebTokenHandler handler = new();
            SecurityTokenDescriptor descriptor = new()
            {
                Issuer = issuer,
                Audience = FwoJwtConstants.Audience,
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
            };

            return handler.CreateToken(descriptor);
        }

        private static bool ValidateToken(string token, TrustedIssuerValidation validationData)
        {
            TokenValidationParameters parameters = new()
            {
                ValidateIssuer = true,
                ValidIssuers = validationData.Issuers,
                ValidateAudience = false,
                RequireSignedTokens = true,
                IssuerSigningKeyResolver = (t, _, _, _) => [JwtIssuerHelper.ResolveSigningKey(t, validationData)]
            };

            JsonWebTokenHandler handler = new();
            TokenValidationResult result = handler.ValidateTokenAsync(token, parameters).GetAwaiter().GetResult();
            return result.IsValid;
        }

        private static string ToPem(RSA rsa)
        {
            string base64Key = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            StringBuilder builder = new();
            builder.AppendLine("-----BEGIN PUBLIC KEY-----");
            for (int i = 0; i < base64Key.Length; i += 64)
            {
                builder.AppendLine(base64Key.Substring(i, Math.Min(64, base64Key.Length - i)));
            }
            builder.Append("-----END PUBLIC KEY-----");
            return builder.ToString();
        }
    }
}
