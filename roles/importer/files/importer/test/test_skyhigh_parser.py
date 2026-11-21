from __future__ import annotations

from pathlib import Path
from typing import Any

import pytest
from fw_modules.skyhighwebgateway12ff.skyhigh_parser import (
    MAX_GROUP_DEPTH,
    GroupContext,
    RulesetEntry,
    collect_rules,
    extract_owner,
    format_condition,
    format_parameter_value,
    get_text,
    is_same_origin,
    parse_ruleset_export,
    parse_ruleset_feed,
)
from fwo_exceptions import FwoGenericConfigError

DATA_DIR = Path(__file__).parent / "data"
BASE_URL = "https://swg.example.com:4712/Konfigurator/REST/"
RULESET = RulesetEntry(title="URL Filtering", href=BASE_URL + "rulesets/1001", enabled=True)


def _read(name: str) -> str:
    return (DATA_DIR / name).read_text(encoding="utf-8")


def _items_by_name() -> dict[str, dict[str, Any]]:
    return {item["name"]: item for item in parse_ruleset_export(_read("skyhigh_ruleset_export.xml"), RULESET)}


class TestRulesetFeed:
    def test_lists_rulesets_of_the_same_gateway_only(self) -> None:
        entries = parse_ruleset_feed(_read("skyhigh_rulesets_feed.xml"), BASE_URL)

        assert [entry.title for entry in entries] == ["URL Filtering", "Legacy Rules"]
        assert entries[0].href == BASE_URL + "rulesets/1001"
        assert entries[0].enabled is True
        assert entries[1].href == BASE_URL + "rulesets/1002"
        assert entries[1].enabled is False

    def test_empty_feed(self) -> None:
        assert parse_ruleset_feed("<feed/>", BASE_URL) == []

    def test_invalid_xml_raises(self) -> None:
        with pytest.raises(FwoGenericConfigError, match="rule set list"):
            parse_ruleset_feed("<feed>", BASE_URL)

    @pytest.mark.parametrize(
        ("url", "expected"),
        [
            (BASE_URL + "rulesets/1", True),
            ("http://swg.example.com:4712/Konfigurator/REST/rulesets/1", False),
            ("https://swg.example.com:443/x", False),
            ("https://other.example.com:4712/x", False),
        ],
    )
    def test_same_origin(self, url: str, expected: bool) -> None:
        assert is_same_origin(url, BASE_URL) is expected


class TestRulesetExport:
    def test_one_item_per_rule_in_all_nested_groups(self) -> None:
        assert set(_items_by_name()) == {
            "Block Malware Sites",
            "Allow Intranet",
            "Coach Social Media",
            "Inside Disabled",
        }

    def test_rule_fields(self) -> None:
        item = _items_by_name()["Block Malware Sites"]

        assert item == {
            "uid": "3001",
            "name": "Block Malware Sites",
            "path": "URL Filtering",
            "ruleset": "URL Filtering",
            "enabled": True,
            "condition": "urlfilter.categories in Malware Categories",
            "group_condition": "",
            "action": "block",
            "description": "Blocks known malware categories\nowner: APP-1234",
            "owner": "APP-1234",
        }

    def test_uid_falls_back_to_path_and_owner_tag_is_case_insensitive(self) -> None:
        item = _items_by_name()["Allow Intranet"]

        assert item["uid"] == "URL Filtering > Allow Intranet"
        assert item["owner"] == "APP-9"
        assert item["enabled"] is False
        assert item["condition"] == "always"

    def test_nested_group_path_and_condition_are_inherited(self) -> None:
        item = _items_by_name()["Coach Social Media"]

        assert item["path"] == "URL Filtering > Coaching"
        assert item["group_condition"] == (
            '(authentication.isauthenticated equals true AND header.get("X-Coach") equals "yes")'
        )
        assert item["owner"] is None

    def test_disabled_group_disables_its_rules(self) -> None:
        assert _items_by_name()["Inside Disabled"]["enabled"] is False

    def test_disabled_ruleset_disables_all_rules(self) -> None:
        disabled = RULESET.model_copy(update={"enabled": False})

        items = parse_ruleset_export(_read("skyhigh_ruleset_export.xml"), disabled)

        assert not any(item["enabled"] for item in items)

    def test_empty_export(self) -> None:
        assert parse_ruleset_export("<libraryContent/>", RULESET) == []

    def test_invalid_export_raises(self) -> None:
        with pytest.raises(FwoGenericConfigError, match="URL Filtering"):
            parse_ruleset_export("not xml", RULESET)

    def test_nesting_depth_is_limited(self) -> None:
        context = GroupContext(path="", ruleset="r", enabled=True, group_condition="")

        with pytest.raises(FwoGenericConfigError, match=str(MAX_GROUP_DEPTH)):
            collect_rules({"@name": "deep"}, context, MAX_GROUP_DEPTH + 1, [])


class TestFormatting:
    def test_empty_condition(self) -> None:
        assert format_condition({}) == ""

    @pytest.mark.parametrize(
        ("parameter", "expected"),
        [
            ({"@valueId": "com.scur.type.boolean.false"}, "false"),
            ({"@typeId": "com.scur.type.number", "value": {"stringValue": {"@value": "42"}}}, "42"),
            ({"@typeId": "com.scur.type.string", "value": {"stringValue": "plain"}}, '"plain"'),
            ({"value": {"listValue": {"@id": "list-1"}}}, "list-1"),
            ({"value": {"listValue": "inline"}}, "inline"),
            ({"value": "raw"}, "raw"),
            ({}, ""),
        ],
    )
    def test_parameter_values(self, parameter: dict[str, Any], expected: str) -> None:
        assert format_parameter_value(parameter) == expected

    @pytest.mark.parametrize(
        ("element", "expected"),
        [({"d": None}, ""), ({"d": "text"}, "text"), ({"d": {"#text": "attributed"}}, "attributed"), ({}, "")],
    )
    def test_get_text(self, element: dict[str, Any], expected: str) -> None:
        assert get_text(element, "d") == expected

    @pytest.mark.parametrize(
        ("description", "expected"),
        [
            ("owner: APP-1", "APP-1"),
            ("first line\n  OWNER = APP-2 trailing", "APP-2"),
            ("the owner is APP-3", None),
            ("", None),
        ],
    )
    def test_extract_owner(self, description: str, expected: str | None) -> None:
        assert extract_owner(description) == expected
