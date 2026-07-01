# Security Review — Lib (Shared Libraries) Role (FWO 9.1.13)

## Summary

The shared libraries are structurally sound in most higher-risk areas: JWT validation (`FWO.Middleware.Client/JwtReader.cs`) uses RSA signatures with full issuer/audience/lifetime/signature checks; all JSON deserialization uses `System.Text.Json` or Newtonsoft **without** polymorphic `TypeNameHandling` (no insecure-deserialization/RCE surface); GraphQL calls are fully parameterized (variables, not string concatenation → no query injection); no hardcoded secrets, no `BinaryFormatter`, no `Process.Start`, no LDAP filter construction, and no `System.Random` used for keys/IVs. The AES IV is generated per-message with the platform CSPRNG.

The dominant weakness is that **TLS certificate validation is disabled across every network client in this role**. The reusable `RestApiClient` base defaults to not checking certificates, and every concrete client (Check Point, FortiManager, Middleware, Tufin SecureChange) inherits that default, while the GraphQL client and the SMTP mailer hard-code accept-any-certificate callbacks. These are shared libraries transporting firewall admin credentials, JWTs, and SMTP credentials, so a single MITM position yields credential theft and data tampering. The secondary weakness is unauthenticated AES-CBC (no MAC) with the key used as raw UTF-8 bytes and no KDF.

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 3 |
| Medium   | 2 |
| Low      | 2 |
| Informational | 2 |

## Findings

### [HIGH] GraphQL API client disables TLS certificate validation unconditionally
- **ID:** LIB-01
- **Category:** TLS/MITM
- **Location:** `roles/lib/files/FWO.Api.Client/GraphQlApiConnection.cs:40` and `:48`
- **Description:** `CreateClient` sets `ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true` and, for websockets, `RemoteCertificateValidationCallback += (message, cert, chain, errors) => true`. Both accept every certificate; there is no flag to enforce validation. This is the primary data-plane connection to the Hasura GraphQL API and carries the bearer JWT in the `Authorization` header.
- **Impact:** A network MITM can transparently intercept the TLS session, steal the JWT (session hijack at the authenticated role), and read/tamper with all policy/config data over GraphQL.
- **Recommendation:** Remove the accept-all callbacks; use default chain/host validation. For a private CA/pinned cert, trust that CA explicitly. Any "insecure" mode must be an explicit, off-by-default flag with a prominent warning.

### [HIGH] All REST clients accept any TLS certificate by default (RestApiClient)
- **ID:** LIB-02
- **Category:** TLS/MITM
- **Location:** `roles/lib/files/FWO.Api.Client/RestApiClient.cs:16` and `:29-36`
- **Description:** The base constructor defaults `checkCertificates = false`, and `CreateRestClient` installs `RemoteCertificateValidationCallback += (…, sslErrors) => !CheckCertificates || sslErrors == SslPolicyErrors.None;`. With the default this is `!false || …` → always `true`. Every subclass constructs the base without `checkCertificates: true`: `MiddlewareClient.cs:11`, `CheckPointAPI.cs:20`, `FortiManagerAPI.cs:10`, `SCClient.cs:13`. No caller anywhere enables cert checking.
- **Impact:** Every REST integration is MITM-able by default: Middleware client (JWTs/auth), Check Point and FortiManager auto-discovery (firewall admin credentials in the login body — see LIB-03), and Tufin SecureChange (Basic-auth ticketing creds). Enables credential/token theft and response tampering.
- **Recommendation:** Default `checkCertificates = true` (fail-closed); allow disabling only per-connection via explicit admin opt-in, surfaced in UI/logs. Internal Middleware calls should always validate against the deployed CA.

### [HIGH] SMTP mailer disables TLS certificate validation ("accept all SSL certificates")
- **ID:** LIB-03
- **Category:** TLS/MITM
- **Location:** `roles/lib/files/FWO.Mail/MailerMailKit.cs:71` and `:80`
- **Description:** For both `StartTls` and `Tls` modes the code sets `smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;` (`//accept all SSL certificates`) before connecting, then sends the SMTP username and decrypted password via `AuthenticateAsync` (line 91). Disabled validation means the "encrypted" transport does not authenticate the server.
- **Impact:** A MITM can present any cert, terminate TLS, and capture the SMTP credentials, then relay/alter outbound mail. Credential theft + email spoofing.
- **Recommendation:** Remove the accept-all callbacks so MailKit validates normally. If a self-signed internal relay must be supported, add an explicit off-by-default option and prefer trusting the relay CA/cert.

### [MEDIUM] Unauthenticated AES-CBC encryption without a key-derivation function
- **ID:** LIB-04
- **Category:** Crypto
- **Location:** `roles/lib/files/FWO.Encryption/AesEnc.cs:130-148` (encrypt), `:170-207` (decrypt), key load `:103-116`
- **Description:** `AesEnc` protects stored secrets (device/import credentials, SMTP password, Tufin creds) with AES-CBC+PKCS7. (1) **No authentication** — CBC has no integrity/MAC; ciphertext is malleable and undetected if tampered (no HMAC/AEAD). (2) **Raw key, no KDF** — `aes.Key = Encoding.UTF8.GetBytes(key)` (lines 133, 186) uses the main-key file bytes directly with no salt/stretching. The IV itself is correctly generated per-message via `aes.GenerateIV()` (CSPRNG) and prepended; the issue is authentication + key handling, not the IV.
- **Impact:** No integrity on secrets at rest; ciphertext manipulation undetected. Binding the AES key to raw file bytes forgoes defense-in-depth and amplifies any partial key exposure.
- **Recommendation:** Move to AEAD (`AesGcm`/`ChaCha20Poly1305`) or add encrypt-then-MAC (HMAC-SHA256) over IV+ciphertext. Derive the encryption key from main-key material via HKDF/PBKDF2 with a stored salt. Version the ciphertext format for migration.

### [MEDIUM] Group-readable main encryption key file
- **ID:** LIB-05
- **Category:** Secrets / Key storage
- **Location:** `roles/lib/files/FWO.Encryption/AesEnc.cs:103-116`; path `roles/lib/files/FWO.Basics/GlobalConstants.cs:10`; installer mode `0640` at `roles/common/tasks/main.yml:466-473`
- **Description:** The single symmetric main key decrypting every stored secret is written to `/etc/fworch/secrets/main_key` with mode `0640` (owner+group readable), whereas the adjacent importer password file is `0600` (`roles/common/tasks/main.yml:443`). Any account in the file's group can read the master key. (Key material and its reader live in Lib; the mode is set by the installer role.)
- **Impact:** Broader-than-necessary local exposure of the master decryption key; a co-located/compromised group member can decrypt all managed-device credentials and integration secrets.
- **Recommendation:** Tighten to `0600` (or a documented least-privilege group if truly required) and confirm the runtime user does not need group-wide access.

### [LOW] Full JWT logged on validation failure / expiry
- **ID:** LIB-06
- **Category:** Secrets / Logging
- **Location:** `roles/lib/files/FWO.Middleware.Client/JwtReader.cs:90,107,116,125,134,143`
- **Description:** Expiry and every failure branch log the raw JWT (e.g. `Jwt lifetime expired: {jwtString}`, `Jwt signature could not be verified. Potential attack: {jwtString}`). A JWT is a bearer credential; even expired/invalid tokens carry claims, and echoing attacker-supplied tokens aids log injection/claim leakage.
- **Impact:** Sensitive token material/claims disclosed to logs; possible session material exposure under clock skew.
- **Recommendation:** Log only a JTI/correlation id or a truncated hash plus the reason; do not log full tokens.

### [LOW] Outbound request body logged at debug in SecureChange client
- **ID:** LIB-07
- **Category:** Secrets / Logging
- **Location:** `roles/lib/files/FWO.ExternalSystems/Tufin.SecureChange/SCClient.cs:31-46`
- **Description:** `DebugApiCallText` logs the full request body (`body = $"data: '{p.Value}'"`). The `Authorization` header is correctly excluded, but ticket bodies can carry sensitive topology data and future task types could place credentials in the body. Emitted whenever debug logging is on.
- **Impact:** Potential disclosure of sensitive request content to logs.
- **Recommendation:** Redact/truncate the body, or gate full-body logging behind an explicit off-by-default "trace payloads" switch.

### [INFORMATIONAL] TryEncrypt/TryDecrypt swallow crypto errors silently (fail-open)
- **ID:** LIB-08
- **Category:** Crypto / Robustness
- **Location:** `roles/lib/files/FWO.Encryption/AesEnc.cs:23-75,118-128`
- **Description:** `TryDecrypt` catches all exceptions and, when `returnOrigin` is set, returns the original ciphertext; `TryEncrypt` decides "already encrypted" solely by attempting a decrypt. With a missing/wrong key, callers (e.g. `smtp.AuthenticateAsync`, device login) may receive ciphertext-as-plaintext and send it over the wire, masking key/rotation failures.
- **Impact:** Fail-open behavior can leak ciphertext into credential fields and obscure misconfiguration.
- **Recommendation:** Distinguish "plaintext passthrough" from "decrypt failed (missing/wrong key)"; do not return ciphertext as a usable secret; surface key-load failures to callers that must not proceed.

### [INFORMATIONAL] Newtonsoft round-trip parse/re-serialize of report JSON (reviewed, safe)
- **ID:** LIB-09
- **Category:** Deserialization (defensive note)
- **Location:** `roles/lib/files/FWO.Report/ReportDevicesBase.cs:253-257`
- **Description:** `JsonConvert.DeserializeObject(...)` + `SerializeObject(..., Formatting.Indented)` only pretty-prints JSON the code just built. No `TypeNameHandling` → no gadget/polymorphic risk. Recorded to document it was reviewed and is safe.
- **Impact:** None.
- **Recommendation:** Optional: use `System.Text.Json` `WriteIndented` to avoid the round trip.

## Positive Observations
- Strict JWT validation (RSA key, signature/lifetime/audience/issuer enforced; no alg-confusion/"none").
- No insecure deserialization anywhere in the role (no `TypeNameHandling`, `BinaryFormatter`, XML/XXE).
- No injection surface in shared clients (parameterized GraphQL variables; no `Process.Start`; no dynamic LDAP filters).
- Correct IV handling in `AesEnc` (fresh CSPRNG IV per message, prepended; no reuse).
- No hardcoded secrets/default credentials; JWT and main keys loaded from files.
- Defensive `FWO.Basics/ImportPathPolicy.cs` checks import paths for world-writable/unsafe components.
- Credential-aware logging in places (`SCClient` omits `Authorization`; GraphQL query logging redacts variables).
