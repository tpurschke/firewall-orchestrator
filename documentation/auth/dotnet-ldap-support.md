# dotnet core 3.x does not support ldap standard lib System.DirectoryServices on linux

## error

```console
Listening...
System.DirectoryServices is not supported on this platform. Stack Trace:    at System.DirectoryServices.DirectoryEntry..ctor(String path, String username, String password, AuthenticationTypes authenticationType)
   at FWO_Auth_Server.LdapServerConnection.Valid(String Username, String Password) in /home/tim/VisualStudioCodeProjects/fwo-tpurschke/firewall-orchestrator/roles/auth/files/FWO_Auth/FWO_Auth_Server/LdapServerConnection.cs:line 23
```

## alternatives
- use Novell.Directory.Ldap.NETStandard library
- replace by flask auth server
