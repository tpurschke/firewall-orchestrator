# Security Review — Importer Role (FWO 9.1.13)

## Summary

The Importer role is a Python data-ingestion component that pulls firewall/manager configuration from vendor management APIs (Check Point R8x, FortiManager, FortiOS REST) or SSH (Cisco ASA), or from files/URLs, and persists the normalized result to the FWO GraphQL API. Overall the code shows good security habits: GraphQL mutations are fully parameterized (queries loaded from `.graphql` files, device data passed as `variables`, never string-interpolated) — no injection into the data layer; sensitive HTTP headers and GraphQL variables are redacted before logging; login payloads are excluded from debug logs; there is no `eval`/`exec`/`pickle`/`yaml.load`/dynamic import; and no XML is parsed.

The dominant weakness is transport security toward managed firewalls. TLS certificate validation for all vendor management-API traffic is driven by `importCheckCertificates`, which **ships disabled by default** (DB seed and C# config default both `False`). In that default state every HTTPS call is made with `verify=False`, and the Cisco ASA SSH path disables host-key verification unconditionally (`auth_strict_key: False`) regardless of that setting. An on-path attacker can MITM the importer↔firewall channel to capture privileged firewall credentials or tamper with ingested policy. Secondary issues: a fail-open secret-decryption helper and session tokens leaking into high-verbosity debug logs.

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High | 2 |
| Medium | 2 |
| Low | 3 |
| Informational | 3 |

## Findings

### [HIGH] SSH host-key verification disabled for Cisco ASA imports
- **ID:** IMP-01
- **Category:** TLS/MITM (SSH transport)
- **Location:** `roles/importer/files/importer/fw_modules/ciscoasa9/fwcommon.py:49`
- **Description:** `_connect_to_device` sets `"auth_strict_key": False` on the scrapli `GenericDriver`, disabling SSH host-key checking entirely. Unlike the HTTPS path, this is not gated by `importCheckCertificates` — always off. The importer then sends the ASA login username/password (`mgm_details.import_user`/`mgm_details.secret`) and, in `_ensure_enable_mode`, the enable password (`mgm_details.cloud_client_secret`, `fwcommon.py:107`) over that unauthenticated channel.
- **Impact:** An on-path/spoofing attacker can impersonate the ASA, complete the handshake, and harvest ASA admin credentials and enable secret — full firewall control; can also feed a forged running-config to poison FWO's policy view.
- **Recommendation:** Enable strict host-key checking with a managed known-hosts store, or at minimum gate the relaxed behavior behind an admin setting with secure-by-default.

### [HIGH] TLS certificate validation disabled by default for all vendor API traffic
- **ID:** IMP-02
- **Category:** TLS/MITM
- **Location:** `roles/importer/files/importer/fw_modules/checkpointR8x/cp_getter.py:37`, `roles/importer/files/importer/fw_modules/fortiadom5ff/fmgr_getter.py:34`, `roles/importer/files/importer/fw_modules/fortiosmanagementREST/fos_getter.py:54`, `roles/importer/files/importer/fwo_file_import.py:90` (driver: `fwo_globals.py:2`; default: `roles/database/files/sql/creation/fworch-fill-stm.sql:15`, `roles/lib/files/FWO.Config.Api/Data/ConfigData.cs:67`)
- **Description:** All vendor HTTPS calls pass `verify=fwo_globals.verify_certs`, populated from DB key `importCheckCertificates` (`import_main_loop.py:180` / `import_mgm.py:93`: `== "True"`). That key ships `'False'` in the DB seed and defaults `false` in `ConfigData.cs`, so out of the box `verify_certs` is `False` and every Check Point/FortiManager/FortiOS request skips cert validation. `urllib3.disable_warnings()` suppresses the warning that would reveal the insecure state.
- **Impact:** In default config an on-path attacker can MITM the importer↔manager TLS channel, steal decrypted management credentials sent at login, and tamper with returned configuration to falsify FWO's policy/compliance view. Warning suppression hides it.
- **Recommendation:** Default `importCheckCertificates` to `True` with a proper trust store; scope exceptions per-management; log a clear warning whenever verification is disabled.

### [MEDIUM] Secret decryption fails open, returning ciphertext instead of erroring
- **ID:** IMP-03
- **Category:** Secrets / Cryptography
- **Location:** `roles/importer/files/importer/fwo_encrypt.py:39-44` (consumed at `model_controllers/management_controller.py:198,206`)
- **Description:** `decrypt()` wraps decryption in a broad `except Exception` that logs a warning and **returns the original (still-encrypted) input** rather than raising. The caller guards with `try/except -> SecretDecryptionFailedError`, but `decrypt()` never raises, so a wrong `main_key`, corrupted key, or tampered ciphertext is silently swallowed and raw ciphertext propagates as `mgm_details.secret`. AES is CBC without any authentication tag (`fwo_encrypt.py:21`) — no integrity check on either side.
- **Impact:** Key/rotation errors or ciphertext tampering are masked; importer then logs in using ciphertext-as-password (opaque auth failures), and line 43 dumps a full traceback around the crypto op. Fail-open crypto weakens tamper detection of stored credentials.
- **Recommendation:** Re-raise on failure; never return ciphertext as plaintext. Move to an authenticated cipher (AES-GCM) or HMAC over ciphertext, coordinated with the middleware.

### [MEDIUM] Unrestricted local file read / URL fetch from config-controlled path
- **ID:** IMP-04
- **Category:** Path Traversal / SSRF
- **Location:** `roles/importer/files/importer/fwo_file_import.py:79-104` (path source: `common.py:387` `get_config_uri`, from `mgm_details.hostname`/`domain_name`)
- **Description:** `read_file` takes `import_state.import_file_name`; if it starts with `http(s)://` it fetches over the network, if `file://` or a bare path it opens that arbitrary local path with `open(filename)`. The value comes from the management's hostname/domain field or the `-i/--in_file` CLI arg. No host allow-listing on the URL branch (SSRF, incl. cloud-metadata/internal services) and no confinement of the local-file branch (a `file:///etc/...` value reads any file the importer user can read, then surfaces it as "config").
- **Impact:** A config-write actor can make the importer fetch arbitrary internal URLs (SSRF) or read arbitrary local files (e.g. `/usr/local/fworch/etc/secrets/*`). Requires DB/config write privilege, hence Medium.
- **Recommendation:** Confine the file branch to a resolved-and-validated import directory; validate/allow-list URL host+scheme; reject `file://` outside the import area.

### [LOW] FortiManager session token logged at debug level 3
- **ID:** IMP-05
- **Category:** Secrets (logging)
- **Location:** `roles/importer/files/importer/fw_modules/fortiadom5ff/fmgr_getter.py:38-60`
- **Description:** After login the session id is injected into the JSON-RPC payload (`json_payload["session"] = sid`). Non-login calls are logged with the full payload at debug 3 unless it contains the substring `"pass"` (line 49). Post-login payloads contain `session` but not `pass`, so the FortiManager session token is logged verbatim.
- **Impact:** Anyone with read access to importer debug logs can recover and replay a live FortiManager session token.
- **Recommendation:** Redact `session` (and known token keys) before logging, mirroring the FWO-API redaction.

### [LOW] Check Point session id (X-chkp-sid) logged on API error path
- **ID:** IMP-06
- **Category:** Secrets (logging)
- **Location:** `roles/importer/files/importer/fw_modules/checkpointR8x/cp_getter.py:24-51`
- **Description:** The CP session id is placed in the `X-chkp-sid` header. On a `RequestException` for a non-credential payload, the exception text includes the full `request_headers` (lines 49-51) containing the live sid; non-login payloads are also logged at debug 10 (line 30). No header redaction on this vendor path.
- **Impact:** Check Point session tokens can leak into error/debug logs and be replayed while valid.
- **Recommendation:** Redact `X-chkp-sid` (and credential headers) before including headers in exception text/debug output.

### [LOW] Native/normalized config dumped to a predictable shared temp path
- **ID:** IMP-07
- **Category:** Path/File handling (information exposure)
- **Location:** `roles/importer/files/importer/fwo_base.py:223-236`, `model_controllers/fwconfigmanagerlist_controller.py:125` (`IMPORT_TMP_PATH` = `fwo_const.py:24`)
- **Description:** At debug level >=7 the full native config (sensitive policy/object data) and normalized config are written to fixed, predictable filenames under `/usr/local/fworch/tmp/import/mgm_id_<id>_config_native.json`. The dir is created with default `mkdir(parents=True)` (`common.py:157`); no explicit restrictive mode/umask, predictable names.
- **Impact:** On a shared host, other local users may read exported firewall configs if the tmp dir isn't tightly permissioned; predictable names enable symlink nuisance (path not attacker-supplied).
- **Recommendation:** Create the import tmp dir `0o700` and write dumps owner-only; gate behind an explicit "dump sensitive config" toggle rather than a generic debug level.

### [INFORMATIONAL] FortiOS access token passed in URL query string
- **ID:** IMP-08
- **Category:** Secrets (transport)
- **Location:** `roles/importer/files/importer/fw_modules/fortiosmanagementREST/fos_getter.py:50-55`
- **Description:** The token is passed via `params={"access_token": access_token}` (query string) — FortiOS API design. The token is *not* embedded in the logged URL (only `api_url` without params logged at line 79), so importer-side logging is clean, but query-string tokens tend to land in FortiGate/proxy access logs.
- **Impact:** Token exposure via upstream access logs, outside the importer's control.
- **Recommendation:** Prefer `Authorization: Bearer` if the FortiOS version supports it; else document as a vendor constraint.

### [INFORMATIONAL] Weak SSH key-exchange algorithm enabled for ASA
- **ID:** IMP-09
- **Category:** Weak crypto (transport)
- **Location:** `roles/importer/files/importer/fw_modules/ciscoasa9/fwcommon.py:50`
- **Description:** ASA SSH transport enables `KexAlgorithms=+diffie-hellman-group14-sha1` (legacy SHA-1 KEX). Commonly needed for old ASA firmware, so a compatibility trade-off, but it weakens the handshake and compounds IMP-01.
- **Impact:** Slightly weaker SSH negotiation; low standalone impact.
- **Recommendation:** Restrict to devices that require it; prefer stronger KEX where supported.

### [INFORMATIONAL] Broad exception handling around security-relevant operations
- **ID:** IMP-10
- **Category:** Error handling
- **Location:** e.g. `fwo_encrypt.py:42`, `fwo_config.py:28`, numerous `except Exception:` in `cp_getter.py`/`fmgr_getter.py`
- **Description:** Many paths catch bare `Exception`. Most re-log/re-raise and are benign, but combined with IMP-03's fail-open decrypt the pattern can mask security-relevant failures (decryption, TLS).
- **Impact:** Reduced visibility of failures; potential to proceed in a degraded/insecure state.
- **Recommendation:** Catch specific exception types where feasible; fail closed on crypto/auth/TLS errors.

## Positive Observations
- GraphQL queries/mutations loaded from static `.graphql` files and executed with parameterized `variables`; device data never interpolated into query text — no data-layer injection.
- Sensitive headers (`authorization`, `x-hasura-admin-secret`) and GraphQL variables redacted before logging (`fwo_api.py:466-486`).
- Login payloads deliberately excluded from debug logs on CP and FortiManager paths (`cp_getter.py:29`, `fmgr_getter.py:39,49`).
- No `eval`/`exec`/`compile`/`os.system`/`subprocess`/`pickle`/`marshal`/`jsonpickle`/`yaml.load`; no XML parsing (no XXE surface).
- Vendor module selection uses an explicit `match` statement, not dynamic import from device-controlled strings (`common.py:290-305`).
- Config integrity hashing uses SHA-256, not MD5/SHA-1 (`fwo_base.py:308-311`).
- Management secrets decrypted only in memory, not written into GraphQL payloads or normal logs; decryption skipped for URL/fileless managements (`management_controller.py:193-209`).
