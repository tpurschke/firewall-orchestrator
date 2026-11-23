"""
Parser for the rule sets of a Skyhigh Web Gateway (Konfigurator REST API).

Turns the XML of the top-level rule set list and of a rule set export into one generic config item per rule:

- scope: path of rule groups, starting with the top-level rule set
- enabled: rule set, all rule groups above the rule and the rule itself are enabled
- match: effective condition, i.e. the conditions of all rule groups above the rule and the rule's own condition
- action: action of the rule
- owner: external app id from a line 'owner: <id>' of the description
- attributes: ruleset, rule_condition (the rule's own condition), group_condition (the group part alone)
- raw: the rule element of the export as parsed by xmltodict
"""

from __future__ import annotations

import re
from typing import Any, cast
from urllib.parse import urljoin, urlsplit
from xml.parsers.expat import ExpatError

import xmltodict
from fwo_exceptions import FwoGenericConfigError
from fwo_log import FWOLogger
from models.generic_config import GenericConfigItem
from pydantic import BaseModel

PATH_SEPARATOR = " > "
CONDITION_ALWAYS = "always"
TRUE_STRING = "true"
OWNER_TAG_PATTERN = re.compile(r"^\s*owner\s*[:=]\s*(\S+)", re.IGNORECASE | re.MULTILINE)
VENDOR_PREFIXES = [
    "com.scur.engine.system.",
    "com.scur.engine.",
    "com.scur.operator.",
    "com.scur.mainaction.",
    "com.scur.type.boolean.",
    "com.scur.type.",
    "com.scur.",
]
STRING_TYPE_ID = "com.scur.type.string"
MAX_GROUP_DEPTH = 100


class RulesetEntry(BaseModel):
    """Top-level rule set as listed by the rulesets endpoint."""

    title: str
    href: str
    enabled: bool


class GroupContext(BaseModel):
    """What a rule inherits from the rule groups above it."""

    path: str
    ruleset: str
    enabled: bool
    group_conditions: list[str]


def as_dict(value: Any) -> dict[str, Any]:
    """An XML element as dict; anything else (text, empty element) gives an empty dict."""
    return cast("dict[str, Any]", value) if isinstance(value, dict) else {}


def as_list(value: Any) -> list[Any]:
    """Xmltodict returns a single child as dict and repeated children as list - always return a list."""
    if value is None:
        return []
    if isinstance(value, list):
        return cast("list[Any]", value)
    return [value]


def as_dict_list(value: Any) -> list[dict[str, Any]]:
    """Child elements as list of dicts, skipping empty elements."""
    return [as_dict(element) for element in as_list(value) if isinstance(element, dict)]


def strip_vendor_prefix(text: str) -> str:
    for prefix in VENDOR_PREFIXES:
        text = text.replace(prefix, "")
    return text


def is_enabled(element: dict[str, Any]) -> bool:
    return str(element.get("@enabled", TRUE_STRING)).lower() == TRUE_STRING


def get_text(element: dict[str, Any], key: str) -> str:
    """Text content of a child element; xmltodict gives None for empty elements and dicts for attributed ones."""
    value: Any = element.get(key)
    if value is None:
        return ""
    if isinstance(value, dict):
        return str(as_dict(value).get("#text", ""))
    return str(value)


def parse_xml(xml_text: str, what: str) -> dict[str, Any]:
    try:
        return as_dict(xmltodict.parse(xml_text))
    except ExpatError as parse_error:
        raise FwoGenericConfigError(f"skyhigh: cannot parse {what}: {parse_error}") from None


def is_same_origin(url: str, base_url: str) -> bool:
    """Only follow links to the gateway the importer logged in to (the session cookie goes with each call)."""
    target = urlsplit(url)
    base = urlsplit(base_url)
    return (target.scheme, target.netloc) == (base.scheme, base.netloc)


def parse_ruleset_feed(xml_text: str, base_url: str) -> list[RulesetEntry]:
    """Parse the Atom feed of the top-level rule sets."""
    feed = as_dict(parse_xml(xml_text, "rule set list").get("feed"))
    entries: list[RulesetEntry] = []
    for entry in as_dict_list(feed.get("entry")):
        hrefs = [str(link["@href"]) for link in as_dict_list(entry.get("link")) if link.get("@href")]
        if not hrefs:
            continue
        href = urljoin(base_url, hrefs[0])
        if not is_same_origin(href, base_url):
            FWOLogger.warning(f"skyhigh: skipping rule set link to another host: {href}")
            continue
        entries.append(
            RulesetEntry(
                title=get_text(entry, "title"), href=href, enabled=get_text(entry, "enabled").lower() != "false"
            )
        )
    return entries


def format_parameter_value(parameter: dict[str, Any]) -> str:
    if "@valueId" in parameter:
        return strip_vendor_prefix(str(parameter["@valueId"]))
    value: Any = parameter.get("value")
    value_element = as_dict(value)
    if "stringValue" in value_element:
        string_value: Any = value_element["stringValue"]
        text = str(as_dict(string_value).get("@value", "")) if isinstance(string_value, dict) else str(string_value)
        return f'"{text}"' if parameter.get("@typeId") == STRING_TYPE_ID else text
    if "listValue" in value_element:
        list_value: Any = value_element["listValue"]
        if isinstance(list_value, dict):
            list_element = as_dict(list_value)
            return str(list_element.get("@name") or list_element.get("@id") or "")
        return str(list_value)
    return "" if value is None else str(value)


def format_property(property_instance: dict[str, Any]) -> str:
    property_id = strip_vendor_prefix(str(property_instance.get("@propertyId", "")))
    if property_id.isnumeric():
        property_id = f"UserDefined.{property_id}"
    entries = as_dict_list(as_dict(property_instance.get("parameters")).get("entry"))
    values = [format_parameter_value(as_dict(entry.get("parameter"))) for entry in entries if entry.get("parameter")]
    return f"{property_id}({', '.join(values)})" if values else property_id


def format_condition_expression(expression: dict[str, Any]) -> str:
    opening = "(" * int(expression.get("@openingBracketCount", 0) or 0)
    closing = ")" * int(expression.get("@closingBracketCount", 0) or 0)
    prefix = str(expression.get("@prefix", "")).strip()
    property_text = format_property(as_dict(expression.get("propertyInstance")))
    operator = strip_vendor_prefix(str(expression.get("@operatorId", "")))
    parameter = as_dict(expression.get("parameter"))
    value = format_parameter_value(parameter) if parameter else ""
    parts = [part for part in (prefix, f"{opening}{property_text}", operator, f"{value}{closing}") if part]
    return " ".join(parts)


def format_condition(condition: dict[str, Any]) -> str:
    """Readable form of a rule or rule group condition."""
    if not condition:
        return ""
    if str(condition.get("@always", "")).lower() == TRUE_STRING:
        return CONDITION_ALWAYS
    formatted: list[str] = []
    for expressions in as_dict_list(condition.get("expressions")):
        formatted.extend(
            format_condition_expression(expression)
            for expression in as_dict_list(expressions.get("conditionExpression"))
        )
    return " ".join(formatted)


def combine_conditions(conditions: list[str]) -> str:
    """Conditions that must all hold, joined with AND; 'always' and empty conditions are left out."""
    relevant = [condition for condition in conditions if condition and condition != CONDITION_ALWAYS]
    if len(relevant) <= 1:
        return "".join(relevant)
    return " AND ".join(f"({condition})" for condition in relevant)


def extract_owner(description: str) -> str | None:
    """Owner tag of a rule: a description line 'owner: <external app id>'."""
    match = OWNER_TAG_PATTERN.search(description)
    return match.group(1) if match else None


def build_rule_item(rule: dict[str, Any], context: GroupContext) -> GenericConfigItem:
    name = str(rule.get("@name", ""))
    description = get_text(rule, "description")
    action = str(as_dict(rule.get("actionContainer")).get("@actionId", ""))
    rule_id = rule.get("@id")
    rule_condition = format_condition(as_dict(rule.get("condition")))
    effective_condition = combine_conditions([*context.group_conditions, rule_condition]) or CONDITION_ALWAYS
    return GenericConfigItem(
        uid=str(rule_id) if rule_id else f"{context.path}{PATH_SEPARATOR}{name}",
        name=name,
        scope=context.path,
        enabled=context.enabled and is_enabled(rule),
        match=effective_condition,
        action=strip_vendor_prefix(action),
        description=description,
        owner=extract_owner(description),
        attributes={
            "ruleset": context.ruleset,
            "rule_condition": rule_condition,
            "group_condition": combine_conditions(context.group_conditions),
        },
        raw=rule,
    )


def collect_rules(group: dict[str, Any], parent: GroupContext, depth: int, items: list[GenericConfigItem]) -> None:
    """Walk a rule group and its nested groups, appending one item per rule."""
    if depth > MAX_GROUP_DEPTH:
        raise FwoGenericConfigError(f"skyhigh: rule groups nested deeper than {MAX_GROUP_DEPTH} levels")
    group_name = str(group.get("@name", ""))
    group_condition = format_condition(as_dict(group.get("condition")))
    context = GroupContext(
        path=f"{parent.path}{PATH_SEPARATOR}{group_name}" if parent.path else group_name,
        ruleset=parent.ruleset,
        enabled=parent.enabled and is_enabled(group),
        group_conditions=[*parent.group_conditions, group_condition],
    )
    rules = as_dict_list(as_dict(group.get("rules")).get("rule"))
    items.extend(build_rule_item(rule, context) for rule in rules)
    for sub_group in as_dict_list(as_dict(group.get("ruleGroups")).get("ruleGroup")):
        collect_rules(sub_group, context, depth + 1, items)


def parse_ruleset_export(xml_text: str, ruleset: RulesetEntry) -> list[GenericConfigItem]:
    """Parse the export of one top-level rule set into config items."""
    library_content = as_dict(parse_xml(xml_text, f"rule set export of '{ruleset.title}'").get("libraryContent"))
    items: list[GenericConfigItem] = []
    root_context = GroupContext(path="", ruleset=ruleset.title, enabled=ruleset.enabled, group_conditions=[])
    for group in as_dict_list(library_content.get("ruleGroup")):
        collect_rules(group, root_context, 0, items)
    return items
