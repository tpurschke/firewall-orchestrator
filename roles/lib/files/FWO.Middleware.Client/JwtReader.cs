﻿using FWO.Config.File;
using FWO.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace FWO.Middleware.Client
{
	public class JwtReader
	{
		private readonly string jwtString;
		private JsonWebToken? jwt;

		private readonly RsaSecurityKey jwtPublicKey;
		private readonly string JwtValidation = "Jwt Validation";
		private readonly string JwtNotValidated = "Jwt was not validated yet.";


		public JwtReader(string jwtString)
		{
			// Save jwt string 
			this.jwtString = jwtString;

			// Get public key from config lib
			jwtPublicKey = ConfigFile.JwtPublicKey ?? throw new ArgumentException("Jwt public key could not be read form config file.");
		}

		/// <summary>
		/// Checks if JWT in HTTP header contains role.
		/// </summary>
		/// <param name="roleName">Role name to check.</param>
		/// <returns>True if JWT contains specified role, otherwise false.</returns>
		public bool ContainsRole(string roleName)
		{
			Log.WriteDebug($"{roleName} Role Jwt", "Checking Jwt for admin role.");

			if (jwt == null)
				throw new ArgumentException(nameof(jwt), JwtNotValidated);

			return jwt.Claims.FirstOrDefault(claim => claim.Type == "role" && claim.Value == roleName) != null;
		}

		/// <summary>
		/// Checks if JWT in HTTP header contains role in x-hasura-allowed-roles.
		/// </summary>
		/// <param name="roleName">Role name to check.</param>
		/// <returns>True if JWT contains specified role in x-hasura-allowed-roles, otherwise false.</returns>
		public bool ContainsAllowedRole(string roleName)
		{
			Log.WriteDebug($"{roleName} Role Jwt", "Checking Jwt for allowed role.");

			if (jwt == null)
				throw new ArgumentException(nameof(jwt), JwtNotValidated);

			return jwt.Claims.FirstOrDefault(claim => claim.Type == "x-hasura-allowed-roles" && claim.Value == roleName) != null;
		}

		public async Task<bool> Validate()
		{
			try
			{
				TokenValidationParameters validationParameters = new TokenValidationParameters
				{
					RequireExpirationTime = true,
					RequireSignedTokens = true,
					ValidateAudience = true,
					ValidateIssuer = true,
					ValidateLifetime = true,
					ValidAudience = FWO.Basics.JwtConstants.Audience,
					ValidIssuer = FWO.Basics.JwtConstants.Issuer,
					IssuerSigningKey = jwtPublicKey,
					
				};

				JsonWebTokenHandler handler = new ();
				TokenValidationResult tokenValidationResult = await handler.ValidateTokenAsync(jwtString, validationParameters);
				if (tokenValidationResult.IsValid)
				{
					jwt = tokenValidationResult.SecurityToken as JsonWebToken;
					Log.WriteDebug(JwtValidation, "Jwt was successfully validated.");
					return true;					
				}
				return false;
			}

			catch (SecurityTokenExpiredException)
			{
				Log.WriteDebug(JwtValidation, "Jwt lifetime expired.");
				return false;
			}
			catch (SecurityTokenInvalidSignatureException InvalidSignatureException)
			{
				Log.WriteError(JwtValidation, $"Jwt signature could not be verified. Potential attack!", InvalidSignatureException);
				return false;
			}
			catch (SecurityTokenInvalidAudienceException InvalidAudienceException)
			{
				Log.WriteError(JwtValidation, $"Jwt audience incorrect.", InvalidAudienceException);
				return false;
			}
			catch (SecurityTokenInvalidIssuerException InvalidIssuerException)
			{
				Log.WriteError(JwtValidation, $"Jwt issuer incorrect.", InvalidIssuerException);
				return false;
			}
			catch (Exception UnexpectedError)
			{
				Log.WriteError(JwtValidation, $"Unexpected problem while trying to verify Jwt", UnexpectedError);
				return false;
			}
		}

		public Claim[] GetClaims()
		{
			Log.WriteDebug("Claims Jwt", "Reading claims from Jwt.");
			if (jwt == null)
				throw new ArgumentException(nameof(jwt), JwtNotValidated);

			return jwt.Claims.ToArray();
		}

		public TimeSpan TimeUntilExpiry()
		{
			if (jwt == null)
				throw new ArgumentException(nameof(jwt), JwtNotValidated);

			return jwt.ValidTo - DateTime.UtcNow;
		}

		public string GetRole()
		{
			if (jwt == null)
				throw new ArgumentException(nameof(jwt), JwtNotValidated);
			return jwt.Claims.FirstOrDefault(claim => claim.Type == "role")?.Value ?? "";
		}
	}
}
