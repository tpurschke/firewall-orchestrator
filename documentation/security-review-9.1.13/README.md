# Security Code Review — Firewall Orchestrator (FWO) 9.1.13

| | |
|---|---|
| **Produkt** | Firewall Orchestrator (FWO) |
| **Version** | 9.1.13 |
| **Basis** | `develop`-Linie (HEAD trägt Versionsbump auf 9.1.13) |
| **Review-Branch** | `security-review/9.1.13` |
| **Datum** | 2026-07-01 |
| **Geprüfte Komponenten** | UI (Blazor/C#), Middleware (ASP.NET/C#), Importer (Python), Lib (C# Shared Libraries) |
| **Methode** | Statisches Code-Review durch 4 spezialisierte Sub-Agenten, je eine Ansible-Rolle |
| **Art** | Read-only Audit — keine Änderungen am Anwendungscode |

Detailberichte je Rolle:
- [UI](findings-ui.md)
- [Middleware](findings-middleware.md)
- [Importer](findings-importer.md)
- [Lib (Shared Libraries)](findings-lib.md)

---

## 1. Management Summary

Das Firewall Orchestrator zeigt in den zentralen, sicherheitskritischen Bereichen ein
**grundsätzlich solides Sicherheitsniveau**. Die Authentifizierungs- und
Autorisierungsarchitektur ist durchdacht umgesetzt: JWTs werden mit RS256 und
installationsindividuellen RSA-2048-Schlüsseln signiert und vollständig validiert
(Signatur, Issuer, Audience, Laufzeit); jeder Middleware-Endpunkt ist mit expliziter
Rollenprüfung geschützt (kein `[AllowAnonymous]`); Refresh-Tokens werden per CSPRNG
erzeugt, nur als Hash gespeichert und sind Einmal-Tokens; LDAP-Filter werden korrekt
nach RFC 4515 escaped; die GraphQL-Schnittstelle wird durchgängig parametrisiert
(keine Injection); und es wurden **keine hartcodierten Produktiv-Secrets, keine unsichere
Deserialisierung (kein `TypeNameHandling`/`BinaryFormatter`/`pickle`) und keine
Command-Injection-Pfade** gefunden.

**Es wurden keine kritischen Schwachstellen (Critical) und keine Remote-Code-Execution-
Pfade identifiziert.** Der dominierende Befund ist stattdessen ein **systemisches,
komponentenübergreifendes Muster: Die TLS-Zertifikatsprüfung ist in nahezu allen
Netzwerk-Clients standardmäßig deaktiviert.** Dies betrifft die Verbindungen zur
GraphQL-/Hasura-API, zum Middleware-Server, zu den Firewall-Management-APIs
(Check Point, FortiManager, FortiOS), zum LDAP-/AD-Server, zum SMTP-Relay sowie den
SSH-Kanal zu Cisco ASA. In dieser Standardkonfiguration kann ein Angreifer mit einer
Position im Netzwerkpfad (On-Path/MITM) den jeweiligen Kanal aufbrechen, um
**privilegierte Firewall-Anmeldedaten, LDAP-Credentials, JWTs und SMTP-Zugangsdaten
abzugreifen oder übertragene Konfigurations-/Policy-Daten zu manipulieren**. Da FWO per
Definition privilegierte Zugänge zu allen verwalteten Firewalls bündelt, ist die
Vertraulichkeit und Integrität dieser Transportkanäle das wichtigste Handlungsfeld.

Ein zweites übergreifendes Thema ist die **Absicherung gespeicherter Secrets**: Die
zentrale Verschlüsselung nutzt AES-CBC ohne Authentifizierung (kein MAC/AEAD) und leitet
den Schlüssel ohne KDF direkt aus den Rohbytes der Schlüsseldatei ab; zusätzlich verhält
sich die Entschlüsselung „fail-open" (gibt bei Fehler den Chiffretext zurück statt zu
scheitern). In der UI existiert ein wiederkehrender Output-Encoding-Mangel
(Rendern importierter/Nutzer-Strings via `MarkupString` ohne HTML-Encoding), dessen
praktische Ausnutzbarkeit jedoch durch die Eingabe-Sanitizer eingeschränkt ist.

### Gesamtbewertung der Findings

| Schweregrad | Anzahl |
|-------------|:------:|
| Critical | 0 |
| **High** | **6** |
| Medium | 9 |
| Low | 11 |
| Informational | 11 |
| **Summe** | **37** |

### Findings je Rolle

| Rolle | Critical | High | Medium | Low | Info | Bericht |
|-------|:---:|:---:|:---:|:---:|:---:|---|
| UI | 0 | 0 | 2 | 3 | 3 | [findings-ui.md](findings-ui.md) |
| Middleware | 0 | 1 | 3 | 3 | 3 | [findings-middleware.md](findings-middleware.md) |
| Importer | 0 | 2 | 2 | 3 | 3 | [findings-importer.md](findings-importer.md) |
| Lib | 0 | 3 | 2 | 2 | 2 | [findings-lib.md](findings-lib.md) |
| **Summe** | **0** | **6** | **9** | **11** | **11** | |

---

## 2. Übergreifende Themen (Cross-Cutting)

### 2.1 TLS-/Transport-Sicherheit standardmäßig deaktiviert — *Leitbefund*
Sieben der elf High-/Medium-Findings mit Transportbezug beschreiben denselben Kern:
Zertifikats- bzw. Host-Key-Prüfung ist ausgeschaltet oder standardmäßig aus.

| Betroffener Kanal | Finding(s) |
|---|---|
| GraphQL-/Hasura-API (trägt das JWT) | LIB-01 |
| Alle REST-Clients (Middleware, Check Point, FortiManager, Tufin) | LIB-02 |
| SMTP-Versand (überträgt SMTP-Credentials) | LIB-03 |
| LDAP/AD-Bind (überträgt Nutzer-Credentials) | MW-01 |
| Firewall-Management-APIs (CP/FortiManager/FortiOS), Default aus | IMP-02 |
| Cisco-ASA-SSH (Host-Key-Prüfung immer aus) | IMP-01 |

**Empfehlung:** Zertifikatsprüfung überall standardmäßig aktivieren (fail-closed);
Ausnahmen nur als explizite, pro-Verbindung wählbare, standardmäßig deaktivierte
Opt-in-Option mit sichtbarem Warnhinweis. Interne Kanäle gegen die ausgerollte CA
validieren.

### 2.2 Absicherung gespeicherter Secrets (Krypto)
Die gemeinsam genutzte Verschlüsselung (C#: `FWO.Encryption/AesEnc.cs`, Python:
`fwo_encrypt.py`) nutzt **AES-CBC ohne MAC/AEAD**, leitet den Schlüssel **ohne KDF** aus
den Roh-UTF-8-Bytes ab und verhält sich bei Fehlern **fail-open**.
Findings: **LIB-04, MW-03, IMP-03, LIB-08**.
**Empfehlung:** Migration auf AEAD (AES-GCM) oder Encrypt-then-MAC (HMAC-SHA256),
Schlüsselableitung via HKDF/PBKDF2 mit Salt, versioniertes Chiffreformat, Fail-closed.

### 2.3 Secrets/Token in Logs
Session-Tokens, JWTs und Request-Bodies gelangen an mehreren Stellen in Debug-/Error-Logs.
Findings: **IMP-05, IMP-06, LIB-06, LIB-07, MW-07, UI-05**.
**Empfehlung:** Token/Header/Bodies vor dem Loggen redigieren (wie bereits in der
FWO-API-Redaction umgesetzt); vollständige Payloads nur hinter explizitem Trace-Schalter.

### 2.4 Output-Encoding in der UI
Import-/nutzergesteuerte Strings werden via `MarkupString` ohne HTML-Encoding gerendert;
die Absicherung erfolgt nur eingangsseitig durch Allowlist-Sanitizer, die `<`/`>`
durchlassen. Findings: **UI-01, UI-02, UI-03, UI-04**.
**Empfehlung:** Am Render-Ort HTML-encoden (`WebUtility.HtmlEncode`, Muster ist bereits an
mehreren Stellen vorhanden); `<`/`>` aus `StandardSanitizationRegex` entfernen.

---

## 3. High-Findings im Detail (priorisiert)

| # | ID | Titel | Ort |
|---|----|-------|-----|
| 1 | **LIB-01** | GraphQL-Client deaktiviert TLS-Zertifikatsprüfung bedingungslos (JWT abgreifbar) | `FWO.Api.Client/GraphQlApiConnection.cs:40,48` |
| 2 | **LIB-02** | `RestApiClient` akzeptiert jedes TLS-Zertifikat per Default; alle REST-Clients erben das | `FWO.Api.Client/RestApiClient.cs:16,29-36` |
| 3 | **LIB-03** | SMTP-Mailer „accept all SSL certificates", dann Versand der Credentials | `FWO.Mail/MailerMailKit.cs:71,80` |
| 4 | **MW-01** | LDAP-TLS-Zertifikatsprüfung deaktiviert (MITM aller Binds inkl. Credential-Prüfung) | `FWO.Middleware.Server/LdapBasic.cs:52` |
| 5 | **IMP-01** | SSH-Host-Key-Prüfung für Cisco-ASA-Import komplett aus (ASA-Admin-/Enable-Secret abgreifbar) | `fw_modules/ciscoasa9/fwcommon.py:49` |
| 6 | **IMP-02** | TLS-Zertifikatsprüfung für alle Vendor-APIs standardmäßig aus (Default `importCheckCertificates=False`) | `cp_getter.py:37`, `fmgr_getter.py:34`, `fos_getter.py:54` |

Alle sechs High-Findings sind Ausprägungen desselben Grundproblems (Abschnitt 2.1) und
lassen sich mit einer koordinierten Maßnahme „Zertifikatsprüfung fail-closed" adressieren.

---

## 4. Empfohlene Maßnahmen (Roadmap)

**Sofort (High — Transportsicherheit):**
1. In allen Netzwerk-Clients die accept-any-Callbacks entfernen und Zertifikatsprüfung
   standardmäßig aktivieren (LIB-01/02/03, MW-01). Unsichere Modi nur als explizites,
   standardmäßig deaktiviertes Opt-in.
2. `importCheckCertificates` standardmäßig auf `True` setzen und mit Trust-Store versehen
   (IMP-02); SSH-Host-Key-Prüfung für ASA aktivieren (IMP-01).

**Kurzfristig (Medium):**
3. Krypto härten: AEAD/Encrypt-then-MAC + KDF, Fail-closed (LIB-04, MW-03, IMP-03).
4. Fehlermeldungen der Auth-/Token-Endpunkte generalisieren, keine `Exception.Message`
   an Clients zurückgeben (MW-02).
5. Delegations-Token an Mandantengrenzen binden, Legacy-`GetForUser` ablösen (MW-04).
6. UI-Output am Render-Ort HTML-encoden; `<`/`>` aus dem Sanitizer entfernen
   (UI-01, UI-02).
7. Datei-/URL-Import einschränken (Allowlist Host+Schema, `file://` verbieten) (IMP-04).

**Mittelfristig (Low/Hardening):**
8. Token/Bodies aus Logs redigieren (IMP-05/06, LIB-06/07, MW-07, UI-05).
9. `main_key`-Datei auf `0600` verschärfen (LIB-05).
10. `AllowedHosts` einschränken, HSTS/HTTPS-Erzwingung bestätigen (MW-06, UI-07);
    committed Test-JWT-Keys durch ephemere Test-Keys ersetzen (MW-05);
    explizite `[Authorize]`-Attribute auf allen routbaren Seiten (UI-08).

---

## 5. Methodik, Geltungsbereich & Grenzen

**Methodik.** Vier spezialisierte Sub-Agenten führten parallel je ein statisches
Security-Review einer Ansible-Rolle durch, jeweils mit auf Sprache/Framework
zugeschnittenen Bedrohungsklassen (u. a. XSS/AuthZ für die Blazor-UI; JWT/LDAP/Krypto für
die Middleware; Command-Injection/TLS/SSRF/Deserialisierung für den Python-Importer;
Krypto/TLS-Clients/Deserialisierung für die Shared Libraries). Befunde wurden per grep
lokalisiert und anschließend durch Lesen des umliegenden Codes auf tatsächliche
Ausnutzbarkeit verifiziert (False Positives wurden verworfen, z. B. `RecertCheck.cs:114`).

**Geltungsbereich.** Geprüft wurden die Rollen `roles/ui`, `roles/middleware`,
`roles/importer` und `roles/lib` im Stand von Version 9.1.13. Mehrere Findings verweisen
begründet auf angrenzende Rollen (z. B. DB-Seed `fworch-fill-stm.sql`, Installer
`roles/common/tasks/main.yml`), wenn dort das Standardverhalten gesetzt wird.

**Grenzen.** Es handelt sich um ein rein statisches Review ohne dynamische Tests,
Penetrationstest oder Laufzeit-/Deployment-Analyse. Nicht im Fokus: Datenbank-/Hasura-
Permissions im Detail, Ansible-Installer-Härtung, Abhängigkeits-/Supply-Chain-Analyse
(CVEs in NuGet/PyPI-Paketen) sowie die eigentliche Reverse-Proxy-/TLS-Terminierung.
Schweregrade sind Experteneinschätzungen nach Ausnutzbarkeit und Auswirkung im
FWO-Kontext; sie ersetzen keine formale CVSS-Bewertung.
