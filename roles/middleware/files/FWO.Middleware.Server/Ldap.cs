﻿using FWO.Middleware.Server.Data;
using FWO.Logging;
using Novell.Directory.Ldap;
using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;


namespace FWO.Middleware.Server
{
    public class Ldap
    {
        // The following properties are retrieved from the database api:
        // ldap_server ldap_port ldap_search_user ldap_tls ldap_tenant_level ldap_connection_id ldap_search_user_pwd ldap_searchpath_for_users ldap_searchpath_for_roles    

        [JsonPropertyName("ldap_server")]
        public string Address { get; set; }

        [JsonPropertyName("ldap_port")]
        public int Port { get; set; }

        [JsonPropertyName("ldap_search_user")]
        public string SearchUser { get; set; }

        [JsonPropertyName("ldap_tls")]
        public bool Tls { get; set; }

        [JsonPropertyName("ldap_tenant_level")]
        public int TenantLevel { get; set; }

        [JsonPropertyName("ldap_search_user_pwd")]
        public string SearchUserPwd { get; set; }

        [JsonPropertyName("ldap_searchpath_for_users")]
        public string UserSearchPath { get; set; }

        [JsonPropertyName("ldap_searchpath_for_roles")]
        public string RoleSearchPath { get; set; }

        [JsonPropertyName("ldap_write_user")]
        public string WriteUser { get; set; }

        [JsonPropertyName("ldap_write_user_pwd")]
        public string WriteUserPwd { get; set; }

        private const int timeOutInMs = 200; // TODO: MOVE TO API

        /// <summary>
        /// Builds a connection to the specified Ldap server.
        /// </summary>
        /// <returns>Connection to the specified Ldap server.</returns>
        private LdapConnection Connect()
        {
            try
            {
                LdapConnection connection = new LdapConnection { SecureSocketLayer = Tls, ConnectionTimeout = timeOutInMs };
                if (Tls) connection.UserDefinedServerCertValidationDelegate += (object sen, X509Certificate cer, X509Chain cha, SslPolicyErrors err) => true;  // todo: allow cert validation                
                connection.Connect(Address, Port);

                return connection;
            }

            catch (Exception exception)
            {
                throw new Exception($"Error while trying to reach LDAP server {Address}:{Port}", exception);
            }
        }

        public string ValidateUser(User user)
        {
            Log.WriteInfo("User Validation", $"Validating User: \"{user.Name}\" ...");
            try         
            {
                // Connecting to Ldap
                using (LdapConnection connection = Connect())
                {
                    // Authenticate as search user
                    connection.Bind(SearchUser, SearchUserPwd);

                    // Search for users in ldap with same name as user to validate
                    LdapSearchResults possibleUsers = (LdapSearchResults)connection.Search(
                        UserSearchPath,             // top-level path under which to search for user
                        LdapConnection.ScopeSub,    // search all levels beneath
                        $"(|(&(sAMAccountName={user.Name})(objectClass=person))(&(objectClass=inetOrgPerson)(uid:dn:={user.Name})))", // matching both AD and openldap filter
                        null,
                        typesOnly: false
                    );

                    while (possibleUsers.HasMore())
                    {
                        LdapEntry currentUser = possibleUsers.Next();
                      
                        try
                        {
                            Log.WriteDebug("User Validation", $"Trying to validate user with distinguished name: \"{ currentUser.Dn}\" ...");

                            // Try to authenticate as user with given password
                            connection.Bind(currentUser.Dn, user.Password);

                            // If authentication was successful (user is bound)
                            if (connection.Bound)
                            {
                                // Return ldap dn
                                Log.WriteDebug("User Validation", $"\"{ currentUser.Dn}\" successfully authenticated.");
                                return currentUser.Dn;
                            }

                            else
                            {
                                // this will probably never be reached as an error is thrown before
                                // Incorrect password - do nothing, assume its another user with the same username
                                Log.WriteDebug("User Validation", $"Found user with matching uid but different pwd: \"{ currentUser.Dn}\".");
                            }
                        }
                        catch (LdapException exc)
                        {
                            if (exc.ResultCode == 49)  // 49 = InvalidCredentials
                                Log.WriteDebug("Duplicate user", $"Found user with matching uid but different pwd: \"{ currentUser.Dn}\".");
                            else
                                Log.WriteError("Ldap exception", "Unexpected error while trying to validate user \"{ currentUser.Dn}\".");
                        } 
                    }
                }
            }
            catch (Exception exception)
            {
                Log.WriteError("Non-LDAP exception", "Unexpected error while trying to validate user", exception);
            }

            Log.WriteInfo("Invalid Credentials", $"Invalid login credentials - could not authenticate user \"{ user.Name}\".");
            return "";
        }

        public string[] GetRoles(string userDn)
        {
            List<string> userRoles = new List<string>();

            // If this Ldap is containing roles
            if (RoleSearchPath != null)
            {
                // Connect to Ldap
                using (LdapConnection connection = Connect())
                {     
                    // Authenticate as search user
                    connection.Bind(SearchUser, SearchUserPwd);

                    // Search for Ldap roles in given directory          
                    int searchScope = LdapConnection.ScopeSub; // TODO: Correct search scobe?
                    string searchFilter = $"(&(objectClass=groupOfUniqueNames)(cn=*))";
                    LdapSearchResults searchResults = (LdapSearchResults)connection.Search(RoleSearchPath, searchScope, searchFilter, null, false);                

                    // Foreach found role
                    foreach (LdapEntry entry in searchResults)
                    {
                        Log.WriteDebug("Ldap Roles", $"Try to get roles from ldap entry {entry.GetAttribute("cn").StringValue}");

                        // Get dn of users having current role
                        LdapAttribute roleMembers = entry.GetAttribute("uniqueMember");
                        string[] roleMemberDn = roleMembers.StringValueArray;

                        // Foreach user 
                        foreach (string currentDn in roleMemberDn)
                        {
                            Log.WriteDebug("Ldap Roles", $"Checking if current Dn: \"{currentDn}\" is user Dn. Then user has current role.");

                            // Check if current user dn is matching with given user dn => Given user has current role
                            if (currentDn == userDn)
                            {
                                // Get role name and add it to list of roles of given user
                                string role = entry.GetAttribute("cn").StringValue;
                                userRoles.Add(role);
                                break;
                            }
                        }
                    }
                }
            }

            Log.WriteDebug($"Found the following roles for user {userDn}:", string.Join("\n", userRoles));
            return userRoles.ToArray();
        }

        public List<KeyValuePair<string, List<string>>> GetAllRoles()
        {
            List<KeyValuePair<string, List<string>>> roleUsers = new List<KeyValuePair<string, List<string>>>();

            // If this Ldap is containing roles
            if (RoleSearchPath != null)
            {
                // Connect to Ldap
                using (LdapConnection connection = Connect())
                {     
                    // Authenticate as search user
                    connection.Bind(SearchUser, SearchUserPwd);

                    // Search for Ldap roles in given directory          
                    int searchScope = LdapConnection.ScopeSub; // TODO: Correct search scobe?
                    string searchFilter = $"(&(objectClass=groupOfUniqueNames)(cn=*))";
                    LdapSearchResults searchResults = (LdapSearchResults)connection.Search(RoleSearchPath, searchScope, searchFilter, null, false);                

                    // Foreach found role
                    foreach (LdapEntry entry in searchResults)
                    {
                        List<string> users = new List<string>();
                        string[] roleMemberDn = entry.GetAttribute("uniqueMember").StringValueArray;
                        foreach (string currentDn in roleMemberDn)
                        {
                            if (currentDn != "")
                            {
                                users.Add(currentDn);
                            }
                        }
                        roleUsers.Add(new KeyValuePair<string, List<string>>(entry.Dn, users));
                    }
                }
            }
            return roleUsers;
        }

        public bool AddUserToRole(string userDn, string role)
        {
            Log.WriteInfo("Add User to Role", $"Trying to add User: \"{userDn}\" to Role: \"{role}\"");
            bool userAdded = false;
            try         
            {
                // Connecting to Ldap
                using (LdapConnection connection = Connect())
                {
                    // Authenticate as write user
                    connection.Bind(WriteUser, WriteUserPwd);

                    // Add a new value to the description attribute
                    LdapAttribute attribute = new LdapAttribute("uniquemember", userDn);
                    LdapModification[] mods = { new LdapModification(LdapModification.Add, attribute) }; 

                    try
                    {
                        //Modify the entry in the directory
                        connection.Modify ( role, mods );
                        userAdded = true;
                    }
                    catch(Exception exception)
                    {
                        Log.WriteInfo("Modify Role", $"maybe role doesn't exist in this LDAP: {exception.ToString()}");
                    }
                }
            }
            catch (Exception exception)
            {
                Log.WriteError("Non-LDAP exception", "Unexpected error while trying to add user", exception);
            }

            return userAdded;
        }
    }
}