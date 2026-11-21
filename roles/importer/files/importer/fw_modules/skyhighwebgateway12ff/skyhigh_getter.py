"""HTTP access to the Konfigurator REST API of a Skyhigh Web Gateway."""

from __future__ import annotations

import requests
from fwo_const import FWO_HTTP_TIMEOUT
from fwo_exceptions import FwLoginFailedError, FwoGenericConfigError
from fwo_log import FWOLogger

REST_BASE_PATH = "/Konfigurator/REST/"
RULESETS_PATH = "rulesets?topLevelOnly=true"
EXPORT_SUFFIX = "/export"
HTTP_OK = 200


def build_base_url(hostname: str, port: int) -> str:
    return f"https://{hostname}:{port}{REST_BASE_PATH}"


class SkyhighRestClient:
    """Session based client: login with basic auth, the session cookie authorizes all further calls."""

    def __init__(self, base_url: str, verify: bool | str) -> None:
        self.base_url = base_url
        self.session = requests.Session()
        self.session.verify = verify

    def _send(self, method: str, url: str, what: str) -> requests.Response:
        try:
            return self.session.request(method, url, timeout=FWO_HTTP_TIMEOUT)
        except requests.exceptions.RequestException as request_error:
            raise FwoGenericConfigError(f"skyhigh: {what} failed ({type(request_error).__name__})") from None

    def login(self, user: str, password: str) -> None:
        try:
            response = self.session.post(self.base_url + "login", auth=(user, password), timeout=FWO_HTTP_TIMEOUT)
        except requests.exceptions.RequestException as request_error:
            raise FwLoginFailedError(f"skyhigh: login failed ({type(request_error).__name__})") from None
        if response.status_code != HTTP_OK:
            raise FwLoginFailedError(f"skyhigh: login failed with status {response.status_code}")

    def logout(self) -> None:
        try:
            self.session.post(self.base_url + "logout", timeout=FWO_HTTP_TIMEOUT)
        except requests.exceptions.RequestException as request_error:
            FWOLogger.warning(f"skyhigh: logout failed ({type(request_error).__name__})")
        finally:
            self.session.close()

    def get_text(self, method: str, url: str, what: str) -> str:
        response = self._send(method, url, what)
        if response.status_code != HTTP_OK:
            raise FwoGenericConfigError(f"skyhigh: {what} failed with status {response.status_code}")
        return response.text

    def get_rulesets(self) -> str:
        return self.get_text("GET", self.base_url + RULESETS_PATH, "reading the rule set list")

    def export_ruleset(self, href: str) -> str:
        return self.get_text("POST", href.rstrip("/") + EXPORT_SUFFIX, f"exporting rule set {href}")
