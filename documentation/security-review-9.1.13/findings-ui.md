# Security Review — UI Role (FWO 9.1.13)

## Summary

The FWO UI (Blazor Server, `roles/ui/files/FWO.UI`) is, on balance, a solidly engineered front-end with several strong security controls: JWTs are cryptographically validated before claims are trusted (`AuthStateProvider.ApplyJwtAsync`), the `importer` and `anonymous` roles are explicitly rejected at UI login, access/refresh tokens are persisted with ASP.NET Core `ProtectedBrowserStorage` (encrypted, server-keyed — never plain `localStorage`), a dedicated `UrlSanitizerMiddleware` blocks unsafe/absolute redirect URLs, JS interop contains no `eval`/`innerHTML` sinks, and no hardcoded secrets, `Process.Start`, reflection, or `BinaryFormatter` usage exist in the role. Page-level authorization is applied via `@attribute [Authorize(Roles=…)]` on privileged pages, and ultimate data-access authorization is enforced server-side by Hasura permissions bound to the JWT (the UI is a thin client), so the few pages without a page-level attribute do not by themselves grant privileged data access.

The one recurring weakness is **output encoding**: user- and firewall-import-controlled strings (network-object/rule names, workflow comments, connection reasons, admin-editable config texts) are rendered to the DOM via `@((MarkupString)…)` / `new MarkupString(…)` without HTML-encoding at the render site. The codebase relies instead on *input-time* allowlist sanitizers (`StringExtensions.SanitizeMand`, `SanitizeCommentMand`) which — critically — permit the `<` and `>` characters. Practical exploitation is constrained (the sanitizers strip `=`, `"` and `'`, and DOM-inserted `<script>` does not execute in modern browsers), so this is a markup-injection / defense-in-depth defect rather than a slam-dunk stored XSS. Several render sites already do encode correctly (`MonitorScheduler`, `RequestFwChangePopup.DisplayTitle`, `WfStatefulObject.DisplayAllComments`), making the omissions inconsistent rather than systematic.

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 0 |
| Medium   | 2 |
| Low      | 3 |
| Informational | 3 |

## Findings

### [MEDIUM] Import-controlled rule / object names rendered as raw HTML in reports
- **ID:** UI-01
- **Category:** XSS / Output Encoding (stored, constrained)
- **Location:** `roles/ui/files/FWO.UI/Pages/Reporting/Reports/ComplianceReport.razor:51` (and `:24,33,42,60,79,97,115,133,142,169`); `roles/ui/files/FWO.UI/Pages/Reporting/ReportedRules.razor:108,121,126`; `roles/ui/files/FWO.UI/Pages/Reporting/Reports/RulesReport.razor:121,139,148`; `roles/ui/files/FWO.UI/Pages/NetworkModelling/ManualAppServer.razor:60`; `roles/ui/files/FWO.UI/Pages/NetworkModelling/SearchNwObject.razor:38`; `roles/ui/files/FWO.UI/Pages/NetworkModelling/EditConn.razor` (numerous `DisplayWithIcon()` sites)
- **Description:** These pages emit report/object display strings via `@((MarkupString)…)`. The underlying helpers return values verbatim with no HTML encoding — `RuleDisplayBase.DisplayName(Rule)` returns `rule.Name ?? ""` (`roles/lib/files/FWO.Report/Display/RuleDisplayBase.cs:86`), `RuleViewData.Name/Comment/ViolationDetails` are raw (`roles/lib/files/FWO.Report/Data/ViewData/RuleViewData.cs:57,67,70`), and `ModellingObject.DisplayHtml()` wraps `Display()` (containing object `Name`) in a `<span>` without encoding (`roles/lib/files/FWO.Data/Modelling/ModellingObject.cs:41`). Rule/object names/zones/comments originate from **firewall device configs ingested by the importer** and from **CSV app-server imports** (`FileUploadService.cs`), neither of which HTML-encodes. The modelling write path calls `Sanitize()`, but the sanitizer (`roles/lib/files/FWO.Basics/StringExtensionsSanitizer.cs:199`, `StandardSanitizationRegex = [^\w\.\*\-\:\?@/\(\)\[\]\{\}\$\+<>#\$ ]`) is an allowlist that **retains `<`, `>`, `/`, `(`, `)`** and only removes `=`, `"`, `'`.
- **Impact:** An actor who can influence a firewall object/rule name (or upload a crafted CSV, or is a low-privilege modeller) can inject HTML markup rendered in the browser of any reviewer/auditor/admin opening the report — UI redressing, content spoofing, phishing links. Full script execution is blocked in practice because `=`/quotes are stripped and DOM-inserted `<script>` does not run (hence Medium, not High). Blast radius is cross-role (report author → report reader).
- **Recommendation:** Encode at render time — render plain text as `@value` instead of `@((MarkupString)value)`, or `WebUtility.HtmlEncode(...)` before building the `MarkupString` (as already done in `MonitorScheduler.razor:77`). Where a `Display*` helper must emit markup, encode the interpolated data segments inside the helper. Do not rely on the input allowlist; at minimum drop `<`/`>` from `StandardSanitizationRegex`.

### [MEDIUM] Workflow comments / connection reasons rendered as raw HTML
- **ID:** UI-02
- **Category:** XSS / Output Encoding (stored, constrained)
- **Location:** `roles/ui/files/FWO.UI/Pages/NetworkModelling/RequestFwChangePopup.razor:69` (via `DisplayTaskDetails`, concatenating `task.Comments.First().Comment.CommentText` unencoded); `roles/ui/files/FWO.UI/Pages/NetworkModelling/EditConn.razor:53` (`ConnHandler.ActConn.Reason` as `MarkupString`)
- **Description:** `DisplayTaskDetails` builds an HTML string splicing raw `CommentText` between `<br>` separators, returned to the `MarkupString` at line 69. `EditConn.razor:53` renders a connection `Reason` verbatim. Both are user-supplied and sanitized only by `SanitizeCommentMand` (`StringExtensionsSanitizer.cs:111`, `CommentRegex = ["'']`) which strips only quotes — `<`/`>` pass through. The same file's `DisplayTitle` already encodes correctly, so this is inconsistent.
- **Impact:** A workflow participant can inject markup into a ticket comment / connection reason later rendered for an approver/implementer/auditor. Script execution constrained by quote/`=` stripping; realistic impact is UI spoofing / content injection.
- **Recommendation:** HTML-encode comment and reason before interpolation (reuse the `WebUtility.HtmlEncode` pattern from `DisplayTitle`), or render the reason as plain `@ConnHandler.ActConn.Reason`.

### [LOW] Admin-editable localization / config texts rendered as raw HTML
- **ID:** UI-03
- **Category:** XSS / Output Encoding (privileged-injection)
- **Location:** `roles/ui/files/FWO.UI/Pages/Start.razor:14,17,18,20,21,23,24`; `roles/ui/files/FWO.UI/Pages/Reporting/ReportCreateTicket.razor:16`; `roles/ui/files/FWO.UI/Pages/NetworkModelling/RequestRecertPopup.razor:42` (`userConfig.ModRecertText`)
- **Description:** Custom UI texts are returned by `UserConfig.GetText`/`Convert` (`roles/lib/files/FWO.Config.Api/UserConfig.cs:189,333`), which `HtmlDecode`s the value and injects `<a href>` link rewriting, then pages render the result as `MarkupString`. Intentional (help text supports HTML), but anyone who can edit custom/recert texts can store arbitrary markup shown to every user.
- **Impact:** Requires a privileged admin role — stored-XSS-by-admin / second-order concern. Not directly exploitable by unprivileged users.
- **Recommendation:** If rich text is required, run stored text through a vetted allowlist sanitizer (the same `StripDangerousHtmlTags` used for the welcome message) before rendering.

### [LOW] Custom logo base64 injected directly into an image `src` data-URI
- **ID:** UI-04
- **Category:** Output Encoding / Content Injection
- **Location:** `roles/ui/files/FWO.UI/Pages/Login.razor:23` (`<img src="data:image/png;base64, @(globalConfig.CustomLogoData)" …>`)
- **Description:** The admin-uploaded logo is stored as base64 (`FileUploadService.ImportCustomLogo`) and interpolated into a `data:` URI on the pre-auth login page. Upload validates extension (substring check — see UI-06) and size but not actual PNG magic bytes, and the base64 is not constrained before landing in `src`. Blazor attribute-encodes `"`, limiting break-out.
- **Impact:** Requires admin; realistic impact is a broken image / mismatched content type, not script execution (hard-coded `image/png` in `<img>`). Low.
- **Recommendation:** Validate uploaded content is a genuine image (magic bytes) and well-formed base64; consider serving the logo from a controller endpoint rather than inlining.

### [LOW] Token/refresh response body logged on error paths
- **ID:** UI-05
- **Category:** Sensitive Data Exposure (logging)
- **Location:** `roles/ui/files/FWO.UI/Services/TokenService.cs:214`, `:277`
- **Description:** On failed token refresh/revoke, raw middleware `response.Content` is logged; on an unexpected partial success this could contain token material. All other logging in the role is clean (only `ex.Message`/status codes; no JWT/password/secret values logged anywhere in scope).
- **Impact:** Low — only on error, body is usually an error string; logs may be less protected than the token store.
- **Recommendation:** Log only `StatusCode`/`ErrorMessage` on these paths, or redact the body.

### [INFORMATIONAL] Upload extension check uses substring match
- **ID:** UI-06
- **Category:** Input Validation
- **Location:** `roles/ui/files/FWO.UI/Services/FileUploadService.cs:63` (`!AllowedFileFormats.Contains(fileExtension)`)
- **Description:** Uses `string.Contains`, so a substring extension can pass. Impact minimal: uploaded bytes are only stored (CSV parsed as text, logo re-encoded), never executed or written to a path-controlled location; `Path.GetExtension` used (no traversal).
- **Recommendation:** Compare against an explicit, case-insensitive exact-extension allowlist.

### [INFORMATIONAL] `AllowedHosts` wildcard and commented-out HTTPS/HSTS
- **ID:** UI-07
- **Category:** Hardening / Configuration
- **Location:** `roles/ui/files/FWO.UI/appsettings.json` (`"AllowedHosts": "*"`); `roles/ui/files/FWO.UI/Program.cs:135-139` (`UseHsts()`/`UseHttpsRedirection()` commented out)
- **Description:** Host-header and transport hardening are disabled at the app layer — likely deliberate because TLS terminates at the installer-deployed reverse proxy.
- **Recommendation:** Set `AllowedHosts` to expected host(s), confirm the proxy enforces HTTPS+HSTS, and document the assumption.

### [INFORMATIONAL] Pages relying on in-code role checks rather than `[Authorize]`
- **ID:** UI-08
- **Category:** Authorization (defense-in-depth)
- **Location:** e.g. `roles/ui/files/FWO.UI/Pages/Monitoring/MonitorUiLog.razor:6` (gates "see all users" via `userConfig.CanUseAnyRole(Roles.Admin, Roles.Auditor)` at line 72), `Pages/Settings/SettingsUser.razor`, `Pages/Settings/SettingsMain.razor`, `Pages/Start.razor`
- **Description:** A few routable pages omit `@attribute [Authorize]`. They remain behind the app-wide `AuthorizeView` in `App.razor` (authenticated-only), and sensitive data is filtered server-side by Hasura JWT permissions (tenant/user scoped). No tenant-isolation bypass identified — consistency/defense-in-depth item only.
- **Recommendation:** Add explicit `@attribute [Authorize(...)]` with the intended role set to every routable page.

## Positive Observations
- **JWT validated before trust** and `Importer`/`Anonymous` roles denied UI login (`AuthStateProvider.ApplyJwtAsync`).
- **Encrypted token storage** via `ProtectedSessionStorage` (`SessionStorageWrapper`, `TokenService`, `ExecutionModeStorage`) — never plain browser storage.
- **Server-side refresh-token revocation on logout** (`TokenService.RevokeTokens`).
- **Dedicated URL sanitizer middleware** blocking non-http(s) schemes, `javascript:`, script/event-handler/dangerous-tag patterns, with help-path allowlist and length cap.
- **Correct output encoding already used** in `MonitorScheduler.razor:77`, `RequestFwChangePopup.DisplayTitle`, `WfStatefulObject.DisplayAllComments(asMarkup:true)`; login welcome message passed through `StripDangerousHtmlTags`.
- **No dangerous sinks:** no `eval`/`innerHTML`, `Process.Start`, reflection, or `BinaryFormatter`; deserialization is `System.Text.Json` into typed models on config/DB data.
- **No hardcoded secrets / keys / connection strings / default creds**; no logging of JWT/password/secret values (aside from UI-05).
- **Internal-only navigation:** every `NavigateTo` uses fixed internal paths with typed IDs/enums — no user-controlled redirect targets.
