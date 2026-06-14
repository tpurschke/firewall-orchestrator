"""Unit tests for fw_modules/checkpointR8x/fwcommon.py helper functions."""

from copy import deepcopy
from typing import TYPE_CHECKING, Any, cast

import pytest
from fw_modules.checkpointR8x import cp_const, fwcommon
from fwo_exceptions import FwoImporterError, ImportInterruptionError
from model_controllers.fwconfigmanagerlist_controller import FwConfigManagerListController
from model_controllers.fworch_config_controller import FworchConfigController
from model_controllers.import_state_controller import ImportStateController
from model_controllers.management_controller import ManagementController
from models.import_state import ImportState
from pytest_mock import MockerFixture

if TYPE_CHECKING:
    from unittest.mock import MagicMock


def _build_import_state(management_controller: ManagementController) -> ImportState:
    """Build an ImportState carrying the attributes the fwcommon helpers read."""
    import_state = ImportState()
    import_state.mgm_details = management_controller
    import_state.fwo_config = FworchConfigController(
        fwo_api_url=None,
        fwo_user_mgmt_api_uri=None,
        importer_pwd=None,
        api_fetch_size=500,
    )
    import_state.import_id = 1
    import_state.force_import = False
    return import_state


def _missing_full_config() -> FwConfigManagerListController:
    return cast("FwConfigManagerListController", None)


def _as_import_state_controller(import_state: ImportState) -> ImportStateController:
    return cast("ImportStateController", import_state)


class TestCreateOrderedManagerList:
    def test_single_manager(self, management_controller: ManagementController) -> None:
        import_state: ImportState = _build_import_state(management_controller)

        result: list[ManagementController] = fwcommon.create_ordered_manager_list(import_state)

        assert len(result) == 1
        assert result[0].mgm_id == management_controller.mgm_id

    def test_super_manager_includes_sub_managers(self, management_controller: ManagementController) -> None:
        management_controller.is_super_manager = True
        sub_manager: ManagementController = deepcopy(management_controller)
        sub_manager.is_super_manager = False
        management_controller.sub_managers = [sub_manager]
        import_state: ImportState = _build_import_state(management_controller)

        result: list[ManagementController] = fwcommon.create_ordered_manager_list(import_state)

        assert len(result) == 2


class TestInitializeNativeConfig:
    def test_builds_domain_structure(self, management_controller: ManagementController) -> None:
        config_in: FwConfigManagerListController = FwConfigManagerListController()
        config_in.native_config = {}
        import_state: ImportState = _build_import_state(management_controller)

        fwcommon.initialize_native_config(config_in, import_state)

        assert "domains" in config_in.native_config
        assert len(config_in.native_config["domains"]) == 1
        domain: dict[str, Any] = config_in.native_config["domains"][0]
        assert domain["management_name"] == management_controller.name
        assert domain["objects"] == []
        assert domain["rulebases"] == []

    def test_raises_when_native_config_none(self, management_controller: ManagementController) -> None:
        config_in: FwConfigManagerListController = FwConfigManagerListController()
        config_in.native_config = None
        import_state: ImportState = _build_import_state(management_controller)

        with pytest.raises(FwoImporterError):
            fwcommon.initialize_native_config(config_in, import_state)


class TestEnsureNativeDomains:
    def test_skips_when_domains_present(self, management_controller: ManagementController) -> None:
        native_config: dict[str, Any] = {"domains": ["existing"]}
        import_state: ImportState = _build_import_state(management_controller)

        fwcommon.ensure_native_domains(native_config, import_state)

        assert native_config["domains"] == ["existing"]

    def test_creates_domain_from_mgm_details(self, management_controller: ManagementController) -> None:
        native_config: dict[str, Any] = {"objects": ["obj"], "rulebases": ["rb"]}
        import_state: ImportState = _build_import_state(management_controller)

        fwcommon.ensure_native_domains(native_config, import_state)

        assert len(native_config["domains"]) == 1
        domain: dict[str, Any] = native_config["domains"][0]
        assert domain["objects"] == ["obj"]
        assert domain["rulebases"] == ["rb"]
        assert domain["nat_rulebases"] == []


class TestInitializeDeviceConfig:
    def test_valid_device(self) -> None:
        device: dict[str, Any] = {"name": "fw1", "uid": "uid-1"}

        result: dict[str, Any] = fwcommon.initialize_device_config(device)

        assert result == {"name": "fw1", "uid": "uid-1", "rulebase_links": []}

    def test_missing_uid_raises(self) -> None:
        with pytest.raises(FwoImporterError):
            fwcommon.initialize_device_config({"name": "fw1"})


class TestDefineInitialRulebase:
    def test_appends_initial_link(self) -> None:
        device_config: dict[str, Any] = {"rulebase_links": []}

        fwcommon.define_initial_rulebase(device_config, ["layer-uid"], is_global=True)

        link: dict[str, Any] = device_config["rulebase_links"][0]
        assert link["to_rulebase_uid"] == "layer-uid"
        assert link["is_initial"] is True
        assert link["is_global"] is True
        assert link["from_rulebase_uid"] is None


class TestGetRulesParams:
    def test_returns_expected_params(self, management_controller: ManagementController) -> None:
        import_state: ImportState = _build_import_state(management_controller)

        params: dict[str, Any] = fwcommon.get_rules_params(import_state)

        assert params["limit"] == 500
        assert params["use-object-dictionary"] == cp_const.use_object_dictionary
        assert params["details-level"] == "standard"
        assert params["show-hits"] == cp_const.with_hits


class TestGetOrderedLayerUids:
    def test_collects_matching_layers(self) -> None:
        policy_structure: list[dict[str, Any]] = [
            {
                "targets": [{"uid": "dev-1"}],
                "access-layers": [
                    {"uid": "layer-a", "domain": "dom"},
                    {"uid": "layer-b", "domain": "other"},
                ],
            }
        ]
        device_config: dict[str, Any] = {"uid": "dev-1"}

        result: list[str] = fwcommon.get_ordered_layer_uids(policy_structure, device_config, "dom")

        assert result == ["layer-a"]

    def test_matches_all_target_and_empty_domain(self) -> None:
        policy_structure: list[dict[str, Any]] = [
            {
                "targets": [{"uid": "all"}],
                "access-layers": [{"uid": "layer-a", "domain": "anything"}],
            }
        ]
        device_config: dict[str, Any] = {"uid": "dev-9"}

        result: list[str] = fwcommon.get_ordered_layer_uids(policy_structure, device_config, "")

        assert result == ["layer-a"]

    def test_no_matching_target(self) -> None:
        policy_structure: list[dict[str, Any]] = [
            {
                "targets": [{"uid": "other-dev"}],
                "access-layers": [{"uid": "layer-a", "domain": "dom"}],
            }
        ]
        device_config: dict[str, Any] = {"uid": "dev-1"}

        result: list[str] = fwcommon.get_ordered_layer_uids(policy_structure, device_config, "dom")

        assert result == []


class TestRemovePredefinedObjectsForDomains:
    def test_skips_types_to_remove_globals_from(self) -> None:
        object_table: dict[str, Any] = {
            "type": cp_const.types_to_remove_globals_from[0],
            "chunks": [{"objects": [{"domain": {"domain-type": "global"}}]}],
        }

        fwcommon.remove_predefined_objects_for_domains(object_table)

        # untouched because type is in skip list
        assert len(object_table["chunks"][0]["objects"]) == 1

    def test_removes_non_domain_objects(self) -> None:
        object_table: dict[str, Any] = {
            "type": "hosts",
            "chunks": [
                {
                    "objects": [
                        {"name": "keep", "domain": {"domain-type": "domain"}},
                        {"name": "drop", "domain": {"domain-type": "global"}},
                    ]
                }
            ],
        }

        fwcommon.remove_predefined_objects_for_domains(object_table)

        remaining: list[str] = [obj["name"] for obj in object_table["chunks"][0]["objects"]]
        assert "keep" in remaining


class TestGetObjectsPerType:
    def test_paginates_chunks(self, mocker: MockerFixture) -> None:
        mocker.patch.object(fwcommon.fwo_globals, "shutdown_requested", new=False)
        mocker.patch.object(
            fwcommon.cp_getter,
            "cp_api_call",
            return_value={"total": 1, "to": 1, "objects": []},
        )

        result: dict[str, Any] = fwcommon.get_objects_per_type("networks", {}, "sid", "https://cp/web_api/")

        assert result["type"] == "networks"
        assert len(result["chunks"]) == 1

    def test_shutdown_requested_raises(self, mocker: MockerFixture) -> None:
        mocker.patch.object(fwcommon.fwo_globals, "shutdown_requested", new=True)

        with pytest.raises(ImportInterruptionError):
            fwcommon.get_objects_per_type("networks", {}, "sid", "https://cp/web_api/")


class TestAddSpecialObjectsToGlobalDomain:
    def test_appends_for_networks(self, mocker: MockerFixture) -> None:
        mocker.patch.object(
            fwcommon.cp_getter,
            "get_object_details_from_api",
            return_value={"chunks": [{"obj": "special"}]},
        )
        object_table: dict[str, Any] = {"type": "networks", "chunks": []}

        fwcommon.add_special_objects_to_global_domain(object_table, "networks", "sid", "https://cp/web_api/")

        # orig, any, none, internet -> four appended
        assert len(object_table["chunks"]) == 4

    def test_no_append_for_other_types(self, mocker: MockerFixture) -> None:
        mocker.patch.object(
            fwcommon.cp_getter,
            "get_object_details_from_api",
            return_value={"chunks": [{"obj": "special"}]},
        )
        object_table: dict[str, Any] = {"type": "hosts", "chunks": []}

        fwcommon.add_special_objects_to_global_domain(object_table, "hosts", "sid", "https://cp/web_api/")

        assert object_table["chunks"] == []


class TestHandleNatRules:
    def test_appends_nat_rules_when_package(
        self, mocker: MockerFixture, management_controller: ManagementController
    ) -> None:
        management_controller.device_type_name = "Check Point"
        import_state: ImportState = _build_import_state(management_controller)
        mocker.patch.object(
            fwcommon.cp_getter,
            "get_nat_rules_from_api_as_dict",
            return_value={"nat_rule_chunks": ["rule"]},
        )
        native_config_domain: dict[str, Any] = {"nat_rulebases": []}

        fwcommon.handle_nat_rules({"package_name": "pkg"}, native_config_domain, "sid", import_state)

        assert native_config_domain["nat_rulebases"] == [{"nat_rule_chunks": ["rule"]}]

    def test_appends_empty_when_no_package(self, management_controller: ManagementController) -> None:
        import_state: ImportState = _build_import_state(management_controller)
        native_config_domain: dict[str, Any] = {"nat_rulebases": []}

        fwcommon.handle_nat_rules({}, native_config_domain, "sid", import_state)

        assert native_config_domain["nat_rulebases"] == [{"nat_rule_chunks": []}]


class TestAddOrderedLayersToNativeConfig:
    def test_links_consecutive_layers(self, mocker: MockerFixture) -> None:
        mocker.patch.object(fwcommon.cp_getter, "get_rulebases", return_value=["rb-uid"])
        device_config: dict[str, Any] = {"rulebase_links": []}

        result: list[str] = fwcommon.add_ordered_layers_to_native_config(
            ["layer-1", "layer-2"],
            {},
            "https://cp/web_api/",
            "sid",
            {"rulebases": []},
            device_config,
            is_global=True,
            global_ordered_layer_count=0,
        )

        assert result == ["rb-uid"]
        # one link between layer-1 and layer-2
        links: list[dict[str, Any]] = device_config["rulebase_links"]
        assert any(link["to_rulebase_uid"] == "layer-2" for link in links)


class TestDefineGlobalRulebaseLink:
    def test_links_placeholder_to_local_layer(self, mocker: MockerFixture) -> None:
        mocker.patch.object(
            fwcommon.cp_getter,
            "get_placeholder_in_rulebase",
            return_value=("placeholder-rule", "placeholder-rb"),
        )
        device_config: dict[str, Any] = {"rulebase_links": []}
        native_config_global_domain: dict[str, Any] = {"rulebases": [{"uid": "global-rb"}]}

        fwcommon.define_global_rulebase_link(
            device_config,
            ["global-layer"],
            ["local-layer"],
            native_config_global_domain,
            ["global-rb"],
        )

        # initial link + domain link
        assert len(device_config["rulebase_links"]) == 2
        domain_link: dict[str, Any] = device_config["rulebase_links"][1]
        assert domain_link["type"] == "domain"
        assert domain_link["to_rulebase_uid"] == "local-layer"
        assert domain_link["from_rulebase_uid"] == "placeholder-rb"


class TestGetObjectsPerDomain:
    def test_collects_object_tables(self, mocker: MockerFixture, management_controller: ManagementController) -> None:
        management_controller.device_type_name = "Check Point"
        mocker.patch.object(fwcommon.cp_getter, "login", return_value="sid")
        mocker.patch.object(
            fwcommon,
            "get_objects_per_type",
            return_value={"type": "hosts", "chunks": []},
        )
        mocker.patch.object(fwcommon, "add_special_objects_to_global_domain")
        remove: MagicMock = mocker.patch.object(fwcommon, "remove_predefined_objects_for_domains")
        native_domain: dict[str, Any] = {"objects": []}

        fwcommon.get_objects_per_domain(
            management_controller,
            native_domain,
            ["hosts"],
            {},
            is_stand_alone_manager=True,
        )

        assert len(native_domain["objects"]) == 1
        # standalone manager skips predefined removal
        remove.assert_not_called()


class TestGetObjects:
    def test_standalone_manager_collects_objects(
        self, mocker: MockerFixture, management_controller: ManagementController
    ) -> None:
        import_state: ImportState = _build_import_state(management_controller)
        per_domain: MagicMock = mocker.patch.object(fwcommon, "get_objects_per_domain")
        native_config_dict: dict[str, Any] = {"domains": [{"objects": []}]}

        result: int = fwcommon.get_objects(native_config_dict, import_state)

        assert result == 0
        per_domain.assert_called_once()

    def test_super_manager_fetches_predefined_and_global(
        self, mocker: MockerFixture, management_controller: ManagementController
    ) -> None:
        management_controller.is_super_manager = True
        import_state: ImportState = _build_import_state(management_controller)
        per_domain: MagicMock = mocker.patch.object(fwcommon, "get_objects_per_domain")
        native_config_dict: dict[str, Any] = {"domains": [{"objects": []}]}

        result: int = fwcommon.get_objects(native_config_dict, import_state)

        assert result == 0
        # super manager fetches Check Point Data + Global domain
        assert per_domain.call_count == 2


class TestHasConfigChanged:
    def test_returns_true_when_full_config_passed(self, management_controller: ManagementController) -> None:
        common: fwcommon.CheckpointR8xCommon = fwcommon.CheckpointR8xCommon()
        import_state: ImportState = _build_import_state(management_controller)
        full_config: FwConfigManagerListController = FwConfigManagerListController.generate_empty_config()

        assert common.has_config_changed(full_config, _as_import_state_controller(import_state)) is True

    def test_full_import_when_no_last_import(
        self, mocker: MockerFixture, management_controller: ManagementController
    ) -> None:
        common: fwcommon.CheckpointR8xCommon = fwcommon.CheckpointR8xCommon()
        import_state: ImportState = _build_import_state(management_controller)
        import_state.last_successful_import = None
        mocker.patch.object(fwcommon.cp_getter, "login", return_value="sid")
        mocker.patch.object(ManagementController, "buildFwApiString", return_value="https://cp/web_api/", create=True)
        logout: MagicMock = mocker.patch.object(fwcommon.cp_getter, "logout")

        assert common.has_config_changed(_missing_full_config(), _as_import_state_controller(import_state)) is True
        logout.assert_called_once()

    def test_detects_changes_since_last_import(
        self, mocker: MockerFixture, management_controller: ManagementController
    ) -> None:
        common: fwcommon.CheckpointR8xCommon = fwcommon.CheckpointR8xCommon()
        import_state: ImportState = _build_import_state(management_controller)
        import_state.last_successful_import = "2020-01-01T00:00:00"
        mocker.patch.object(fwcommon.cp_getter, "login", return_value="sid")
        mocker.patch.object(ManagementController, "buildFwApiString", return_value="https://cp/web_api/", create=True)
        mocker.patch.object(fwcommon.cp_getter, "get_changes", return_value=3)
        mocker.patch.object(fwcommon.cp_getter, "logout")

        assert common.has_config_changed(_missing_full_config(), _as_import_state_controller(import_state)) is True
