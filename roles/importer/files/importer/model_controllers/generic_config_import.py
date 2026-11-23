"""
Import step for generic (non-firewall) config items such as proxy or load balancer rules.

The importer module delivers the full list of config items of a management as GenericConfigItem objects.
This step maps them to generic.config_item and brings the stored items up to date:

- a new item gets a new row,
- a changed item (config line, name or owner) gets a new version, the old one is marked removed,
- a vanished item is marked removed,
- order and last-used value are updated in place, as they change without the item changing.

Recertification data is never touched here.
"""

from __future__ import annotations

from datetime import datetime  # noqa: TC003 - Pydantic resolves this type at runtime
from typing import TYPE_CHECKING, Any

from fwo_api import FwoApi
from fwo_const import GRAPHQL_QUERY_PATH
from fwo_exceptions import FwoApiWriteError, FwoImporterError
from fwo_log import FWOLogger
from models.generic_config import (
    GenericConfigItem,
    GenericConfigItemRecord,
    GenericItemChanges,
    GenericKeyConfig,
    GenericOwnerRef,
    NormalizedGenericItem,
)

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController
    from models.fw_common import GenericConfigCommon


def build_owner_lookup(owners: list[GenericOwnerRef], owner_match: str) -> dict[str, int]:
    """Map owner name or external app id (depending on owner_match) to the owner id."""
    lookup: dict[str, int] = {}
    for owner in owners:
        key = owner.app_id_external if owner_match == "ext_id" else owner.name
        if key:
            lookup[key] = owner.id
    return lookup


def normalize_item(item: GenericConfigItem, owner_lookup: dict[str, int], position: int) -> NormalizedGenericItem:
    """Map one delivered item to the columns of generic.config_item."""
    owner_id = owner_lookup.get(item.owner) if item.owner else None
    if item.owner and owner_id is None:
        FWOLogger.debug(f"generic import: no owner found for '{item.owner}' of item {item.uid}")
    return NormalizedGenericItem(
        item_uid=item.uid,
        item_name=item.name,
        item_order=item.order if item.order is not None else position,
        config_line=item.to_config_line(),
        owner_id=owner_id,
        last_used=item.last_used,
    )


def normalize_items(items: list[GenericConfigItem], owner_lookup: dict[str, int]) -> dict[str, NormalizedGenericItem]:
    """Normalize all delivered items; of several items with the same uid only the first one is kept."""
    normalized: dict[str, NormalizedGenericItem] = {}
    for position, item in enumerate(items):
        if item.uid in normalized:
            FWOLogger.warning(f"generic import: duplicate config item uid '{item.uid}' - keeping the first one")
            continue
        normalized[item.uid] = normalize_item(item, owner_lookup, position)
    return normalized


def is_new_version(existing: GenericConfigItemRecord, item: NormalizedGenericItem) -> bool:
    """Whether an item changed in a way that needs a new version (everything except order and last use)."""
    return (
        existing.item_name != item.item_name
        or existing.owner_id != item.owner_id
        or existing.config_line != item.config_line
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
        elif is_new_version(existing, item):
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

    def get_owner_lookup(self, key_config: GenericKeyConfig, items: list[GenericConfigItem]) -> dict[str, int]:
        if not any(item.owner for item in items):
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

    def import_items(self, items: list[GenericConfigItem]) -> GenericItemChanges:
        """Bring the stored config items of the management in line with the delivered ones."""
        state = self.import_state.state
        mgm_details = state.mgm_details
        key_config = self.get_key_config(mgm_details.device_type_name, mgm_details.device_type_version)
        normalized = normalize_items(items, self.get_owner_lookup(key_config, items))
        changes = calculate_changes(
            self.get_existing_items(mgm_details.mgm_id), normalized, mgm_details.mgm_id, state.import_id
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
    items: list[GenericConfigItem] = [] if clear_management_data else module.get_config_items(import_state)
    return GenericConfigImport(import_state).import_items(items)
