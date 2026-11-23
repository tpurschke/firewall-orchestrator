from __future__ import annotations

from abc import ABC, abstractmethod
from typing import TYPE_CHECKING

from fwo_exceptions import FwoImporterError, RuleHitsNotSupportedError

if TYPE_CHECKING:
    from model_controllers.fwconfigmanagerlist_controller import FwConfigManagerListController
    from model_controllers.import_state_controller import ImportStateController
    from models.generic_config import GenericConfigItem
    from models.rule_hit import RuleHit


class FwCommon(ABC):
    def has_config_changed(
        self, _full_config: FwConfigManagerListController, _import_state: ImportStateController, _force: bool = False
    ) -> bool:
        return True

    @abstractmethod
    def get_config(
        self, config_in: FwConfigManagerListController, import_state: ImportStateController
    ) -> tuple[int, FwConfigManagerListController]:
        raise NotImplementedError("Please Implement this method")

    def supports_rule_hits(self) -> bool:
        """Return whether the vendor API of this module supplies the last hit of rules (rule hit import)."""
        return False

    def get_rule_hits(self, _import_state: ImportStateController) -> list[RuleHit]:
        """
        Read the last hit of all rules per gateway from the firewall management (rule hit import).

        Modules returning True from supports_rule_hits must override this method.
        """
        raise RuleHitsNotSupportedError(f"{type(self).__name__} cannot read rule hits")


class GenericConfigCommon(FwCommon):
    """
    Base class of importer modules for generic (non-firewall) devices such as proxies or load balancers.

    These modules do not deliver a normalized firewall config but a flat list of rule-like config items
    (GenericConfigItem: the same core fields for all device types, vendor-specific values in attributes and
    the original object in raw). The generic import step maps them to generic.config_item.
    """

    def get_config(
        self,
        config_in: FwConfigManagerListController,  # noqa: ARG002 - generic modules have no firewall config
        import_state: ImportStateController,  # noqa: ARG002
    ) -> tuple[int, FwConfigManagerListController]:
        raise FwoImporterError(f"{type(self).__name__} delivers generic config items, not a firewall config")

    @abstractmethod
    def get_config_items(self, import_state: ImportStateController) -> list[GenericConfigItem]:
        """Read all rule-like config items of the management (full set, not incremental)."""
        raise NotImplementedError("Please Implement this method")
