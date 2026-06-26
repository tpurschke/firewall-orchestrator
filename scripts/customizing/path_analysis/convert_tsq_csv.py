#!/usr/bin/python3
"""Convert TSQ path-analysis CSV exports into FWO path-analysis JSON."""

from __future__ import annotations

import argparse
import csv
import json
import logging
from pathlib import Path
from typing import TYPE_CHECKING, TypedDict

if TYPE_CHECKING:
    from collections.abc import Sequence

logger = logging.getLogger(__name__)

REQUIRED_COLUMNS: tuple[str, ...] = ("Version", "Zone", "Netz", "Maske", "TSQ-Root", "TSQ-Internet")


class PathAnalysisPayload(TypedDict):
    """JSON payload shape expected by the FWO path-analysis import."""

    source_name: str
    entries: list[dict[str, str]]


def convert_csv_file(csv_path: Path, source_name: str | None = None) -> PathAnalysisPayload:
    """Read TSQ CSV data and return the JSON payload expected by FWO."""
    with csv_path.open("r", encoding="utf-8-sig", newline="") as csv_file:
        reader = csv.DictReader(csv_file)
        if reader.fieldnames is None:
            raise ValueError(f"path-analysis CSV '{csv_path}' has no header row")
        validate_header(reader.fieldnames, csv_path)
        entries: list[dict[str, str]] = [
            convert_row(row) for row in reader if any((value or "").strip() for value in row.values())
        ]
    return {"source_name": source_name or csv_path.name, "entries": entries}


def validate_header(fieldnames: Sequence[str], csv_path: Path) -> None:
    """Validate that all required TSQ columns are present."""
    normalized_fields: set[str] = {field.strip() for field in fieldnames}
    missing_columns: list[str] = [column for column in REQUIRED_COLUMNS if column not in normalized_fields]
    if missing_columns:
        raise ValueError(f"path-analysis CSV '{csv_path}' is missing column(s): {', '.join(missing_columns)}")


def convert_row(row: dict[str, str | None]) -> dict[str, str]:
    """Convert one TSQ CSV row into the stable import DTO shape."""
    network = clean_value(row.get("Netz"))
    mask = clean_value(row.get("Maske"))
    return {
        "version": clean_value(row.get("Version")),
        "zone": clean_value(row.get("Zone")),
        "network": format_network(network, mask),
        "root_path": clean_value(row.get("TSQ-Root")),
        "internet_path": clean_value(row.get("TSQ-Internet")),
    }


def format_network(network: str, mask: str) -> str:
    """Return the network in the JSON import format."""
    if network == "" or network.startswith("!") or "/" in network:
        return network
    if mask == "":
        raise ValueError(f"path-analysis network '{network}' has no mask")
    return f"{network}/{mask}"


def clean_value(value: str | None) -> str:
    """Normalize empty CSV cells to empty strings."""
    return "" if value is None else value.strip()


def parse_args() -> argparse.Namespace:
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description="Convert TSQ path-analysis CSV to FWO JSON.")
    script_stem = Path(__file__).with_suffix("")
    parser.add_argument(
        "--csv",
        dest="csv_path",
        type=Path,
        default=script_stem.with_suffix(".csv"),
        help="TSQ CSV input path",
    )
    parser.add_argument(
        "--json",
        dest="json_path",
        type=Path,
        default=script_stem.with_suffix(".json"),
        help="JSON output path",
    )
    parser.add_argument("--source-name", dest="source_name", default=None, help="source_name value written to JSON")
    return parser.parse_args()


def main() -> int:
    """Convert the configured CSV and write the JSON file."""
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    args = parse_args()
    payload = convert_csv_file(args.csv_path, args.source_name)
    args.json_path.parent.mkdir(parents=True, exist_ok=True)
    args.json_path.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    logger.info(
        "converted %d path-analysis entries from %s to %s",
        len(payload["entries"]),
        args.csv_path,
        args.json_path,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
