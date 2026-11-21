"""Models of generic (non-firewall) config items such as proxy or load balancer rules."""

from __future__ import annotations

from datetime import datetime  # noqa: TC003 - Pydantic resolves this type at runtime
from typing import Any, Literal

from pydantic import BaseModel


class GenericKeyConfig(BaseModel):
    """
    Meaning of the JSON keys of the config items of one device type (generic.device_type_key_config).

    All *_key values are dot-separated paths into a config item.
    """

    dev_typ_id: int
    uid_key: str
    name_key: str | None = None
    order_key: str | None = None
    owner_key: str | None = None
    owner_match: Literal["name", "ext_id"] = "name"
    last_used_key: str | None = None
    last_used_format: Literal["iso8601", "unix", "unix_ms"] | None = None
    reportable_fields: list[str] = []


class GenericOwnerRef(BaseModel):
    """Owner data needed to map the owner value of a config item to an owner id."""

    id: int
    name: str
    app_id_external: str | None = None


class GenericConfigItemRecord(BaseModel):
    """Active version of a config item as stored in generic.config_item."""

    id: int
    item_uid: str
    item_name: str | None = None
    item_order: int | None = None
    config_line: dict[str, Any]
    owner_id: int | None = None
    last_used: datetime | None = None
    first_seen: datetime


class NormalizedGenericItem(BaseModel):
    """Config item read from the device, with the key configuration applied."""

    item_uid: str
    item_name: str | None = None
    item_order: int
    config_line: dict[str, Any]
    owner_id: int | None = None
    last_used: datetime | None = None


class GenericItemChanges(BaseModel):
    """Writes needed to bring generic.config_item of one management up to date."""

    new_items: list[dict[str, Any]] = []
    removed_ids: list[int] = []
    in_place_updates: list[dict[str, Any]] = []
    added: int = 0
    changed: int = 0
    deleted: int = 0
