# Generic (non-firewall) config items

Since 10.1.0 Firewall Orchestrator imports and recertifies rule-like config items of devices that are not firewalls,
for example proxy or load balancer rules (#5786). This page explains the data model and how to add a new device type.

## Config item

Every importer module delivers its items as `models.generic_config.GenericConfigItem`. The core fields mean the same
for all device types, so reports and the recertification page show the same columns for all of them:

| Field | Content | Skyhigh rule | F5 virtual server (example) |
|---|---|---|---|
| `uid` (required) | stable unique ID within the management | rule ID | `fullPath` |
| `name` | display name | rule name | name |
| `scope` | where the item lives on the device | rule set > group path | partition / folder |
| `order` | position; without it the position in the delivered list is used | - | - |
| `enabled` | in effect, including everything inherited | rule set, groups and rule enabled | enabled / disabled |
| `match` | readable form of what the item applies to | effective condition | destination address:port |
| `action` | readable form of what the item does | block, redirect, ... | pool |
| `description` | free text | description | description |
| `owner` | owner name or external app ID | `owner:` tag in the description | tag or metadata |
| `last_used` | last use (timestamps without time zone are taken as UTC) | - | - |
| `attributes` | vendor-specific normalized values | rule set, rule and group condition | e.g. `pool`, `profiles` |
| `raw` | original object of the device | rule element of the export | object from the device API |

The item without `order` and `last_used` is stored as `config_line`. The reports show uid, name, owner, scope, enabled,
match and action as columns; the details of an item show the whole config line.

## Data model (schema `generic`)

| Table | Content |
|---|---|
| `generic.device_type_key_config` | Per device type settings, see below. |
| `generic.config_item` | One item per row, versioned like `firewall.rule` (`created` / `removed` import IDs). Only one active version per `(mgm_id, item_uid)`. |
| `generic.recertification` | Recertification decisions, append-only. `config_item_id` is the certified version; the decision applies to the item (`mgm_id`, `item_uid`) until its next due date. |
| `generic.owner_recertification` | Owner-level recertification of all generic items of an owner. |
| `generic.responsible_type_dev_type` | Generic device types the users of an owner responsible type may recertify (#5788). A responsible type without rows may not recertify generic items. |

Key configuration columns:

- `owner_match` (`name` or `ext_id`): the `owner` of an item is mapped to an owner by owner name or external app ID
- `has_last_used`: the device type delivers `last_used`; only then its items appear in the unused items report
- `reportable_fields`: keys (or dot-separated paths) of `attributes` shown as additional report columns, after the core columns

## Import

1. The importer module returns all items of the management (`GenericConfigCommon.get_config_items`).
2. `model_controllers/generic_config_import.py` maps them to `generic.config_item` and compares with the active items:
   - new item: new row
   - changed item (config line, name or owner): new version, the old one gets `removed`
   - vanished item: `removed`
   - order and last-used value only: updated in place
3. All writes go in one mutation (`generic/updateGenericConfigItems.graphql`). A failed import is rolled back by
   `import/rollbackImport.graphql`. Recertification data is never touched by the import.

## Adding a device type

1. Add the device type to `stm_dev_typ` and its key configuration to `generic.device_type_key_config`
   (`fworch-fill-stm.sql` for fresh installs, a new upgrade script for existing ones).
2. Create `roles/importer/files/importer/fw_modules/<name><version>/` with a `fwcommon.py` whose class derives from
   `models.fw_common.GenericConfigCommon` and implements `get_config_items`, mapping each device object to the
   core fields of `GenericConfigItem`.
   Use the management's import credential and `import_state.state.verify_certs` for the device connection.
3. Map the package name to the class in `common.get_generic_module` and add the device type ID to the list of
   `import_main_loop.py`.
4. Add unit tests with sample data under `roles/importer/files/importer/test/`.

No UI or report change is needed: reports and the recertification page show the core fields and the reportable
attributes from the key configuration.

## Skyhigh Web Gateway (device type 33)

The module `skyhighwebgateway12ff` uses the Konfigurator REST API (`https://<host>:<port>/Konfigurator/REST/`):
login with basic auth, list the top-level rule sets, export each rule set and log out. Each rule becomes one item:

- `scope` is the path of rule groups, starting with the top-level rule set.
- `enabled` is only true if the rule set, all rule groups above the rule and the rule itself are enabled.
- `match` is the effective condition: the conditions of all rule groups above the rule and the rule's own
  condition, joined with AND. `attributes` hold the rule set name, the rule's own condition (`rule_condition`) and
  the group part alone (`group_condition`).
- `raw` is the rule element of the rule set export as parsed by xmltodict. Referenced lists appear with their
  name only, not with their entries.
- The owner is taken from a line `owner: <external app id>` in the rule description.
Links to other hosts in the rule set list are ignored, as the session cookie would be sent there.

### Testing without a Skyhigh gateway

There is no public Skyhigh test system. `test/mock_servers/skyhigh_konfigurator_mock.py` in the importer directory
mocks the four Konfigurator calls the module uses (login, rule set list, rule set export, logout) with a cookie
session over HTTPS, following the Skyhigh documentation of the REST interface. It serves the rule set exports in
`test/data/skyhigh_mock/` (see the README there for the owner tags used).

```console
cd /usr/local/fworch/importer
SKYHIGH_MOCK_PASSWORD=<password> importer-venv/bin/python -m test.mock_servers.skyhigh_konfigurator_mock --host 127.0.0.1 --port 4712
```

Without `--cert`/`--key` a self-signed certificate is created and its path is logged. Create a management of type
"Skyhigh Web Gateway" with host and port of the mock and an import credential with user `mockadmin` (or `--user`)
and the password, and either trust the certificate or switch off the certificate check for firewall connections.
The mock only proves that the module matches our reading of the documentation; it does not replace a test against
a real gateway.

### Demo installations

With demo data (`add_demo_data`, set by `testkeys=yes`) the installer sets this up without further steps:

- every importer host runs the mock as service `fworch-skyhigh-mock` on `127.0.0.1:4712`
  (`demo_skyhigh_mock_*` in `inventory/group_vars/all.yml`), with a server certificate of the internal CA
  (profile `skyhigh-mock-server`), so the default certificate check of firewall connections passes,
- the management `skyhigh_demo` (credential `credential_skyhigh_demo`) imports from it,
- the sample rules carry the owner tags of the demo owners (`ownerF_demo` = `123`, `ownerD_demo` = `234`),
- main responsibles may recertify Skyhigh items, and both demo tenants see the management, so `user1_demo`
  recertifies the items of `ownerF_demo` and `user2_demo` those of `ownerD_demo`.

Installations without demo data remove a mock service left from an earlier demo installation.

## Switching the feature on and off

Generic recertification is the module `GenericRecertification` (7) in `availableModules` (Settings > Further
settings > Available Modules). Without it:

- the generic items section on the recertification page is not shown,
- the generic report types (61-63) cannot be selected, whatever `availableReportTypes` or a per-role override says,
- the daily recertification check adds no generic items to its notifications,
- Settings > Responsibles neither shows nor changes the generic device types of the responsible types.

Fresh installations offer the module. The upgrade to 10.1.0 leaves an existing module list unchanged, so the module
stays off until an admin enables it; a missing or empty list (meaning "all modules") is pinned to the modules 1-6.
Import and storage of the items, and their GraphQL permissions, do not depend on the module: it only hides the feature.

## Access

- Config items are written by the `importer` role only.
- `recertifier` may only add decisions for items and owners in `x-hasura-recertifiable-owners`, of device types in
  `x-hasura-generic-recert-dev-types`, with `user_dn` equal to its own DN (`x-hasura-uuid`); `recert_date` is set by
  the database.
- `x-hasura-generic-recert-dev-types` is derived at login: the device types assigned (settings, owner responsible
  types) to the active, recertifying responsible types the user or one of the user's groups holds. It applies per user
  to all owners the user may recertify; "proxies for owner A, load balancers for owner B" cannot be expressed.
- `reporter` sees items of its visible managements, `recertifier` and `modeller` additionally only items of their owners.
- The daily recertification check (rule-by-rule mode) adds due generic items to the rule recertification notification.

No middleware REST endpoint exists for generic items or their recertification state (decision of #5792): the
UI, the reports and the daily check read them through GraphQL with the permissions above, and no external system
needs them yet. A future endpoint has to follow the REST rules (`fwo-rest-api`: rate limit, `limit` / `offset` /
`X-Has-More`, bounded inputs) and must not widen what a caller may see.

The permissions are checked against a running installation by the integration test
`roles/tests-integration/tasks/test-generic-recert.yml` (decisions per owner, device type and DN, read filters
per role, and the device type claim at login and token refresh).
