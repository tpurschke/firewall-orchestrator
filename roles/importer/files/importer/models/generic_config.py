"""Models of generic (non-firewall) config items such as proxy or load balancer rules."""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

# order and last use are stored in their own columns and change without the item changing
CONFIG_LINE_EXCLUDED_FIELDS = {"order", "last_used"}


class GenericConfigItem(BaseModel):
    """
    Rule-like config item of a generic (non-firewall) device as delivered by an importer module.

    The core fields mean the same for all device types, so reports and the recertification page show
    the same columns for all of them:

    - uid: stable unique id of the item within its management
    - scope: where the item lives on the device (rule set and group path, partition, policy, ...)
    - order: position among the items of the management; without it the position in the delivered list is used
    - enabled: whether the item is in effect, including everything it inherits (disabled parent groups, ...)
    - match: readable form of what the item applies to (effective condition, virtual address, ...)
    - action: readable form of what the item does (block, redirect, forward to pool, ...)
    - owner: owner name or external app id, mapped as configured in generic.device_type_key_config
    - last_used: last use, needed by the unused items report

    Vendor-specific normalized values go into attributes, the original object of the device into raw.
    """

    model_config = ConfigDict(extra="forbid")

    uid: str = Field(min_length=1)
    name: str | None = None
    scope: str | None = None
    order: int | None = None
    enabled: bool = True
    match: str | None = None
    action: str | None = None
    description: str | None = None
    owner: str | None = None
    last_used: datetime | None = None
    attributes: dict[str, Any] = {}
    raw: dict[str, Any] = {}

    @field_validator("last_used")
    @classmethod
    def assume_utc(cls, value: datetime | None) -> datetime | None:
        """Timestamps without time zone are taken as UTC."""
        if value is not None and value.tzinfo is None:
            return value.replace(tzinfo=timezone.utc)
        return value

    def to_config_line(self) -> dict[str, Any]:
        """JSON object stored as generic.config_item.config_line."""
        return self.model_dump(mode="json", exclude=CONFIG_LINE_EXCLUDED_FIELDS)


class GenericKeyConfig(BaseModel):
    """Per device type settings the generic import needs (part of generic.device_type_key_config)."""

    dev_typ_id: int
    owner_match: Literal["name", "ext_id"] = "name"


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
    """Config item read from the device, mapped to the columns of generic.config_item."""

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
