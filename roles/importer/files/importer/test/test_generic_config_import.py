from __future__ import annotations

from datetime import datetime, timedelta, timezone
from typing import TYPE_CHECKING, Any, cast
from unittest.mock import MagicMock

import pytest
from fwo_exceptions import FwoApiWriteError, FwoImporterError
from model_controllers.generic_config_import import (
    GenericConfigImport,
    build_owner_lookup,
    calculate_changes,
    import_generic_config,
    is_new_version,
    normalize_items,
)
from models.generic_config import (
    GenericConfigItem,
    GenericConfigItemRecord,
    GenericKeyConfig,
    GenericOwnerRef,
    NormalizedGenericItem,
)
from pydantic import ValidationError

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController

UTC = timezone.utc
MGM_ID = 3
IMPORT_ID = 42
FIRST_SEEN = datetime(2026, 1, 1, tzinfo=UTC)
KEY_CONFIG = GenericKeyConfig(dev_typ_id=33, owner_match="ext_id")


def _item(
    uid: str,
    name: str = "rule",
    owner: str | None = "APP-1",
    last_used: datetime | None = None,
    order: int | None = None,
) -> GenericConfigItem:
    return GenericConfigItem(
        uid=uid,
        name=name,
        scope="Rule Set > Group",
        order=order,
        match="host in Partners",
        action="allow",
        owner=owner,
        last_used=last_used,
        attributes={"pool": "web_pool"},
        raw={"@id": uid},
    )


def _record(
    record_id: int, item: GenericConfigItem, owner_id: int | None = 7, order: int = 0
) -> GenericConfigItemRecord:
    return GenericConfigItemRecord(
        id=record_id,
        item_uid=item.uid,
        item_name=item.name,
        item_order=order,
        config_line=item.to_config_line(),
        owner_id=owner_id,
        last_used=item.last_used,
        first_seen=FIRST_SEEN,
    )


def _normalize(*items: GenericConfigItem) -> dict[str, NormalizedGenericItem]:
    return normalize_items(list(items), {"APP-1": 7, "APP-2": 8})


class TestGenericConfigItem:
    def test_config_line_holds_everything_except_order_and_last_used(self) -> None:
        item = _item("r1", last_used=datetime(2026, 5, 1, tzinfo=UTC), order=3)

        assert item.to_config_line() == {
            "uid": "r1",
            "name": "rule",
            "scope": "Rule Set > Group",
            "enabled": True,
            "match": "host in Partners",
            "action": "allow",
            "description": None,
            "owner": "APP-1",
            "attributes": {"pool": "web_pool"},
            "raw": {"@id": "r1"},
        }

    @pytest.mark.parametrize(
        ("value", "expected"),
        [
            ("2026-05-01T10:00:00Z", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            ("2026-05-01T10:00:00", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            ("2026-05-01T12:00:00+02:00", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            (datetime(2026, 5, 1, 10), datetime(2026, 5, 1, 10, tzinfo=UTC)),  # noqa: DTZ001 - naive on purpose
            (None, None),
        ],
    )
    def test_last_used_is_time_zone_aware(self, value: Any, expected: datetime | None) -> None:
        assert GenericConfigItem(uid="r1", last_used=value).last_used == expected

    @pytest.mark.parametrize("data", [{"uid": ""}, {"name": "no uid"}, {"uid": "r1", "path": "unknown field"}])
    def test_invalid_items_are_rejected(self, data: dict[str, Any]) -> None:
        with pytest.raises(ValidationError):
            GenericConfigItem(**data)


class TestNormalization:
    def test_owner_lookup_by_ext_id_and_by_name(self) -> None:
        owners = [GenericOwnerRef(id=1, name="App One", app_id_external="APP-1"), GenericOwnerRef(id=2, name="No Ext")]

        assert build_owner_lookup(owners, "ext_id") == {"APP-1": 1}
        assert build_owner_lookup(owners, "name") == {"App One": 1, "No Ext": 2}

    def test_maps_core_fields_to_columns(self) -> None:
        last_used = datetime(2026, 5, 1, 10, tzinfo=UTC)
        items = _normalize(_item("r1", owner="APP-2", last_used=last_used), _item("r2", owner="UNKNOWN"))

        assert items["r1"].item_name == "rule"
        assert items["r1"].owner_id == 8
        assert items["r1"].item_order == 0
        assert items["r1"].last_used == last_used
        assert items["r1"].config_line["raw"] == {"@id": "r1"}
        assert items["r2"].owner_id is None
        assert items["r2"].item_order == 1

    def test_keeps_the_first_of_duplicate_uids(self) -> None:
        items = _normalize(_item("dup", name="first"), _item("dup", name="second"))

        assert list(items) == ["dup"]
        assert items["dup"].item_name == "first"

    @pytest.mark.parametrize(("order", "expected"), [(17, 17), (None, 1)])
    def test_order_with_fallback_to_position(self, order: int | None, expected: int) -> None:
        assert _normalize(_item("r0"), _item("r1", order=order))["r1"].item_order == expected


class TestCalculateChanges:
    def test_new_item_is_inserted(self) -> None:
        changes = calculate_changes([], _normalize(_item("r1")), MGM_ID, IMPORT_ID)

        assert changes.added == 1
        assert changes.new_items[0]["item_uid"] == "r1"
        assert changes.new_items[0]["created"] == IMPORT_ID
        assert changes.new_items[0]["mgm_id"] == MGM_ID
        assert "first_seen" not in changes.new_items[0]

    def test_unchanged_item_needs_no_write(self) -> None:
        changes = calculate_changes([_record(1, _item("r1"))], _normalize(_item("r1")), MGM_ID, IMPORT_ID)

        assert not changes.new_items
        assert not changes.removed_ids
        assert not changes.in_place_updates

    @pytest.mark.parametrize(
        "update",
        [{"action": "block"}, {"enabled": False}, {"attributes": {"pool": "other"}}, {"raw": {"@id": "changed"}}],
    )
    def test_changed_item_gets_new_version_keeping_first_seen(self, update: dict[str, Any]) -> None:
        changed = _item("r1").model_copy(update=update)

        changes = calculate_changes([_record(1, _item("r1"))], _normalize(changed), MGM_ID, IMPORT_ID)

        assert changes.changed == 1
        assert changes.removed_ids == [1]
        assert changes.new_items[0]["first_seen"] == FIRST_SEEN.isoformat()

    def test_owner_change_gives_new_version(self) -> None:
        changes = calculate_changes(
            [_record(1, _item("r1"))], _normalize(_item("r1", owner="APP-2")), MGM_ID, IMPORT_ID
        )

        assert changes.changed == 1

    def test_vanished_item_is_removed(self) -> None:
        changes = calculate_changes([_record(1, _item("r1"))], {}, MGM_ID, IMPORT_ID)

        assert changes.deleted == 1
        assert changes.removed_ids == [1]
        assert not changes.new_items

    def test_last_used_and_order_are_updated_in_place(self) -> None:
        existing = _record(1, _item("r1", last_used=datetime(2026, 1, 1, tzinfo=UTC)), order=5)
        delivered = _normalize(_item("r1", last_used=datetime(2026, 6, 1, tzinfo=UTC)))

        changes = calculate_changes([existing], delivered, MGM_ID, IMPORT_ID)

        assert not changes.new_items
        assert changes.in_place_updates == [
            {"where": {"id": {"_eq": 1}}, "_set": {"item_order": 0, "last_used": "2026-06-01T00:00:00+00:00"}}
        ]

    def test_last_used_and_order_are_ignored_for_versioning(self) -> None:
        existing = _record(1, _item("r1", last_used=datetime(2026, 1, 1, tzinfo=UTC)), order=5)
        delivered = _normalize(_item("r1", last_used=datetime(2026, 6, 1, tzinfo=UTC), order=9))["r1"]

        assert is_new_version(existing, delivered) is False


def _api_results(
    existing: list[dict[str, Any]], key_configs: list[dict[str, Any]] | None = None
) -> list[dict[str, Any]]:
    key_config_rows = key_configs if key_configs is not None else [KEY_CONFIG.model_dump()]
    return [
        {"data": {"generic_device_type_key_config": key_config_rows}},
        {"data": {"owner": [{"id": 7, "name": "App One", "app_id_external": "APP-1"}]}},
        {"data": {"generic_config_item": existing}},
        {"data": {}},
    ]


class TestGenericConfigImport:
    @pytest.fixture
    def generic_state(self, import_state_controller: ImportStateController) -> ImportStateController:
        import_state_controller.state.import_id = IMPORT_ID
        import_state_controller.state.mgm_details.device_type_name = "Skyhigh Web Gateway"
        import_state_controller.state.mgm_details.device_type_version = "12ff"
        return import_state_controller

    @staticmethod
    def _call_mock(state: ImportStateController) -> MagicMock:
        return cast("MagicMock", state.api_call.call)

    def test_writes_changes_and_counts_them(self, generic_state: ImportStateController) -> None:
        existing = [_record(1, _item("old")).model_dump(mode="json")]
        self._call_mock(generic_state).side_effect = _api_results(existing)

        changes = GenericConfigImport(generic_state).import_items([_item("new")])

        write_call = self._call_mock(generic_state).call_args_list[-1]
        variables = write_call.kwargs["query_variables"]
        assert variables["importId"] == IMPORT_ID
        assert variables["removedIds"] == [1]
        assert variables["newItems"][0]["owner_id"] == 7
        assert variables["newItems"][0]["config_line"]["attributes"] == {"pool": "web_pool"}
        assert changes.added == 1
        assert changes.deleted == 1
        statistics = generic_state.state.stats.statistics
        assert statistics.generic_item_add_count == 1
        assert statistics.generic_item_delete_count == 1
        assert generic_state.state.stats.get_total_change_number() == 2
        assert generic_state.state.stats.get_rule_change_number() == 0

    def test_no_write_without_changes(self, generic_state: ImportStateController) -> None:
        existing = [_record(1, _item("r1")).model_dump(mode="json")]
        self._call_mock(generic_state).side_effect = _api_results(existing)

        GenericConfigImport(generic_state).import_items([_item("r1")])

        assert self._call_mock(generic_state).call_count == 3

    def test_owner_lookup_skipped_without_owners(self, generic_state: ImportStateController) -> None:
        results = _api_results([])
        del results[1]
        self._call_mock(generic_state).side_effect = results

        changes = GenericConfigImport(generic_state).import_items([_item("r1", owner=None)])

        assert changes.new_items[0]["owner_id"] is None

    def test_missing_key_config_fails_import(self, generic_state: ImportStateController) -> None:
        self._call_mock(generic_state).side_effect = _api_results([], [])

        generic_import = GenericConfigImport(generic_state)

        with pytest.raises(FwoImporterError, match="no key configuration"):
            generic_import.import_items([])

    def test_api_errors_fail_import(self, generic_state: ImportStateController) -> None:
        self._call_mock(generic_state).return_value = {"errors": ["permission denied"]}

        generic_import = GenericConfigImport(generic_state)

        with pytest.raises(FwoApiWriteError):
            generic_import.import_items([])

    def test_clearing_removes_all_items_without_reading_the_device(self, generic_state: ImportStateController) -> None:
        existing = [_record(1, _item("r1")).model_dump(mode="json")]
        results = _api_results(existing)
        del results[1]  # without items there are no owners to look up
        self._call_mock(generic_state).side_effect = results
        module = MagicMock()

        changes = import_generic_config(generic_state, module, clear_management_data=True)

        module.get_config_items.assert_not_called()
        assert changes.removed_ids == [1]

    def test_reads_items_with_module(self, generic_state: ImportStateController) -> None:
        self._call_mock(generic_state).side_effect = _api_results([])
        module = MagicMock()
        module.get_config_items.return_value = [_item("r1")]

        changes = import_generic_config(generic_state, module, clear_management_data=False)

        module.get_config_items.assert_called_once_with(generic_state)
        assert changes.added == 1


def test_first_seen_of_new_version_is_kept_in_time_order() -> None:
    later = FIRST_SEEN + timedelta(days=30)
    record = _record(1, _item("r1")).model_copy(update={"first_seen": later})

    changes = calculate_changes([record], _normalize(_item("r1", name="renamed")), MGM_ID, IMPORT_ID)

    assert changes.new_items[0]["first_seen"] == later.isoformat()
