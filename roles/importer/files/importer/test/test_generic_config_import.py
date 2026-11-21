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
    get_value_at_path,
    import_generic_config,
    is_new_version,
    normalize_items,
    parse_last_used,
    without_path,
)
from models.generic_config import GenericConfigItemRecord, GenericKeyConfig, GenericOwnerRef, NormalizedGenericItem

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController

UTC = timezone.utc
MGM_ID = 3
IMPORT_ID = 42
FIRST_SEEN = datetime(2026, 1, 1, tzinfo=UTC)
KEY_CONFIG = GenericKeyConfig(
    dev_typ_id=33,
    uid_key="uid",
    name_key="name",
    order_key=None,
    owner_key="meta.owner",
    owner_match="ext_id",
    last_used_key="meta.lastUsed",
    last_used_format="iso8601",
    reportable_fields=["name"],
)


def _item(uid: str, name: str = "rule", owner: str | None = "APP-1", last_used: str | None = None) -> dict[str, Any]:
    meta: dict[str, Any] = {"owner": owner}
    if last_used is not None:
        meta["lastUsed"] = last_used
    return {"uid": uid, "name": name, "action": "allow", "meta": meta}


def _record(record_id: int, item: dict[str, Any], owner_id: int | None = 7, order: int = 0) -> GenericConfigItemRecord:
    return GenericConfigItemRecord(
        id=record_id,
        item_uid=item["uid"],
        item_name=item["name"],
        item_order=order,
        config_line=item,
        owner_id=owner_id,
        last_used=None,
        first_seen=FIRST_SEEN,
    )


def _normalize(*items: dict[str, Any]) -> dict[str, NormalizedGenericItem]:
    return normalize_items(list(items), KEY_CONFIG, {"APP-1": 7, "APP-2": 8})


class TestPathHelpers:
    def test_get_value_at_nested_path(self) -> None:
        assert get_value_at_path({"a": {"b": {"c": 5}}}, "a.b.c") == 5

    @pytest.mark.parametrize("path", [None, "", "a.x", "a.b.c.d", "z"])
    def test_get_value_at_missing_path_gives_none(self, path: str | None) -> None:
        assert get_value_at_path({"a": {"b": {"c": 5}}}, path) is None

    def test_without_path_removes_nested_value_and_keeps_original(self) -> None:
        original = {"a": {"b": 1, "c": 2}}

        reduced = without_path(original, "a.b")

        assert reduced == {"a": {"c": 2}}
        assert original == {"a": {"b": 1, "c": 2}}

    @pytest.mark.parametrize("path", [None, "x.y", "a.b.c"])
    def test_without_path_ignores_missing_path(self, path: str | None) -> None:
        assert without_path({"a": {"b": 1}}, path) == {"a": {"b": 1}}


class TestParseLastUsed:
    @pytest.mark.parametrize(
        ("value", "value_format", "expected"),
        [
            ("2026-05-01T10:00:00Z", "iso8601", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            ("2026-05-01T10:00:00", None, datetime(2026, 5, 1, 10, tzinfo=UTC)),
            ("2026-05-01T12:00:00+02:00", "iso8601", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            (1777629600, "unix", datetime(2026, 5, 1, 10, tzinfo=UTC)),
            ("1777629600000", "unix_ms", datetime(2026, 5, 1, 10, tzinfo=UTC)),
        ],
    )
    def test_parses_supported_formats(self, value: Any, value_format: str | None, expected: datetime) -> None:
        assert parse_last_used(value, value_format) == expected

    @pytest.mark.parametrize(
        ("value", "value_format"), [(None, "iso8601"), ("", "unix"), ("never", "iso8601"), ("x", "unix")]
    )
    def test_unparsable_values_give_none(self, value: Any, value_format: str) -> None:
        assert parse_last_used(value, value_format) is None


class TestNormalization:
    def test_owner_lookup_by_ext_id_and_by_name(self) -> None:
        owners = [GenericOwnerRef(id=1, name="App One", app_id_external="APP-1"), GenericOwnerRef(id=2, name="No Ext")]

        assert build_owner_lookup(owners, "ext_id") == {"APP-1": 1}
        assert build_owner_lookup(owners, "name") == {"App One": 1, "No Ext": 2}

    def test_maps_keys_owner_order_and_last_used(self) -> None:
        items = _normalize(_item("r1", owner="APP-2", last_used="2026-05-01T10:00:00Z"), _item("r2", owner="UNKNOWN"))

        assert items["r1"].item_name == "rule"
        assert items["r1"].owner_id == 8
        assert items["r1"].item_order == 0
        assert items["r1"].last_used == datetime(2026, 5, 1, 10, tzinfo=UTC)
        assert items["r2"].owner_id is None
        assert items["r2"].item_order == 1

    def test_skips_items_without_uid_and_duplicates(self) -> None:
        items = normalize_items(
            [{"name": "no uid"}, {"uid": ""}, _item("dup", name="first"), _item("dup", name="second")], KEY_CONFIG, {}
        )

        assert list(items) == ["dup"]
        assert items["dup"].item_name == "first"

    @pytest.mark.parametrize(("order_value", "expected"), [(17, 17), ("5", 5), ("high", 0), (None, 0)])
    def test_order_key_with_fallback_to_position(self, order_value: Any, expected: int) -> None:
        key_config = KEY_CONFIG.model_copy(update={"order_key": "pos"})
        item = _item("r1")
        if order_value is not None:
            item["pos"] = order_value

        assert normalize_items([item], key_config, {})["r1"].item_order == expected


class TestCalculateChanges:
    def test_new_item_is_inserted(self) -> None:
        changes = calculate_changes([], _normalize(_item("r1")), KEY_CONFIG, MGM_ID, IMPORT_ID)

        assert changes.added == 1
        assert changes.new_items[0]["item_uid"] == "r1"
        assert changes.new_items[0]["created"] == IMPORT_ID
        assert changes.new_items[0]["mgm_id"] == MGM_ID
        assert "first_seen" not in changes.new_items[0]

    def test_unchanged_item_needs_no_write(self) -> None:
        changes = calculate_changes([_record(1, _item("r1"))], _normalize(_item("r1")), KEY_CONFIG, MGM_ID, IMPORT_ID)

        assert not changes.new_items
        assert not changes.removed_ids
        assert not changes.in_place_updates

    def test_changed_item_gets_new_version_keeping_first_seen(self) -> None:
        changed = _item("r1")
        changed["action"] = "block"

        changes = calculate_changes([_record(1, _item("r1"))], _normalize(changed), KEY_CONFIG, MGM_ID, IMPORT_ID)

        assert changes.changed == 1
        assert changes.removed_ids == [1]
        assert changes.new_items[0]["first_seen"] == FIRST_SEEN.isoformat()

    def test_owner_change_gives_new_version(self) -> None:
        changes = calculate_changes(
            [_record(1, _item("r1"))], _normalize(_item("r1", owner="APP-2")), KEY_CONFIG, MGM_ID, IMPORT_ID
        )

        assert changes.changed == 1

    def test_vanished_item_is_removed(self) -> None:
        changes = calculate_changes([_record(1, _item("r1"))], {}, KEY_CONFIG, MGM_ID, IMPORT_ID)

        assert changes.deleted == 1
        assert changes.removed_ids == [1]
        assert not changes.new_items

    def test_last_used_and_order_are_updated_in_place(self) -> None:
        existing = _record(1, _item("r1", last_used="2026-01-01T00:00:00Z"), order=5)
        delivered = _normalize(_item("r1", last_used="2026-06-01T00:00:00Z"))

        changes = calculate_changes([existing], delivered, KEY_CONFIG, MGM_ID, IMPORT_ID)

        assert not changes.new_items
        assert changes.in_place_updates == [
            {"where": {"id": {"_eq": 1}}, "_set": {"item_order": 0, "last_used": "2026-06-01T00:00:00+00:00"}}
        ]

    def test_last_used_value_is_ignored_for_versioning(self) -> None:
        existing = _record(1, _item("r1", last_used="2026-01-01T00:00:00Z"))
        delivered = _normalize(_item("r1", last_used="2026-06-01T00:00:00Z"))["r1"]

        assert is_new_version(existing, delivered, KEY_CONFIG.last_used_key) is False
        assert is_new_version(existing, delivered, None) is True


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
        stored = _item("old")
        existing = [_record(1, stored).model_dump(mode="json")]
        self._call_mock(generic_state).side_effect = _api_results(existing)

        changes = GenericConfigImport(generic_state).import_items([_item("new")])

        write_call = self._call_mock(generic_state).call_args_list[-1]
        variables = write_call.kwargs["query_variables"]
        assert variables["importId"] == IMPORT_ID
        assert variables["removedIds"] == [1]
        assert variables["newItems"][0]["owner_id"] == 7
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

    def test_owner_lookup_skipped_without_owner_key(self, generic_state: ImportStateController) -> None:
        key_config = KEY_CONFIG.model_copy(update={"owner_key": None}).model_dump()
        results = _api_results([], [key_config])
        del results[1]
        self._call_mock(generic_state).side_effect = results

        changes = GenericConfigImport(generic_state).import_items([_item("r1")])

        assert changes.new_items[0]["owner_id"] is None

    def test_missing_key_config_fails_import(self, generic_state: ImportStateController) -> None:
        self._call_mock(generic_state).side_effect = _api_results([], [])

        with pytest.raises(FwoImporterError, match="no key configuration"):
            GenericConfigImport(generic_state).import_items([])

    def test_api_errors_fail_import(self, generic_state: ImportStateController) -> None:
        self._call_mock(generic_state).return_value = {"errors": ["permission denied"]}

        with pytest.raises(FwoApiWriteError):
            GenericConfigImport(generic_state).import_items([])

    def test_clearing_removes_all_items_without_reading_the_device(self, generic_state: ImportStateController) -> None:
        existing = [_record(1, _item("r1")).model_dump(mode="json")]
        self._call_mock(generic_state).side_effect = _api_results(existing)
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
    changed = _item("r1", name="renamed")

    changes = calculate_changes([record], _normalize(changed), KEY_CONFIG, MGM_ID, IMPORT_ID)

    assert changes.new_items[0]["first_seen"] == later.isoformat()
