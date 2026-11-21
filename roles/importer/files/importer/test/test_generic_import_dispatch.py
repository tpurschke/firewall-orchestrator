# pyright: reportPrivateUsage=false
from __future__ import annotations

from socket import gethostname
from typing import TYPE_CHECKING, cast
from unittest.mock import MagicMock

import common
import pytest
from fw_modules.skyhighwebgateway12ff.fwcommon import SkyhighWebGateway12ffCommon

if TYPE_CHECKING:
    from pathlib import Path

    from model_controllers.import_state_controller import ImportStateController
    from pytest_mock import MockerFixture

MGM_ID = 3
IMPORT_ID = 77


def _set_device_type(import_state_controller: ImportStateController, name: str, version: str) -> None:
    import_state_controller.state.mgm_details.device_type_name = name
    import_state_controller.state.mgm_details.device_type_version = version


class TestModuleSelection:
    def test_skyhigh_package_name(self, import_state_controller: ImportStateController) -> None:
        _set_device_type(import_state_controller, "Skyhigh Web Gateway", "12ff")

        assert common.get_module_package_name(import_state_controller.state) == "skyhighwebgateway12ff"
        assert isinstance(common.get_generic_module(import_state_controller.state), SkyhighWebGateway12ffCommon)
        assert isinstance(common.get_module(import_state_controller.state), SkyhighWebGateway12ffCommon)

    def test_firewall_types_have_no_generic_module(self, import_state_controller: ImportStateController) -> None:
        _set_device_type(import_state_controller, "Check Point", "R8x")

        assert common.get_generic_module(import_state_controller.state) is None


class TestGenericImportBranch:
    @pytest.fixture
    def mocks(
        self, mocker: MockerFixture, import_state_controller: ImportStateController, tmp_path: Path
    ) -> dict[str, MagicMock]:
        state = import_state_controller.state
        state.mgm_details.importer_hostname = gethostname()
        state.force_import = False
        cast("MagicMock", import_state_controller.api_call.set_import_lock).return_value = IMPORT_ID
        mocker.patch("common.IMPORT_TMP_PATH", str(tmp_path / "import"))
        mocker.patch("common.ManagementController.build_gateway_list", return_value=[])
        return {
            "generic_import": mocker.patch("common.import_generic_config"),
            "get_config": mocker.patch("common.get_config_top_level"),
            "config_importer": mocker.patch("common.FwConfigImport").return_value,
        }

    @pytest.mark.parametrize("clear_management_data", [False, True])
    def test_generic_device_skips_firewall_import(
        self,
        mocks: dict[str, MagicMock],
        import_state_controller: ImportStateController,
        clear_management_data: bool,
    ) -> None:
        _set_device_type(import_state_controller, "Skyhigh Web Gateway", "12ff")

        common._import_management(
            MGM_ID, file=None, limit=100, clear_management_data=clear_management_data, suppress_consistency_check=False
        )

        assert import_state_controller.state.import_id == IMPORT_ID
        call = mocks["generic_import"].call_args
        assert call.args[0] is import_state_controller
        assert isinstance(call.args[1], SkyhighWebGateway12ffCommon)
        assert call.args[2] is clear_management_data
        mocks["get_config"].assert_not_called()
        mocks["config_importer"].import_management_set.assert_not_called()
        mocks["config_importer"].clear_management.assert_not_called()

    def test_firewall_device_does_not_use_generic_import(
        self, mocks: dict[str, MagicMock], import_state_controller: ImportStateController
    ) -> None:
        _set_device_type(import_state_controller, "Check Point", "R8x")
        mocks["get_config"].return_value = (False, MagicMock())

        common._import_management(
            MGM_ID, file=None, limit=100, clear_management_data=False, suppress_consistency_check=False
        )

        mocks["generic_import"].assert_not_called()
        mocks["get_config"].assert_called_once()
