"""
Mock of the Konfigurator REST API of a Skyhigh Web Gateway (on-prem), for tests and demos (#5789).

It serves the calls the importer module skyhighwebgateway12ff uses, as described in the Skyhigh
documentation of the REST interface ("Working with rule sets"):

    POST <base>/login                                  basic auth, starts a session (cookie JSESSIONID)
    GET  <base>/rulesets?topLevelOnly=true             Atom feed of the top-level rule sets
    POST <base>/rulesets/rulegroups/<id>/export        XML export of one rule set
    POST <base>/logout                                 ends the session

with <base> = https://<host>:<port>/Konfigurator/REST. Every call but login needs a valid session.
The rule sets are read from a directory with one export file (<libraryContent>) per top-level rule set.
The mock only shows that the module matches our reading of the documentation; it is no replacement for a
test against a real gateway.

Run it from the importer directory:

    importer-venv/bin/python -m test.mock_servers.skyhigh_konfigurator_mock --port 4712 --password <password>

Without --cert/--key a self-signed certificate for the host is created; its path is logged so the importer
can trust it.
"""

from __future__ import annotations

import argparse
import base64
import binascii
import datetime
import hmac
import ipaddress
import os
import secrets
import ssl
import tempfile
import threading
from dataclasses import dataclass, field
from http import HTTPStatus
from http.cookies import CookieError, SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, cast
from urllib.parse import urlsplit
from xml.parsers.expat import ExpatError
from xml.sax.saxutils import escape, quoteattr

import xmltodict
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID
from fwo_exceptions import FwoImporterError
from fwo_log import FWOLogger

REST_PREFIX = "/Konfigurator/REST/"
SESSION_COOKIE = "JSESSIONID"
RULESET_PATH_PREFIX = "rulesets/rulegroups/"
EXPORT_SUFFIX = "/export"
XML_CONTENT_TYPE = "application/xml; charset=utf-8"
ATOM_CONTENT_TYPE = "application/atom+xml; charset=utf-8"
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 4712
DEFAULT_USER = "mockadmin"
PASSWORD_ENV_VARIABLE = "SKYHIGH_MOCK_PASSWORD"  # noqa: S105 - name of the variable, not a password
SAMPLE_DATA_DIR = Path(__file__).resolve().parent.parent / "data" / "skyhigh_mock"
CERTIFICATE_VALIDITY_DAYS = 30
MAX_SESSIONS = 100
SESSION_ID_BYTES = 24


@dataclass(frozen=True)
class MockRuleset:
    """One top-level rule set: id, title and enabled flag of its top rule group, and its export XML."""

    ruleset_id: str
    title: str
    enabled: bool
    export_xml: str


def read_ruleset(path: Path) -> MockRuleset:
    """Read one rule set export file; it must contain a <libraryContent> with one top <ruleGroup>."""
    export_xml = path.read_text(encoding="utf-8")
    try:
        document: dict[str, Any] = xmltodict.parse(export_xml)
    except ExpatError as parse_error:
        raise FwoImporterError(f"skyhigh mock: {path.name} is no valid XML: {parse_error}") from None
    library_content: Any = document.get("libraryContent")
    rule_group: Any = (
        cast("dict[str, Any]", library_content).get("ruleGroup") if isinstance(library_content, dict) else None
    )
    if not isinstance(rule_group, dict) or "@id" not in rule_group:
        raise FwoImporterError(f"skyhigh mock: {path.name} contains no rule set with id")
    group = cast("dict[str, Any]", rule_group)
    return MockRuleset(
        ruleset_id=str(group["@id"]),
        title=str(group.get("@name", "")),
        enabled=str(group.get("@enabled", "true")).lower() == "true",
        export_xml=export_xml,
    )


def read_rulesets(directory: Path) -> list[MockRuleset]:
    """Read all *.xml rule set exports of a directory in file name order."""
    return [read_ruleset(path) for path in sorted(directory.glob("*.xml"))]


def build_ruleset_feed(rulesets: list[MockRuleset], base_url: str) -> str:
    """Atom feed listing the top-level rule sets, each with a link to its rule group."""
    entries = "".join(
        "  <entry>\n"
        f"    <id>{escape(ruleset.ruleset_id)}</id>\n"
        f"    <title>{escape(ruleset.title)}</title>\n"
        f'    <link href={quoteattr(base_url + RULESET_PATH_PREFIX + ruleset.ruleset_id)} rel="self"/>\n'
        f"    <enabled>{'true' if ruleset.enabled else 'false'}</enabled>\n"
        "  </entry>\n"
        for ruleset in rulesets
    )
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<feed xmlns="http://www.w3.org/2005/Atom">\n'
        "  <title>rulesets</title>\n"
        f"{entries}"
        "</feed>\n"
    )


def _empty_sessions() -> set[str]:
    return set()


@dataclass
class KonfiguratorState:
    """Credentials, rule sets and open sessions of the mock; sessions are shared by all request threads."""

    user: str
    password: str
    rulesets: list[MockRuleset]
    sessions: set[str] = field(default_factory=_empty_sessions)
    lock: threading.Lock = field(default_factory=threading.Lock)

    def check_credentials(self, authorization: str | None) -> bool:
        """Check a basic auth header in constant time."""
        if not authorization or not authorization.startswith("Basic "):
            return False
        try:
            decoded = base64.b64decode(authorization[len("Basic ") :], validate=True).decode("utf-8")
        except (binascii.Error, UnicodeDecodeError):
            return False
        user, _, password = decoded.partition(":")
        return hmac.compare_digest(user, self.user) & hmac.compare_digest(password, self.password)

    def open_session(self) -> str:
        session_id = secrets.token_urlsafe(SESSION_ID_BYTES)
        with self.lock:
            if len(self.sessions) >= MAX_SESSIONS:
                self.sessions.clear()
            self.sessions.add(session_id)
        return session_id

    def has_session(self, session_id: str | None) -> bool:
        with self.lock:
            return session_id is not None and session_id in self.sessions

    def close_session(self, session_id: str) -> None:
        with self.lock:
            self.sessions.discard(session_id)

    def find_ruleset(self, ruleset_id: str) -> MockRuleset | None:
        return next((ruleset for ruleset in self.rulesets if ruleset.ruleset_id == ruleset_id), None)


class KonfiguratorServer(ThreadingHTTPServer):
    """HTTP(S) server holding the mock state for its request handlers."""

    daemon_threads = True

    def __init__(self, address: tuple[str, int], state: KonfiguratorState) -> None:
        super().__init__(address, KonfiguratorHandler)
        self.state = state


class KonfiguratorHandler(BaseHTTPRequestHandler):
    """Answers the Konfigurator REST calls; everything else gets 404."""

    @property
    def _state(self) -> KonfiguratorState:
        return cast("KonfiguratorServer", self.server).state

    def log_message(self, format: str, *args: Any) -> None:  # noqa: A002 - name given by BaseHTTPRequestHandler
        FWOLogger.debug(f"skyhigh mock: {format % args}")

    def do_GET(self) -> None:
        self._dispatch("GET")

    def do_POST(self) -> None:
        self._dispatch("POST")

    def _dispatch(self, method: str) -> None:
        path = urlsplit(self.path).path
        if not path.startswith(REST_PREFIX):
            self._send(HTTPStatus.NOT_FOUND)
            return
        resource = path[len(REST_PREFIX) :].rstrip("/")
        if method == "POST" and resource == "login":
            self._login()
            return
        session_id = self._session_id()
        if not self._state.has_session(session_id):
            self._send(HTTPStatus.UNAUTHORIZED)
            return
        self._handle_authenticated(method, resource, cast("str", session_id))

    def _handle_authenticated(self, method: str, resource: str, session_id: str) -> None:
        if method == "POST" and resource == "logout":
            self._state.close_session(session_id)
            self._send(HTTPStatus.OK)
        elif method == "GET" and resource == "rulesets":
            self._send(HTTPStatus.OK, build_ruleset_feed(self._state.rulesets, self._base_url()), ATOM_CONTENT_TYPE)
        elif method == "POST" and resource.startswith(RULESET_PATH_PREFIX) and resource.endswith(EXPORT_SUFFIX):
            ruleset_id = resource[len(RULESET_PATH_PREFIX) : -len(EXPORT_SUFFIX)]
            ruleset = self._state.find_ruleset(ruleset_id)
            if ruleset is None:
                self._send(HTTPStatus.NOT_FOUND)
            else:
                self._send(HTTPStatus.OK, ruleset.export_xml, XML_CONTENT_TYPE)
        else:
            self._send(HTTPStatus.NOT_FOUND)

    def _login(self) -> None:
        if not self._state.check_credentials(self.headers.get("Authorization")):
            self._send(HTTPStatus.UNAUTHORIZED)
            return
        session_id = self._state.open_session()
        cookie = f"{SESSION_COOKIE}={session_id}; Path=/Konfigurator; HttpOnly; Secure"
        self._send(HTTPStatus.OK, extra_headers={"Set-Cookie": cookie})

    def _session_id(self) -> str | None:
        cookie_header = self.headers.get("Cookie")
        if not cookie_header:
            return None
        cookies: SimpleCookie = SimpleCookie()
        try:
            cookies.load(cookie_header)
        except CookieError:
            return None
        morsel = cookies.get(SESSION_COOKIE)
        return morsel.value if morsel else None

    def _base_url(self) -> str:
        server_address = cast("tuple[str, int]", self.server.server_address)
        host = self.headers.get("Host") or f"{server_address[0]}:{server_address[1]}"
        return f"https://{host}{REST_PREFIX}"

    def _send(
        self,
        status: HTTPStatus,
        body: str = "",
        content_type: str = XML_CONTENT_TYPE,
        extra_headers: dict[str, str] | None = None,
    ) -> None:
        payload = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        for name, value in (extra_headers or {}).items():
            self.send_header(name, value)
        self.end_headers()
        self.wfile.write(payload)


def create_self_signed_certificate(directory: Path, host: str) -> tuple[Path, Path]:
    """Create a self-signed server certificate for the host (and localhost); returns certificate and key file."""
    key = ec.generate_private_key(ec.SECP256R1())
    alternative_names: list[x509.GeneralName] = [x509.DNSName("localhost")]
    try:
        alternative_names.append(x509.IPAddress(ipaddress.ip_address(host)))
    except ValueError:
        alternative_names.append(x509.DNSName(host))
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, host)])
    now = datetime.datetime.now(datetime.timezone.utc)
    certificate = (
        x509.CertificateBuilder()
        .subject_name(name)
        .issuer_name(name)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(now - datetime.timedelta(minutes=1))
        .not_valid_after(now + datetime.timedelta(days=CERTIFICATE_VALIDITY_DAYS))
        .add_extension(x509.SubjectAlternativeName(alternative_names), critical=False)
        .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
        .sign(key, hashes.SHA256())
    )
    certificate_file = directory / "skyhigh-mock.crt"
    key_file = directory / "skyhigh-mock.key"
    certificate_file.write_bytes(certificate.public_bytes(serialization.Encoding.PEM))
    key_file.write_bytes(
        key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
    )
    key_file.chmod(0o600)
    return certificate_file, key_file


@dataclass
class MockServerHandle:
    """A running mock: its server, thread and the certificate clients must trust."""

    server: KonfiguratorServer
    thread: threading.Thread
    certificate_file: Path

    @property
    def port(self) -> int:
        return cast("tuple[str, int]", self.server.server_address)[1]

    @property
    def base_url(self) -> str:
        return f"https://{cast('tuple[str, int]', self.server.server_address)[0]}:{self.port}{REST_PREFIX}"

    def stop(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()


@dataclass(frozen=True)
class MockServerConfig:
    """Where and how the mock listens, and what it serves."""

    password: str
    data_dir: Path = SAMPLE_DATA_DIR
    user: str = DEFAULT_USER
    host: str = DEFAULT_HOST
    port: int = DEFAULT_PORT
    certificate_file: Path | None = None
    key_file: Path | None = None
    certificate_dir: Path | None = None


def start_mock_server(config: MockServerConfig) -> MockServerHandle:
    """Start the mock in a background thread; port 0 picks a free port."""
    if not config.password:
        raise FwoImporterError("skyhigh mock: a password is needed")
    certificate_file, key_file = config.certificate_file, config.key_file
    if certificate_file is None or key_file is None:
        certificate_dir = config.certificate_dir or Path(tempfile.mkdtemp(prefix="skyhigh-mock-"))
        certificate_file, key_file = create_self_signed_certificate(certificate_dir, config.host)
    state = KonfiguratorState(user=config.user, password=config.password, rulesets=read_rulesets(config.data_dir))
    server = KonfiguratorServer((config.host, config.port), state)
    tls_context = ssl.create_default_context(ssl.Purpose.CLIENT_AUTH)
    tls_context.load_cert_chain(certificate_file, key_file)
    server.socket = tls_context.wrap_socket(server.socket, server_side=True)
    thread = threading.Thread(target=server.serve_forever, name="skyhigh-mock", daemon=True)
    thread.start()
    return MockServerHandle(server=server, thread=thread, certificate_file=certificate_file)


def parse_args(argv: list[str] | None = None) -> MockServerConfig:
    parser = argparse.ArgumentParser(description="Mock of the Skyhigh Web Gateway Konfigurator REST API")
    parser.add_argument("--host", default=DEFAULT_HOST, help="address to listen on (default: %(default)s)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help="port to listen on (default: %(default)s)")
    parser.add_argument("--data", type=Path, default=SAMPLE_DATA_DIR, help="directory with rule set exports")
    parser.add_argument("--user", default=DEFAULT_USER, help="login user (default: %(default)s)")
    parser.add_argument(
        "--password",
        default=os.environ.get(PASSWORD_ENV_VARIABLE, ""),
        help=f"login password (or {PASSWORD_ENV_VARIABLE})",
    )
    parser.add_argument("--cert", type=Path, help="server certificate (PEM); created if missing")
    parser.add_argument("--key", type=Path, help="private key of the server certificate (PEM)")
    args = parser.parse_args(argv)
    return MockServerConfig(
        password=args.password,
        data_dir=args.data,
        user=args.user,
        host=args.host,
        port=args.port,
        certificate_file=args.cert,
        key_file=args.key,
    )


def main(argv: list[str] | None = None, stop_event: threading.Event | None = None) -> int:
    """Run the mock until interrupted (or until stop_event is set, used by tests)."""
    handle = start_mock_server(parse_args(argv))
    FWOLogger.info(
        f"skyhigh mock: serving {len(handle.server.state.rulesets)} rule sets at {handle.base_url}, "
        f"certificate: {handle.certificate_file}"
    )
    try:
        (stop_event or threading.Event()).wait()
    except KeyboardInterrupt:
        FWOLogger.info("skyhigh mock: stopped")
    finally:
        handle.stop()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
