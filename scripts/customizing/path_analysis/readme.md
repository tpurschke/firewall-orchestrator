
# Customizing Script for Path Analysis TSQ CSV Conversion

Path analysis imports follow the same script-to-JSON pattern as app data imports. Configure the extensionless path to
`/usr/local/fworch/scripts/customizing/path_analysis/convert_tsq_csv` in Settings - Modules - Path Analysis.

The middleware executes `convert_tsq_csv.py` when present and imports only
`convert_tsq_csv.json`. The script reads `convert_tsq_csv.csv` by default and converts these
TSQ columns into the JSON import format: `Version`, `Zone`, `Netz`, `Maske`, `TSQ-Root`, and `TSQ-Internet`.
The JSON `network` value combines `Netz` and `Maske` into CIDR notation, for example `1.2.3.0/24`.
Both IPv4 and IPv6 networks are supported (for example `2001:db8::/32`); the lookup resolves
endpoints against the matching address family using longest-prefix matching.

You can also run the script manually:

```shell
/usr/local/fworch/scripts/customizing/path_analysis/convert_tsq_csv.py \
  --csv /path/to/tsq.csv \
  --json /path/to/tsq.json
```
