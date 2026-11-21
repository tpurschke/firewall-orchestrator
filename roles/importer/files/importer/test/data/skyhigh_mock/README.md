# Skyhigh Web Gateway sample rule sets

Sample data for the Konfigurator REST API mock (`test/mock_servers/skyhigh_konfigurator_mock.py`).
Each file is the export of one top-level rule set (`<libraryContent>` with one top `<ruleGroup>`); the
mock lists them as top-level rule sets in file name order.

| Rule set | Rules | Owner tags |
|---|---|---|
| Global Block Lists (1001) | 2 | `APP-0001` |
| URL Filtering (1002) | 4, two in the nested group "Coaching", one disabled | `APP-0002` (one written `Owner = APP-0002`) |
| Legacy Exceptions (1003, rule set disabled) | 1 | none |

To see the items with owners in Firewall Orchestrator, create owners with the external app ids `APP-0001`
and `APP-0002`.
