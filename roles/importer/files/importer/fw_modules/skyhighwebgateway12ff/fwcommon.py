"""Importer module for Skyhigh Web Gateway rules (generic, non-firewall config items)."""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

from fw_modules.skyhighwebgateway12ff.skyhigh_getter import SkyhighRestClient, build_base_url
from fw_modules.skyhighwebgateway12ff.skyhigh_parser import parse_ruleset_export, parse_ruleset_feed
from fwo_log import FWOLogger
from models.fw_common import GenericConfigCommon

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController


def read_config_items(client: SkyhighRestClient) -> list[dict[str, Any]]:
    """Export all top-level rule sets and flatten their rules into config items."""
    items: list[dict[str, Any]] = []
    for ruleset in parse_ruleset_feed(client.get_rulesets(), client.base_url):
        ruleset_items = parse_ruleset_export(client.export_ruleset(ruleset.href), ruleset)
        FWOLogger.debug(f"skyhigh: rule set '{ruleset.title}' has {len(ruleset_items)} rules")
        items.extend(ruleset_items)
    return items


class SkyhighWebGateway12ffCommon(GenericConfigCommon):
    def get_config_items(self, import_state: ImportStateController) -> list[dict[str, Any]]:
        mgm_details = import_state.state.mgm_details
        client = SkyhighRestClient(
            build_base_url(mgm_details.hostname, mgm_details.port), import_state.state.verify_certs
        )
        client.login(mgm_details.import_user, mgm_details.secret)
        try:
            return read_config_items(client)
        finally:
            client.logout()
