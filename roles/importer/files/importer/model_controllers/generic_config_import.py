"""
Import step for generic (non-firewall) config items such as proxy or load balancer rules.

The importer module delivers the full list of config items of a management as plain JSON objects.
This step applies the key configuration of the device type (generic.device_type_key_config) and brings
generic.config_item up to date:

- a new item gets a new row,
- a changed item (config line without the last-used value, name or owner) gets a new version,
  the old one is marked removed,
- a vanished item is marked removed,
- order and last-used value are updated in place, as they change without the item changing.

Recertification data is never touched here.
"""

from __future__ import annotations

import copy
from datetime import datetime, timezone
from typing import TYPE_CHECKING, Any, cast

from fwo_api import FwoApi
from fwo_const import GRAPHQL_QUERY_PATH
from fwo_exceptions import FwoApiWriteError, FwoImporterError
from fwo_log import FWOLogger
from models.generic_config import (
    GenericConfigItemRecord,
    GenericItemChanges,
    GenericKeyConfig,
    GenericOwnerRef,
    NormalizedGenericItem,
)

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController
    from models.fw_common import GenericConfigCommon

KEY_PATH_SEPARATOR = "."
MILLISECONDS_PER_SECOND = 1000
UTC = timezone.utc


def as_json_object(value: Any) -> dict[str, Any] | None:
    """A JSON value as object (dict), or None if it is no object."""
    return cast("dict[str, Any]", value) if isinstance(value, dict) else None


def get_value_at_path(item: dict[str, Any], path: str | None) -> Any:
    """Return the value at a dot-separated path of a JSON object, or None if any part is missing."""
    if not path:
        return None
    current: Any = item
    for part in path.split(KEY_PATH_SEPARATOR):
        current_object = as_json_object(current)
        if current_object is None or part not in current_object:
            return None
        current = current_object[part]
    return current


def without_path(item: dict[str, Any], path: str | None) -> dict[str, Any]:
    """Return a deep copy of a JSON object without the value at the given dot-separated path."""
    reduced = copy.deepcopy(item)
    if not path:
        return reduced
    parts = path.split(KEY_PATH_SEPARATOR)
    parent: dict[str, Any] | None = reduced
    for part in parts[:-1]:
        if parent is None or part not in parent:
            return reduced
        parent = as_json_object(parent[part])
    if parent is not None:
        parent.pop(parts[-1], None)
    return reduced


def parse_last_used(value: Any, value_format: str | None) -> datetime | None:
    """Convert the last-used value of an item to a time zone aware timestamp; unparsable values give None."""
    if value is None or value == "":
        return None
    try:
        if value_format in ("unix", "unix_ms"):
            seconds = float(value)
            if value_format == "unix_ms":
                seconds /= MILLISECONDS_PER_SECOND
            return datetime.fromtimestamp(seconds, tz=UTC)
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    except (TypeError, ValueError, OverflowError, OSError):
        FWOLogger.debug(f"generic import: cannot parse last-used value '{value}' as {value_format}")
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=UTC)
    return parsed


def build_owner_lookup(owners: list[GenericOwnerRef], owner_match: str) -> dict[str, int]:
    """Map owner name or external app id (depending on owner_match) to the owner id."""
    lookup: dict[str, int] = {}
    for owner in owners:
        key = owner.app_id_external if owner_match == "ext_id" else owner.name
        if key:
            lookup[key] = owner.id
    return lookup


def resolve_order(raw_item: dict[str, Any], key_config: GenericKeyConfig, position: int) -> int:
    """Use the order key of the item if it holds a number, otherwise the position in the delivered list."""
    order_value = get_value_at_path(raw_item, key_config.order_key)
    if order_value is None:
        return position
    try:
        return int(order_value)
    except (TypeError, ValueError):
        return position


def normalize_item(
    raw_item: dict[str, Any], key_config: GenericKeyConfig, owner_lookup: dict[str, int], position: int
) -> NormalizedGenericItem | None:
    """Apply the key configuration to one delivered item; items without uid are skipped."""
    uid_value = get_value_at_path(raw_item, key_config.uid_key)
    if uid_value is None or str(uid_value) == "":
        FWOLogger.warning(f"generic import: skipping config item at position {position} without uid")
        return None
    name_value = get_value_at_path(raw_item, key_config.name_key)
    owner_value = get_value_at_path(raw_item, key_config.owner_key)
    owner_id = owner_lookup.get(str(owner_value)) if owner_value is not None else None
    if owner_value is not None and owner_id is None:
        FWOLogger.debug(f"generic import: no owner found for '{owner_value}' of item {uid_value}")
    return NormalizedGenericItem(
        item_uid=str(uid_value),
        item_name=str(name_value) if name_value is not None else None,
        item_order=resolve_order(raw_item, key_config, position),
        config_line=raw_item,
        owner_id=owner_id,
        last_used=parse_last_used(get_value_at_path(raw_item, key_config.last_used_key), key_config.last_used_format),
    )


def normalize_items(
    raw_items: list[dict[str, Any]], key_config: GenericKeyConfig, owner_lookup: dict[str, int]
) -> dict[str, NormalizedGenericItem]:
    """Normalize all delivered items; of several items with the same uid only the first one is kept."""
    normalized: dict[str, NormalizedGenericItem] = {}
    for position, raw_item in enumerate(raw_items):
        item = normalize_item(raw_item, key_config, owner_lookup, position)
        if item is None:
            continue
        if item.item_uid in normalized:
            FWOLogger.warning(f"generic import: duplicate config item uid '{item.item_uid}' - keeping the first one")
            continue
        normalized[item.item_uid] = item
    return normalized


def is_new_version(existing: GenericConfigItemRecord, item: NormalizedGenericItem, last_used_key: str | None) -> bool:
    """Whether an item changed in a way that needs a new version (everything except order and last use)."""
    return (
        existing.item_name != item.item_name
        or existing.owner_id != item.owner_id
        or without_path(existing.config_line, last_used_key) != without_path(item.config_line, last_used_key)
    )


def to_insert_object(
    item: NormalizedGenericItem, mgm_id: int, import_id: int, first_seen: datetime | None
) -> dict[str, Any]:
    """Build the GraphQL insert object of a new item version."""
    insert_object: dict[str, Any] = {
        "mgm_id": mgm_id,
        "item_uid": item.item_uid,
        "item_name": item.item_name,
        "item_order": item.item_order,
        "config_line": item.config_line,
        "owner_id": item.owner_id,
        "last_used": item.last_used.isoformat() if item.last_used else None,
        "created": import_id,
    }
    if first_seen is not None:
        insert_object["first_seen"] = first_seen.isoformat()
    return insert_object


def needs_in_place_update(existing: GenericConfigItemRecord, item: NormalizedGenericItem) -> bool:
    return existing.item_order != item.item_order or existing.last_used != item.last_used


def calculate_changes(
    existing_items: list[GenericConfigItemRecord],
    new_items: dict[str, NormalizedGenericItem],
    key_config: GenericKeyConfig,
    mgm_id: int,
    import_id: int,
) -> GenericItemChanges:
    """Compare the stored active items with the delivered ones and collect the writes needed."""
    changes = GenericItemChanges()
    existing_by_uid = {existing.item_uid: existing for existing in existing_items}
    for uid, item in new_items.items():
        existing = existing_by_uid.get(uid)
        if existing is None:
            changes.new_items.append(to_insert_object(item, mgm_id, import_id, None))
            changes.added += 1
        elif is_new_version(existing, item, key_config.last_used_key):
            changes.removed_ids.append(existing.id)
            changes.new_items.append(to_insert_object(item, mgm_id, import_id, existing.first_seen))
            changes.changed += 1
        elif needs_in_place_update(existing, item):
            changes.in_place_updates.append(
                {
                    "where": {"id": {"_eq": existing.id}},
                    "_set": {
                        "item_order": item.item_order,
                        "last_used": item.last_used.isoformat() if item.last_used else None,
                    },
                }
            )
    for uid, existing in existing_by_uid.items():
        if uid not in new_items:
            changes.removed_ids.append(existing.id)
            changes.deleted += 1
    return changes


class GenericConfigImport:
    """Imports the generic config items of one management (see module docstring)."""

    def __init__(self, import_state: ImportStateController) -> None:
        self.import_state = import_state

    def _call(self, query_file: str, variables: dict[str, Any]) -> dict[str, Any]:
        query = FwoApi.get_graphql_code([GRAPHQL_QUERY_PATH + query_file])
        result = self.import_state.api_call.call(query, query_variables=variables)
        if "errors" in result:
            raise FwoApiWriteError(f"generic import: api call {query_file} failed: {result['errors']!s}")
        return result["data"]

    def get_key_config(self, device_type_name: str, device_type_version: str) -> GenericKeyConfig:
        data = self._call(
            "generic/getGenericKeyConfig.graphql",
            {"devTypName": device_type_name, "devTypVersion": device_type_version},
        )
        configs = data["generic_device_type_key_config"]
        if len(configs) == 0:
            raise FwoImporterError(
                f"generic import: no key configuration found for device type {device_type_name} {device_type_version}"
            )
        return GenericKeyConfig(**configs[0])

    def get_owner_lookup(self, key_config: GenericKeyConfig) -> dict[str, int]:
        if not key_config.owner_key:
            return {}
        data = self._call("generic/getOwnersForGenericMapping.graphql", {})
        owners = [GenericOwnerRef(**owner) for owner in data["owner"]]
        return build_owner_lookup(owners, key_config.owner_match)

    def get_existing_items(self, mgm_id: int) -> list[GenericConfigItemRecord]:
        data = self._call("generic/getActiveGenericConfigItems.graphql", {"mgmId": mgm_id})
        return [GenericConfigItemRecord(**item) for item in data["generic_config_item"]]

    def write_changes(self, changes: GenericItemChanges, import_id: int) -> None:
        if not changes.new_items and not changes.removed_ids and not changes.in_place_updates:
            return
        self._call(
            "generic/updateGenericConfigItems.graphql",
            {
                "importId": import_id,
                "removedIds": changes.removed_ids,
                "newItems": changes.new_items,
                "inPlaceUpdates": changes.in_place_updates,
            },
        )

    def import_items(self, raw_items: list[dict[str, Any]]) -> GenericItemChanges:
        """Bring the stored config items of the management in line with the delivered ones."""
        state = self.import_state.state
        mgm_details = state.mgm_details
        key_config = self.get_key_config(mgm_details.device_type_name, mgm_details.device_type_version)
        normalized = normalize_items(raw_items, key_config, self.get_owner_lookup(key_config))
        changes = calculate_changes(
            self.get_existing_items(mgm_details.mgm_id), normalized, key_config, mgm_details.mgm_id, state.import_id
        )
        self.write_changes(changes, state.import_id)
        stats = state.stats.statistics
        stats.generic_item_add_count += changes.added
        stats.generic_item_change_count += changes.changed
        stats.generic_item_delete_count += changes.deleted
        FWOLogger.info(
            f"generic import of management {mgm_details.name}: {len(normalized)} items, "
            f"{changes.added} added, {changes.changed} changed, {changes.deleted} removed"
        )
        return changes


def import_generic_config(
    import_state: ImportStateController, module: GenericConfigCommon, clear_management_data: bool
) -> GenericItemChanges:
    """Read the config items with the module (or none when clearing the management) and import them."""
    raw_items: list[dict[str, Any]] = [] if clear_management_data else module.get_config_items(import_state)
    return GenericConfigImport(import_state).import_items(raw_items)
