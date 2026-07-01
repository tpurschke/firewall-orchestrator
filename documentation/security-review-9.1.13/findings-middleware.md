# Security Review — Middleware Role (FWO 9.1.13)

## Summary

The Middleware role is the highest-value target in FWO (it mints the JWTs that the
whole platform trusts), and overall it is in reasonably good shape. The JWT design is
sound: tokens are signed with RS256 using an RSA-2048 private key that is generated
per-installation and stored outside the source tree (`/etc/secrets`), and the token
validator enforces signature, issuer, audience and lifetime. Authorization is
consistently applied — every REST action carries an explicit `[Authorize(Roles=...)]`
attribute, there are no `[AllowAnonymous]` endpoints, role-assignment and user-mutation
endpoints are admin-only, the password-change endpoint has an explicit IDOR check, and
refresh tokens are stored/rotated as SHA-256 hashes with single-use semantics.
LDAP filter construction is properly escaped (RFC 4515), process execution uses
`ArgumentList` with `UseShellExecute=false`, and import file paths are confined to an
allow-listed root with extension allow-listing.

The one clear defect is that LDAP TLS is established with certificate validation
unconditionally disabled (callback returns `true`), which undermines the confidentiality
and integrity of every LDAP bind — including the credential-validation binds that back
authentication. The remainder of the findings are lower-severity hardening items
(exception messages returned to callers, a committed test key pair shipped in the role,
CBC-without-MAC symmetric encryption of stored secrets, permissive `AllowedHosts`, and
admin token delegation without tenant scoping).

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 1 |
| Medium | 3 |
| Low | 3 |
| Informational | 3 |

## Findings

### [High] LDAP TLS certificate validation is disabled (accept-any-cert)

- **ID:** MW-01
- **Category:** Cryptography / TLS / Authentication
- **Location:** `FWO.Middleware.Server/LdapBasic.cs:52`
- **Description:** When an LDAP connection is configured with TLS, the connection is built
  with a remote-certificate-validation callback that unconditionally returns `true`:
  `if (Tls) ldapOptions.ConfigureRemoteCertificateValidationCallback((...) => true);`.
  Every LDAP bind performed by the middleware — search-user binds, write-user binds, and
  critically the per-user credential-validation binds in `CredentialsValid` /
  `GetLdapEntry` — flows over this connection. Because the certificate is never
  verified, the "TLS" protection is reduced to opportunistic encryption with no
  authentication of the server endpoint.
- **Impact:** An attacker positioned on the network path between the middleware and an
  LDAP/AD server (ARP/DNS spoofing, rogue DHCP, compromised switch, malicious upstream)
  can transparently man-in-the-middle the TLS session. They can then (a) harvest cleartext
  user credentials as they are bound for validation, (b) harvest the privileged
  search-user and write-user credentials, and (c) forge bind results to authenticate as an
  arbitrary user. This defeats authentication for the entire platform, and is
  particularly severe for externally-federated LDAPs reached over untrusted networks.
- **Recommendation:** Perform real certificate validation. Validate against the system /
  a configured trust store, reject on `SslPolicyErrors` other than `None`, and only relax
  validation behind an explicit, per-LDAP-connection opt-in flag (e.g. "trust self-signed")
  that is off by default and clearly surfaced in the UI. Do not ship a build with the
  callback hardcoded to `true`.

### [Medium] Authentication and token endpoints return raw exception messages to callers

- **ID:** MW-02
- **Category:** Information Disclosure / Authentication
- **Location:** `FWO.Middleware.Server/Controllers/AuthenticationTokenController.cs:84`,
  `:130`, `:175`, `:238` (all `catch { return BadRequest(ex.Message); }`);
  `FWO.Middleware.Server/Controllers/AuthenticationServerController.cs:56`
- **Description:** The token-issuance endpoints (`Get`, `GetForUser`, `GetTokenPair`,
  `GetTokenPairForUser`) and the LDAP `TestConnection` endpoint catch exceptions and return
  the raw `Exception.Message` (and for `GetForUser`, wrapped LDAP/validation error text) in
  the HTTP response body. These are unauthenticated or low-privilege reachable surfaces.
- **Impact:** An attacker probing the login endpoint can extract internal detail —
  LDAP server addresses/ports, DN structure, decrypt/bind failure specifics, whether a
  username exists in a directory — that aids reconnaissance and username enumeration
  against the authentication backend. It also risks leaking stack/config details on
  unexpected exceptions.
- **Recommendation:** Return a generic, constant error ("Invalid credentials" /
  "Authentication failed") to the client for auth failures and log the detailed exception
  server-side only. Avoid differentiating "user not found" from "bad password" in the
  response.

### [Medium] Stored secrets use AES-CBC without authentication (no MAC)

- **ID:** MW-03
- **Category:** Cryptography
- **Location:** `FWO.Encryption/AesEnc.cs:130` (encrypt), `:170` (decrypt) — consumed by
  the middleware in `LdapBasic.cs:110` (`TryBind`) and `LdapGroupHandling.cs:95`
- **Description:** LDAP search/write-user passwords and other stored secrets are encrypted
  with AES-256-CBC and a random IV prepended to the ciphertext, but there is no
  authentication tag / HMAC over the ciphertext. The mode is unauthenticated encryption.
  Additionally the key is taken as the raw UTF-8 bytes of the main-key file
  (`Encoding.UTF8.GetBytes(key)`), so key strength depends entirely on the file contents
  rather than a KDF. (This code lives in `roles/lib`, outside the strict middleware path,
  but it is the encryption the middleware relies on for every LDAP credential.)
- **Impact:** CBC without a MAC is malleable and exposes the system to padding-oracle /
  bit-flipping classes of attack if any decryption oracle is exposed, and provides no
  tamper detection on secrets at rest. An attacker with write access to the stored
  ciphertext can silently corrupt or manipulate it.
- **Recommendation:** Migrate to an authenticated cipher (AES-GCM / `AesGcm`, or
  encrypt-then-MAC with HMAC-SHA256). Derive the AES key from the main key material with a
  KDF (e.g. HKDF/PBKDF2) and enforce a fixed key length, instead of using the raw file
  bytes.

### [Medium] Admin token delegation issues tokens for any user without tenant scoping

- **ID:** MW-04
- **Category:** Authorization / Privilege Delegation
- **Location:** `FWO.Middleware.Server/Controllers/AuthenticationTokenController.cs:99`
  (`GetTokenPairForUser`) and `:189` (`GetAsyncForUser`)
- **Description:** These endpoints let a user who authenticates with the global `admin`
  role obtain a fully-usable access token (with the target user's roles, tenant and
  visibility claims) for **any** target user identified by name or DN, without the target
  user's password. The only gate is that the caller holds `Roles.Admin`. There is no
  tenant boundary — a global admin can impersonate any user in any tenant. The refresh
  token is (correctly, per #4654) withheld for the `GetTokenPairForUser` variant, but the
  legacy `GetForUser` variant still mints a full-lifetime access token.
- **Impact:** This is by-design admin functionality, but it means a single compromised or
  malicious global-admin credential yields silent impersonation of every user/tenant,
  bypassing tenant isolation. The `GetForUser` legacy path additionally grants a
  full-lifetime access token, widening the window.
- **Recommendation:** Keep the capability admin-gated but (a) ensure every issued
  delegated token is audit-logged with actor + target (this is done — good), (b) consider
  restricting delegation to within the admin's tenant unless the admin is a global/tenant0
  admin, and (c) deprecate/remove the legacy `GetForUser`/`Get` string-returning variants
  in favor of the delegated, non-refreshable `GetTokenPairForUser` path with a short
  lifetime.

### [Low] Committed fixed JWT key pair shipped inside the middleware role

- **ID:** MW-05
- **Category:** Secrets Management / JWT
- **Location:** `roles/middleware/files/jwt_test_private_key.pem`,
  `roles/middleware/files/jwt_test_public_key.pem`; deployment in
  `roles/middleware/tasks/create_auth_secrets.yml:60-78`
- **Description:** A real RSA-2048 private/public key pair is committed to the repository.
  It is only deployed when the installer variable `testkeys` is true; the default
  (`inventory/group_vars/middlewareserver.yml: testkeys: no`) generates a fresh random key,
  and a "SECURITY WARNING" banner is printed when test keys are enabled
  (`roles/common/tasks/main.yml:41`). So this is not a production-key-exposure by default.
- **Impact:** Any installation deployed with `testkeys=yes` (test/CI/demo, or an operator
  who copies test inventory) uses a globally-known private key. Anyone with the repo can
  forge arbitrary JWTs — including `admin` / `middleware-server` roles — against such an
  installation, achieving full auth bypass and privilege escalation.
- **Recommendation:** Keep the strong default and warning. Additionally, treat the checked-in
  private key as sensitive: consider generating ephemeral test keys at test time instead of
  committing a static private key, and add a guard that refuses to start with the known
  test key outside of an explicitly-flagged test environment.

### [Low] Permissive `AllowedHosts` wildcard

- **ID:** MW-06
- **Category:** Configuration / Hardening
- **Location:** `FWO.Middleware.Server/appsettings.json` (`"AllowedHosts": "*"`)
- **Description:** The host-filtering middleware is configured to accept any Host header.
  Combined with HTTPS redirection being commented out in `Program.cs:155`
  (`//app.UseHttpsRedirection();`), the server relies entirely on the surrounding reverse
  proxy / network placement for host and transport hardening.
- **Impact:** Increases exposure to Host-header spoofing / cache-poisoning-style issues if
  the service is ever reachable directly rather than only via the intended proxy.
- **Recommendation:** Restrict `AllowedHosts` to the expected middleware hostname(s) and
  confirm TLS termination/redirection is enforced at the proxy for all environments.

### [Low] Verbose authentication logging of user identifiers and directory topology

- **ID:** MW-07
- **Category:** Sensitive Data in Logs
- **Location:** `FWO.Middleware.Server/Controllers/AuthenticationTokenController.cs:487`,
  `:496`, `:565`, `:602`, `:630`; `FWO.Middleware.Server/LdapBasic.cs:283`, `:356`, `:362`
- **Description:** Authentication paths log usernames, full DNs, resolved group DNs,
  target LDAP address:port, and "found user with matching uid but different pwd" messages.
  No passwords or tokens are logged (verified), and much of this is intentional audit
  content, so this is low severity — but at `Info`/`Debug` level it does emit a fairly
  complete picture of the directory topology and per-user membership.
- **Impact:** Anyone with read access to logs gains a map of users, groups and directory
  structure, useful for lateral-movement reconnaissance.
- **Recommendation:** Keep DN/group detail at `Debug` only, ensure production log level and
  log-file permissions are restrictive, and avoid the "matching uid but different pwd"
  wording that confirms account existence.

### [Informational] LDAP filter escaping and DN comparison are implemented correctly

- **ID:** MW-08
- **Category:** LDAP Injection
- **Location:** `FWO.Middleware.Server/LdapBasic.cs:135` (`EscapeFilterValue`), `:178`
  (`EscapeSearchPattern`), `:187`/`:210` (filter builders);
  `FWO.Middleware.Server/LdapGroupHandling.cs:446`
- **Description:** All user-influenced values interpolated into LDAP search filters are
  passed through RFC 4515 escaping (`\`, `*`, `(`, `)`, `NUL`), with wildcard-preserving
  segment escaping for search patterns. DN membership comparisons use a normalizing
  comparer (`DistName.NormalizeDnForComparison`) rather than naive string equality. No
  injectable filter construction was found. Noted as a positive.
- **Impact:** None.
- **Recommendation:** Maintain this pattern for any new filter-building code.

### [Informational] Import script execution is not shell-interpolated and paths are confined

- **ID:** MW-09
- **Category:** Command Injection / Path Traversal
- **Location:** `FWO.Middleware.Server/DataImportBase.cs:66-127`;
  `FWO.Basics/ImportPathPolicy.cs`
- **Description:** `RunImportScript` uses `ProcessStartInfo` with `UseShellExecute = false`
  and an explicit `ArgumentList` (arguments parsed with a custom quote-aware splitter, no
  shell), so classic shell metacharacter injection is not possible. The executable and
  data paths are validated by `ImportPathPolicy`, which resolves the full path and rejects
  anything outside `/usr/local/fworch/scripts/customizing`, and restricts extensions to
  `.json`/`.py`. Noted as a positive.
- **Impact:** None.
- **Recommendation:** Continue routing all import-source paths through `ImportPathPolicy`.

### [Informational] JWT validation and refresh-token handling are well designed

- **ID:** MW-10
- **Category:** JWT / Session Management
- **Location:** `FWO.Middleware.Server/Program.cs:103-117`;
  `FWO.Middleware.Server/JwtWriter.cs`;
  `FWO.Middleware.Server/Controllers/AuthenticationTokenController.cs:772-855`
- **Description:** Tokens are signed with RS256 (`SecurityAlgorithms.RsaSha256`); no `none`
  algorithm or HS/RS confusion path exists. The validator enforces
  `RequireSignedTokens`, `RequireExpirationTime`, `ValidateAudience`, `ValidateIssuer`,
  `ValidateLifetime`, with a fixed issuer/audience and the RSA public key. Refresh tokens
  are 64 bytes from `RandomNumberGenerator`, stored only as SHA-256 hashes, single-use
  (revoke-before-reissue with an affected-row check), and delegated tokens deliberately
  omit refresh tokens (#4654). Anonymous tokens are short-lived (15 min). Noted as a
  positive.
- **Impact:** None.
- **Recommendation:** None; keep the anonymous/access lifetimes conservative.

## Positive Observations

- **RS256 with per-install keys.** Asymmetric signing, no symmetric-key or `none`-algorithm
  weaknesses; production keys are generated fresh and stored under `/etc/secrets`, not in
  source.
- **Strict, consistent authorization.** Every controller action has an explicit
  `[Authorize(Roles=...)]`; no `[AllowAnonymous]`; user/role/tenant mutations are admin-only.
- **IDOR protection on password change** (`CallerCanChangePassword` verifies the caller's
  `x-hasura-user-id` claim against the target).
- **Refresh-token hygiene** — CSPRNG generation, hashed-at-rest storage, single-use
  rotation, delegated tokens non-refreshable.
- **Robust LDAP filter escaping** (RFC 4515) and normalized DN comparison for membership.
- **Roles are sourced only from internal LDAPs** (`HasRoleHandling`), so an external
  federated directory cannot grant platform roles, and deterministic LDAP selection avoids
  a rogue-source auth bypass.
- **Safe process execution and path confinement** for the import subsystem.
- **Comprehensive audit logging** (`Log.WriteAudit`) on token issuance, delegation, refresh,
  revoke, and LDAP/role/user administration, including actor and target identity — without
  logging passwords or token secrets.
