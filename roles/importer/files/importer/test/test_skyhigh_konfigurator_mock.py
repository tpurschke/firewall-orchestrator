from __future__ import annotations

import base64
import threading
from typing import TYPE_CHECKING

import pytest
import requests
from fw_modules.skyhighwebgateway12ff.fwcommon import SkyhighWebGateway12ffCommon
from fw_modules.skyhighwebgateway12ff.skyhigh_getter import SkyhighRestClient
from fwo_exceptions import FwLoginFailedError, FwoGenericConfigError, FwoImporterError
from test.mock_servers.skyhigh_konfigurator_mock import (
    DEFAULT_PORT,
    REST_PREFIX,
    SAMPLE_DATA_DIR,
    KonfiguratorState,
    MockServerConfig,
    MockServerHandle,
    build_ruleset_feed,
    main,
    parse_args,
    read_ruleset,
    read_rulesets,
    start_mock_server,
)

if TYPE_CHECKING:
    from collections.abc import Iterator
    from pathlib import Path

    from model_controllers.import_state_controller import ImportStateController

USER = "mockadmin"
PASSWORD = "mock-secret"  # noqa: S105 - test credential of the mock


@pytest.fixture
def mock_server(tmp_path: Path) -> Iterator[MockServerHandle]:
    handle = start_mock_server(MockServerConfig(password=PASSWORD, user=USER, port=0, certificate_dir=tmp_path))
    yield handle
    handle.stop()


def _client(handle: MockServerHandle) -> SkyhighRestClient:
    return SkyhighRestClient(handle.base_url, str(handle.certificate_file))


class TestSampleData:
    def test_sample_rule_sets(self) -> None:
        rulesets = read_rulesets(SAMPLE_DATA_DIR)

        assert [(ruleset.ruleset_id, ruleset.title, ruleset.enabled) for ruleset in rulesets] == [
            ("1001", "Global Block Lists", True),
            ("1002", "URL Filtering", True),
            ("1003", "Legacy Exceptions", False),
        ]

    @pytest.mark.parametrize("content", ["<libraryContent/>", "<other><ruleGroup name='x'/></other>", "not xml"])
    def test_invalid_rule_set_files_are_rejected(self, tmp_path: Path, content: str) -> None:
        path = tmp_path / "broken.xml"
        path.write_text(content, encoding="utf-8")

        with pytest.raises(FwoImporterError, match=r"broken\.xml"):
            read_ruleset(path)

    def test_feed_escapes_titles(self) -> None:
        state_rulesets = read_rulesets(SAMPLE_DATA_DIR)[:1]
        tricky = [type(state_rulesets[0])(ruleset_id="1", title="<a & b>", enabled=True, export_xml="")]

        feed = build_ruleset_feed(tricky, "https://gw:1/Konfigurator/REST/")

        assert "<title>&lt;a &amp; b&gt;</title>" in feed
        assert 'href="https://gw:1/Konfigurator/REST/rulesets/rulegroups/1"' in feed


class TestSessionHandling:
    @pytest.mark.parametrize(
        ("authorization", "expected"),
        [
            (None, False),
            ("Bearer x", False),
            ("Basic !!!", False),
            ("Basic " + base64.b64encode(b"\xff\xfe").decode(), False),
            ("Basic " + base64.b64encode(f"{USER}:wrong".encode()).decode(), False),
            ("Basic " + base64.b64encode(f"{USER}:{PASSWORD}".encode()).decode(), True),
        ],
    )
    def test_check_credentials(self, authorization: str | None, expected: bool) -> None:
        state = KonfiguratorState(user=USER, password=PASSWORD, rulesets=[])

        assert state.check_credentials(authorization) is expected

    def test_sessions_are_capped(self) -> None:
        state = KonfiguratorState(user=USER, password=PASSWORD, rulesets=[])
        first = state.open_session()
        for _ in range(100):
            state.open_session()

        assert not state.has_session(first)
        assert not state.has_session(None)


class TestAgainstTheModule:
    def test_module_reads_all_rules_and_logs_out(
        self, mock_server: MockServerHandle, import_state_controller: ImportStateController
    ) -> None:
        mgm_details = import_state_controller.state.mgm_details
        mgm_details.hostname = "127.0.0.1"
        mgm_details.port = mock_server.port
        mgm_details.import_user = USER
        mgm_details.secret = PASSWORD
        import_state_controller.state.verify_certs = str(mock_server.certificate_file)

        items = SkyhighWebGateway12ffCommon().get_config_items(import_state_controller)

        by_uid = {item.uid: item for item in items}
        assert sorted(by_uid) == ["10011", "10012", "10021", "10022", "10031", "11001", "11002"]
        assert by_uid["10011"].owner == "123"
        assert by_uid["10022"].owner == "234"
        assert by_uid["10031"].owner is None
        assert by_uid["10031"].enabled is False
        assert by_uid["11002"].enabled is False
        assert by_uid["11001"].scope == "URL Filtering > Coaching"
        assert by_uid["11001"].attributes["group_condition"] == "authentication.isauthenticated equals true"
        assert by_uid["11001"].match == (
            "(authentication.isauthenticated equals true) AND (urlfilter.categories atLeastOneInList Social Networking)"
        )
        assert by_uid["11001"].raw["@id"] == "11001"
        assert by_uid["10021"].match == "urlfilter.categories atLeastOneInList Malicious Categories"
        assert mock_server.server.state.sessions == set()

    def test_wrong_password_is_rejected(self, mock_server: MockServerHandle) -> None:
        with pytest.raises(FwLoginFailedError, match="401"):
            _client(mock_server).login(USER, "wrong")

    def test_calls_need_a_session(self, mock_server: MockServerHandle) -> None:
        with pytest.raises(FwoGenericConfigError, match="401"):
            _client(mock_server).get_rulesets()

    def test_session_ends_with_logout(self, mock_server: MockServerHandle) -> None:
        client = _client(mock_server)
        client.login(USER, PASSWORD)
        session_cookie = client.session.cookies.get("JSESSIONID")
        client.logout()

        response = requests.get(
            mock_server.base_url + "rulesets",
            cookies={"JSESSIONID": session_cookie or ""},
            verify=str(mock_server.certificate_file),
            timeout=5,
        )
        assert response.status_code == 401

    @pytest.mark.parametrize(
        ("method", "path", "expected"),
        [
            ("POST", REST_PREFIX + "rulesets/rulegroups/9999/export", 404),
            ("GET", REST_PREFIX + "rulesets/rulegroups/1001/export", 404),
            ("GET", REST_PREFIX + "unknown", 404),
            ("GET", "/other/path", 404),
            ("GET", REST_PREFIX + "login", 404),
        ],
    )
    def test_unknown_calls(self, mock_server: MockServerHandle, method: str, path: str, expected: int) -> None:
        client = _client(mock_server)
        client.login(USER, PASSWORD)
        base = mock_server.base_url[: -len(REST_PREFIX)]

        response = client.session.request(method, base + path, timeout=5)

        assert response.status_code == expected

    def test_invalid_session_cookie(self, mock_server: MockServerHandle) -> None:
        response = requests.get(
            mock_server.base_url + "rulesets",
            headers={"Cookie": 'JSESSIONID="unterminated'},
            verify=str(mock_server.certificate_file),
            timeout=5,
        )
        assert response.status_code == 401

    def test_certificate_is_checked(self, mock_server: MockServerHandle) -> None:
        with pytest.raises(FwLoginFailedError, match="SSLError"):
            SkyhighRestClient(mock_server.base_url, True).login(USER, PASSWORD)


class TestCommandLine:
    def test_defaults(self, monkeypatch: pytest.MonkeyPatch) -> None:
        monkeypatch.setenv("SKYHIGH_MOCK_PASSWORD", "from-env")

        config = parse_args([])

        assert config.port == DEFAULT_PORT
        assert config.password == "from-env"  # noqa: S105
        assert config.data_dir == SAMPLE_DATA_DIR

    def test_password_is_required(self) -> None:
        with pytest.raises(FwoImporterError, match="password"):
            start_mock_server(MockServerConfig(password=""))

    def test_main_serves_until_stopped(self, tmp_path: Path) -> None:
        first = start_mock_server(MockServerConfig(password=PASSWORD, port=0, certificate_dir=tmp_path))
        certificate_file = first.certificate_file
        key_file = tmp_path / "skyhigh-mock.key"
        first.stop()
        stop_event = threading.Event()
        stop_event.set()

        result = main(
            ["--port", "0", "--password", PASSWORD, "--cert", str(certificate_file), "--key", str(key_file)],
            stop_event,
        )

        assert result == 0
