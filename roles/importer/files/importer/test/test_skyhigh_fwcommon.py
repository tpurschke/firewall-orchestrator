from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING
from unittest.mock import MagicMock

import pytest
import requests
from fw_modules.skyhighwebgateway12ff import fwcommon
from fw_modules.skyhighwebgateway12ff.fwcommon import SkyhighWebGateway12ffCommon, read_config_items
from fw_modules.skyhighwebgateway12ff.skyhigh_getter import SkyhighRestClient, build_base_url
from fwo_exceptions import FwLoginFailedError, FwoGenericConfigError, FwoImporterError

if TYPE_CHECKING:
    from model_controllers.import_state_controller import ImportStateController
    from pytest_mock import MockerFixture

DATA_DIR = Path(__file__).parent / "data"
BASE_URL = "https://swg.example.com:4712/Konfigurator/REST/"


def _response(status: int, text: str = "") -> MagicMock:
    response = MagicMock()
    response.status_code = status
    response.text = text
    return response


@pytest.fixture
def client(mocker: MockerFixture) -> SkyhighRestClient:
    session = MagicMock()
    mocker.patch("fw_modules.skyhighwebgateway12ff.skyhigh_getter.requests.Session", return_value=session)
    return SkyhighRestClient(BASE_URL, True)


def _session(client: SkyhighRestClient) -> MagicMock:
    return client.session  # type: ignore[return-value]


class TestRestClient:
    def test_base_url(self) -> None:
        assert build_base_url("swg.example.com", 4712) == BASE_URL

    def test_login_uses_basic_auth(self, client: SkyhighRestClient) -> None:
        _session(client).post.return_value = _response(200)

        client.login("user", "secret")

        args, kwargs = _session(client).post.call_args
        assert args[0] == BASE_URL + "login"
        assert kwargs["auth"] == ("user", "secret")

    def test_login_rejected(self, client: SkyhighRestClient) -> None:
        _session(client).post.return_value = _response(401)

        with pytest.raises(FwLoginFailedError, match="401"):
            client.login("user", "wrong")

    def test_login_connection_error(self, client: SkyhighRestClient) -> None:
        _session(client).post.side_effect = requests.exceptions.ConnectionError("down")

        with pytest.raises(FwLoginFailedError, match="ConnectionError"):
            client.login("user", "secret")

    def test_rulesets_and_export(self, client: SkyhighRestClient) -> None:
        _session(client).request.side_effect = [_response(200, "<feed/>"), _response(200, "<libraryContent/>")]

        assert client.get_rulesets() == "<feed/>"
        assert client.export_ruleset(BASE_URL + "rulesets/1/") == "<libraryContent/>"

        calls = _session(client).request.call_args_list
        assert calls[0].args == ("GET", BASE_URL + "rulesets?topLevelOnly=true")
        assert calls[1].args == ("POST", BASE_URL + "rulesets/1/export")

    def test_http_error_status(self, client: SkyhighRestClient) -> None:
        _session(client).request.return_value = _response(500)

        with pytest.raises(FwoGenericConfigError, match="500"):
            client.get_rulesets()

    def test_request_exception(self, client: SkyhighRestClient) -> None:
        _session(client).request.side_effect = requests.exceptions.Timeout()

        with pytest.raises(FwoGenericConfigError, match="Timeout"):
            client.get_rulesets()

    def test_logout_errors_are_only_logged(self, client: SkyhighRestClient) -> None:
        _session(client).post.side_effect = requests.exceptions.ConnectionError()

        client.logout()

        _session(client).close.assert_called_once()


class TestModule:
    def test_reads_all_rulesets(self) -> None:
        client = MagicMock()
        client.base_url = BASE_URL
        client.get_rulesets.return_value = (DATA_DIR / "skyhigh_rulesets_feed.xml").read_text(encoding="utf-8")
        client.export_ruleset.return_value = (DATA_DIR / "skyhigh_ruleset_export.xml").read_text(encoding="utf-8")

        items = read_config_items(client)

        assert client.export_ruleset.call_count == 2
        assert len(items) == 8

    def test_get_config_items_logs_in_and_out(
        self, mocker: MockerFixture, import_state_controller: ImportStateController
    ) -> None:
        client = MagicMock()
        client_class = mocker.patch.object(fwcommon, "SkyhighRestClient", return_value=client)
        mocker.patch.object(fwcommon, "read_config_items", return_value=[{"uid": "1"}])

        items = SkyhighWebGateway12ffCommon().get_config_items(import_state_controller)

        assert items == [{"uid": "1"}]
        assert client_class.call_args.args[0] == "https://mock.example.com:443/Konfigurator/REST/"
        client.login.assert_called_once_with("mock-user", "mock-secret")
        client.logout.assert_called_once()

    def test_logout_after_failed_read(
        self, mocker: MockerFixture, import_state_controller: ImportStateController
    ) -> None:
        client = MagicMock()
        mocker.patch.object(fwcommon, "SkyhighRestClient", return_value=client)
        mocker.patch.object(fwcommon, "read_config_items", side_effect=FwoGenericConfigError("broken"))

        with pytest.raises(FwoGenericConfigError):
            SkyhighWebGateway12ffCommon().get_config_items(import_state_controller)

        client.logout.assert_called_once()

    def test_firewall_config_is_not_supported(self, import_state_controller: ImportStateController) -> None:
        with pytest.raises(FwoImporterError, match="generic config items"):
            SkyhighWebGateway12ffCommon().get_config(MagicMock(), import_state_controller)
