using FWO.Api.Client;
using FWO.Api.Client.Queries;
using FWO.Basics;
using FWO.Data;
using FWO.Data.Middleware;
using FWO.Logging;
using Microsoft.AspNetCore.Mvc;
using Novell.Directory.Ldap;
using System.Data;
using System.Security.Authentication;

namespace FWO.Middleware.Server.Controllers
{
	/// <summary>
	/// Authentication token generation. Token is of type JSON web token (JWT).
	/// </summary>
	[ApiController]
	[Route("api/[controller]")]
	public class AuthenticationTokenController : ControllerBase
	{
		private readonly JwtWriter jwtWriter;
		private readonly List<Ldap> ldaps;
		private readonly ApiConnection apiConnection;

		/// <summary>
		/// Constructor needing jwt writer, ldap list and connection
		/// </summary>
		public AuthenticationTokenController(JwtWriter jwtWriter, List<Ldap> ldaps, ApiConnection apiConnection)
		{
			this.jwtWriter = jwtWriter;
			this.ldaps = ldaps;
			this.apiConnection = apiConnection;
		}

		/// <summary>
		/// Generates an authentication token (jwt) given valid credentials.  
		/// </summary>
		/// <remarks>
		/// Username (required)&#xA;
		/// Password (required)
		/// </remarks>
		/// <param name="parameters">Credentials</param>
		/// <returns>Jwt, if credentials are vaild.</returns>
		[HttpPost("Get")]
		public async Task<ActionResult<string>> GetAsync([FromBody] AuthenticationTokenGetParameters parameters)
		{
			try
			{
				UiUser? user = null;

				if (parameters != null)
				{
					string? username = parameters.Username;
					string? password = parameters.Password;

					// Create User from given parameters / If user does not provide login data => anonymous login
					if (username != null && password != null)
						user = new UiUser { Name = username, Password = password };
				}

				AuthManager authManager = new (jwtWriter, ldaps, apiConnection);

				// Authenticate user
				string jwt = await authManager.AuthorizeUserAsync(user, validatePassword: true);

				return Ok(jwt);
			}
			catch (Exception e)
			{
				return BadRequest(e.Message);
			}
		}

		/// <summary>
		/// Generates an authentication token (jwt) for the specified user given valid admin credentials.  
		/// </summary>
		/// <remarks>
		/// AdminUsername (required) - Example: "admin" &#xA;
		/// AdminPassword (required) - Example: "password" &#xA;
		/// Lifetime (optional) - Example: "365.12:02:00" ("days.hours:minutes:seconds") &#xA;
		/// TargetUserDn OR TargetUserName (required) - Example: "uid=demo_user,ou=tenant0,ou=operator,ou=user,dc=fworch,dc=internal" OR "demo_user" 
		/// </remarks>
		/// <param name="parameters">Admin Credentials, Lifetime, User</param>
		/// <returns>User jwt, if credentials are vaild.</returns>
		[HttpPost("GetForUser")]
		public async Task<ActionResult<string>> GetAsyncForUser([FromBody] AuthenticationTokenGetForUserParameters parameters)
		{
			try
			{
				string adminUsername = parameters.AdminUsername;
				string adminPassword = parameters.AdminPassword;
				TimeSpan lifetime = parameters.Lifetime;
				string targetUserName = parameters.TargetUserName;
				string targetUserDn = parameters.TargetUserDn;

				AuthManager authManager = new (jwtWriter, ldaps, apiConnection);
				UiUser adminUser = new() { Name = adminUsername, Password = adminPassword };
				// Check if admin valids are valid
				try
				{
					await authManager.AuthorizeUserAsync(adminUser, validatePassword: true);
					if (!adminUser.Roles.Contains(Roles.Admin))
					{
						throw new AuthenticationException("Provided credentials do not belong to a user with role admin.");
					}
				}
				catch (Exception e)
				{
					throw new AuthenticationException("Error while validating admin credentials: " + e.Message);
				}
				// Check if username is valid and generate jwt
				try
				{
					UiUser targetUser = new() { Name = targetUserName, Dn = targetUserDn };
					string jwt = await authManager.AuthorizeUserAsync(targetUser, validatePassword: false, lifetime);
					return Ok(jwt);
				}
				catch (Exception e)
				{
					throw new AuthenticationException("Error while validating user credentials (user name): " + e.Message);
				}
			}
			catch (Exception e)
			{
				return BadRequest(e.Message);
			}
		}
	}

	class AuthManager
	{
		private readonly JwtWriter jwtWriter;
		private readonly List<Ldap> ldaps;
		private readonly ApiConnection apiConnection;

		public AuthManager(JwtWriter jwtWriter, List<Ldap> ldaps, ApiConnection apiConnection)
		{
			this.jwtWriter = jwtWriter;
			this.ldaps = ldaps;
			this.apiConnection = apiConnection;
		}

		/// <summary>
		/// Validates user credentials and retrieves user information. Returns a jwt containing it.
		/// </summary>
		/// <param name="user">User to validate. Must contain username / dn and password if <paramref name="validatePassword"/> == true.</param>
		/// <param name="validatePassword">Check password if true.</param>
		/// <param name="lifetime">Set the lifetime of the jwt (optional)</param>
		/// <returns>Jwt, User infos (dn, email, groups, roles, tenant), if credentials are valid.</returns>
		public async Task<string> AuthorizeUserAsync(UiUser? user, bool validatePassword, TimeSpan? lifetime = null)
		{
			// Case: anonymous user
			if (user == null)
				return await jwtWriter.CreateJWT();

			// Retrieve ldap entry for user (throws exception if credentials are invalid)
			(LdapEntry ldapUser, Ldap ldap) = await GetLdapEntry(user, validatePassword);

			// Get dn of user
			user.Dn = ldapUser.Dn;

			// Get email of user
			user.Email = ldap.GetEmail(ldapUser);
			user.Firstname = ldap.GetFirstName(ldapUser);
			user.Lastname = ldap.GetLastName(ldapUser);

			// Get groups of user
			user.Groups = await GetGroups(ldapUser, ldap);
			Log.WriteDebug("Get Groups", $"Found groups for user: {string.Join("; ", user.Groups)}");

			// Get roles of user
			user.Roles = await GetRoles(user);

			// Get tenant of user
			user.Tenant = await GetTenantAsync(ldapUser, ldap);
			Log.WriteDebug("Get Tenants", $"Found tenant for user: {user.Tenant?.Name ?? ""}");

			// Remember the hosting ldap
			user.LdapConnection.Id = ldap.Id;

			// Create JWT for validated user with roles and tenant
			return await jwtWriter.CreateJWT(user, lifetime);
		}

		public async Task<List<string>> GetGroups(LdapEntry ldapUser, Ldap ldap)
		{
			List<string> userGroups = ldap.GetGroups(ldapUser);
			if (!ldap.IsInternal())
			{
				object groupsLock = new ();
				List<Task> ldapRoleRequests = [];

				foreach (Ldap currentLdap in ldaps)
				{
					if (currentLdap.IsInternal())
					{
						ldapRoleRequests.Add(Task.Run(async() =>
						{
							// Get groups from current Ldap
							List<string> currentGroups = await currentLdap.GetGroups([ldapUser.Dn]);
							lock (groupsLock)
							{
								currentGroups = Array.ConvertAll(currentGroups.ToArray(), x => "cn=" + x + "," + currentLdap.GroupSearchPath).ToList();
								userGroups.AddRange(currentGroups);
							}
						}));
					}
				}
				await Task.WhenAll(ldapRoleRequests);
			}
			return userGroups;
		}

		// why do we have a local GetLdapEntry method here?
		public async Task<(LdapEntry, Ldap)> GetLdapEntry(UiUser user, bool validatePassword)
		{
			Log.WriteDebug("User Authentication", $"Trying to get ldap entry for user: {user.Name + " " + user.Dn}...");

			if (user.Dn == "" && user.Name == "")
			{
				throw new Exception("A0001 Invalid credentials. Username / User DN must not be empty.");
			}
			else
			{
				LdapEntry? ldapEntry = null;
				Ldap? ldap = null;
				List<Task> ldapValidationRequests = [];
				object dnLock = new();
				bool ldapFound = false;

				foreach (Ldap currentLdap in ldaps.Where(x => x.Active))
				{
					ldapValidationRequests.Add(Task.Run(async() =>
					{
						Log.WriteDebug("User Authentication", $"Trying to authenticate {user.Name + " " + user.Dn} against LDAP {currentLdap.Address}:{currentLdap.Port} ...");

						try
						{
							LdapEntry? currentLdapEntry = await currentLdap.GetLdapEntry(user, validatePassword);

							if (currentLdapEntry != null)
							{
								// User was successfully authenticated via this LDAP
								if(user.Name == Roles.Importer)
								{
									Log.WriteDebug("User Authentication", $"User {user.Name + " " + currentLdapEntry.Dn} found.");
								}
								else
								{
									Log.WriteInfo("User Authentication", $"User {user.Name + " " + currentLdapEntry.Dn} found.");
								}

								lock (dnLock)
								{
									if (!ldapFound)
									{
										ldapEntry = currentLdapEntry;
										ldap = currentLdap;
										ldapFound = true;
									}
								}
							}
						}
						catch
						{
							// this Ldap can't validate user, but maybe another one can
						}
					}));
				}

				while (ldapValidationRequests.Count > 0)
				{
					Task finishedDnRequest = await Task.WhenAny(ldapValidationRequests);

					if (ldapEntry != null && ldap != null)
					{
						return (ldapEntry, ldap);
					}

					ldapValidationRequests.Remove(finishedDnRequest);
				}
				Log.WriteInfo("User Authentication", $"User {user.Name} not found in any connected LDAP.");
			}

			// Invalid User Credentials
			throw new Exception("A0002 Invalid credentials");
		}

		public async Task<List<string>> GetRoles(UiUser user)
		{
			List<string> dnList =
            [
                user.Dn,
                .. user.Groups, // search all groups where user is member for group associated roles
            ];

			List<string> userRoles = [];
			object rolesLock = new();

			List<Task> ldapRoleRequests = [];

			foreach (Ldap currentLdap in ldaps)
			{
				// if current Ldap has roles stored
				if (currentLdap.HasRoleHandling())
				{
					ldapRoleRequests.Add(Task.Run(async() =>
					{
						// Get roles from current Ldap
						List<string> currentRoles = await currentLdap.GetRoles(dnList);

						lock (rolesLock)
						{
							userRoles.AddRange(currentRoles);
						}
					}));
				}
			}

			await Task.WhenAll(ldapRoleRequests);

			// If no roles found
			if (userRoles.Count == 0)
			{
				// Use anonymous role
				Log.WriteWarning("Missing roles", $"No roles for user \"{user.Dn}\" could be found. Using anonymous role.");
				userRoles.Add(Roles.Anonymous);
			}

			return userRoles;
		}

		public async Task<Tenant?> GetTenantAsync(LdapEntry user, Ldap ldap)
		{
			Tenant tenant = new();
			if (ldap.TenantId != null)
			{
				Log.WriteDebug("Get Tenant", $"This LDAP has the fixed tenant {ldap.TenantId.Value}");
				tenant.Id = ldap.TenantId.Value;
			}
			else
			{
				tenant.Name = new DistName(user.Dn).GetTenantNameViaLdapTenantLevel(ldap.TenantLevel);
				if (tenant.Name == "")
				{
					return null;
				}
				Log.WriteDebug("Get Tenant", $"extracting TenantName as: {tenant.Name} from {user.Dn}");
				if (tenant.Name == ldap.GlobalTenantName)
				{
					tenant.Id = GlobalConst.kTenant0Id;
				}
				else
				{
					var tenNameObj = new { tenant_name = tenant.Name };
					Tenant[] tenants = await apiConnection.SendQueryAsync<Tenant[]>(AuthQueries.getTenantId, tenNameObj, "getTenantId");
					if (tenants.Length > 0)
					{
						tenant.Id = tenants[0].Id;
					}
					else
					{
						// tenant unknown: create in db. This should only happen for users from external Ldaps
						try
						{
							var Variables = new
							{
								name = tenant.Name,
								project = "",
								comment = "",
								viewAllDevices = false,
								create = DateTime.Now
							};
							ReturnId[]? returnIds = (await apiConnection.SendQueryAsync<ReturnIdWrapper>(AuthQueries.addTenant, Variables)).ReturnIds;
							if (returnIds != null)
							{
								tenant.Id = returnIds[0].NewId;
								// no further search for devices etc necessary
								return tenant;
							}
							else
							{
								return null;
							}
						}
						catch (Exception exception)
						{
							Log.WriteError("AddTenant", $"Adding Tenant {tenant.Name} locally failed: {exception.Message}");
							return null;
						}
					}
				}
			}
			await AddDevices(apiConnection, tenant);

			return tenant;
		}

		// the following method adds device visibility information to a tenant (fetched from API)
        private async Task AddDevices(ApiConnection conn, Tenant tenant)
        {
            var tenIdObj = new { tenantId = tenant.Id };

            Device[] deviceIds = await conn.SendQueryAsync<Device[]>(AuthQueries.getVisibleDeviceIdsPerTenant, tenIdObj, "getVisibleDeviceIdsPerTenant");
            tenant.VisibleGatewayIds = Array.ConvertAll(deviceIds, device => device.Id);

            Management[] managementIds = await conn.SendQueryAsync<Management[]>(AuthQueries.getVisibleManagementIdsPerTenant, tenIdObj, "getVisibleManagementIdsPerTenant");
            tenant.VisibleManagementIds = Array.ConvertAll(managementIds, management => management.Id);
        }
	}
}
