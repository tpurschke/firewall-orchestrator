# Skyhigh Web Gateway sample rule sets

Sample data for the Konfigurator REST API mock (`test/mock_servers/skyhigh_konfigurator_mock.py`).
Each file is the export of one top-level rule set (`<libraryContent>` with one top `<ruleGroup>`); the
mock lists them as top-level rule sets in file name order.

| Rule set | Rules | Owner tags |
|---|---|---|
| Global Block Lists (1001) | 2 | `123` |
| URL Filtering (1002) | 4, two in the nested group "Coaching", one disabled | `234` (one written `Owner = 234`) |
| Legacy Exceptions (1003, rule set disabled) | 1 | none |

The owner tags are the external app ids of the demo owners (`ownerF_demo` = `123`, `ownerD_demo` = `234`), so on a
demo installation the items are mapped to them without further setup. Elsewhere create owners with these external
app ids.
