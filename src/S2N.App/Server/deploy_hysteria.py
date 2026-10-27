"""S2N-owned Hysteria deployment. Does not modify SSH, Xray or foreign services."""
import base64
import hashlib
import ipaddress
import json
import os
import pathlib
import platform
import re
import secrets
import shutil
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.request

VERSION = "2.13.0"
ASSETS = {
    "x86_64": ("amd64", "907ba8c9693edb104b20582681fb7dc15639d5b64a9cbb616a7b539190a86691"),
    "aarch64": ("arm64", "a68a61a84452ca250ce0368202521965ca9cc9d801a404f1dc9008ac6cf677a7"),
}
OWNER = "S2N.Connect"
SERVICE = "s2n-hysteria.service"
DIRECTORY = "/etc/s2n-hysteria"
BINARY_DIRECTORY = "/usr/local/lib/s2n-hysteria"
BINARY = BINARY_DIRECTORY + "/hysteria"
UNIT = "/etc/systemd/system/" + SERVICE
SERVER_NAME = "s2n.internal"


class DeploymentError(Exception):
    pass


class System:
    """Injectable command/filesystem boundary for isolated regression checks."""
    def __init__(self, root=None):
        self.root = pathlib.Path(root) if root is not None else pathlib.Path("/")

    def path(self, absolute):
        relative = pathlib.PurePosixPath(absolute)
        if not relative.is_absolute() or ".." in relative.parts:
            raise DeploymentError("S2N_OWNERSHIP_CONFLICT")
        return self.root.joinpath(*relative.parts[1:])

    def run(self, arguments, timeout=35):
        return subprocess.run(arguments, capture_output=True, text=True, timeout=timeout)

    def download(self, url, destination):
        request = urllib.request.Request(url, headers={"User-Agent": "S2N-Connect-Deployment"})
        with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as target:
            if not response.geturl().startswith("https://"):
                raise DeploymentError("S2N_DOWNLOAD_FAILED")
            shutil.copyfileobj(response, target)

    def architecture(self):
        return platform.machine().lower()

    def sleep(self, seconds):
        time.sleep(seconds)


def require(condition, code):
    if not condition:
        raise DeploymentError(code)


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as source:
        while True:
            chunk = source.read(1024 * 1024)
            if not chunk:
                break
            result.update(chunk)
    return result.hexdigest()


def ensure_safe(system, absolute):
    """Never follow a symlink into a path that belongs to somebody else."""
    path = system.path(absolute)
    current = path
    while current != system.root:
        require(not current.is_symlink(), "S2N_OWNERSHIP_CONFLICT")
        current = current.parent
    return path


def valid_server(host):
    if not isinstance(host, str) or not host or len(host) > 253:
        return False
    try:
        return not ipaddress.ip_address(host).is_unspecified
    except ValueError:
        return all(re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?", label)
                   for label in host.rstrip(".").split("."))


def active(system):
    return system.run(["systemctl", "is-active", "--quiet", SERVICE]).returncode == 0


def enabled(system):
    return system.run(["systemctl", "is-enabled", "--quiet", SERVICE]).returncode == 0


def port_users(system):
    result = system.run(["ss", "-H", "-lunp", "sport = :443"])
    require(result.returncode == 0, "S2N_DEPLOY_FAILED")
    return result.stdout.strip()


def port_is_owned(system, listeners):
    if not listeners:
        return True
    pid = system.run(["systemctl", "show", SERVICE, "--property=MainPID", "--value"]).stdout.strip()
    if not pid.isdigit() or pid == "0":
        return False
    lines = [line for line in listeners.splitlines() if line.strip()]
    return all(re.search(r"\bpid=" + re.escape(pid) + r"(?:,|\))", line) for line in lines)


def unit_text():
    return """[Unit]
Description=S2N Connect Hysteria UDP VPN
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/usr/local/lib/s2n-hysteria/hysteria server --config /etc/s2n-hysteria/config.json
User=root
Group=root
Restart=on-failure
RestartSec=3
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
AmbientCapabilities=CAP_NET_BIND_SERVICE
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
UMask=0077

[Install]
WantedBy=multi-user.target
"""


def write_private(path, value, mode=0o600):
    data = value.encode("utf-8") if isinstance(value, str) else value
    created = False
    try:
        with path.open("xb") as target:
            created = True
            os.chmod(path, mode)
            target.write(data)
    except Exception:
        if created and path.is_file() and not path.is_symlink():
            path.unlink()
        raise


def load_owned(system, server):
    """Validate recorded ownership and return credentials without rewriting them."""
    directory = ensure_safe(system, DIRECTORY)
    manifest_path = ensure_safe(system, DIRECTORY + "/owner.json")
    if not manifest_path.exists():
        require(not directory.exists() or not any(directory.iterdir()), "S2N_OWNERSHIP_CONFLICT")
        require(not ensure_safe(system, UNIT).exists(), "S2N_OWNERSHIP_CONFLICT")
        binary_directory = ensure_safe(system, BINARY_DIRECTORY)
        require(not binary_directory.exists() or not any(binary_directory.iterdir()), "S2N_OWNERSHIP_CONFLICT")
        return None
    try:
        owner = json.loads(manifest_path.read_text(encoding="utf-8"))
        require(owner.get("owner") == OWNER and owner.get("schemaVersion") == 1,
                "S2N_OWNERSHIP_CONFLICT")
        required = [BINARY, UNIT] + [DIRECTORY + "/" + name for name in
                                  ("config.json", "profile.json", "cert.pem", "key.pem")]
        require(set(owner.get("files", {})) == set(required), "S2N_OWNERSHIP_CONFLICT")
        for absolute in required:
            path = ensure_safe(system, absolute)
            require(path.is_file() and digest(path) == owner["files"][absolute], "S2N_OWNERSHIP_CONFLICT")
        profile = json.loads(system.path(DIRECTORY + "/profile.json").read_text(encoding="utf-8"))
        require(profile.get("transport") == "hysteria2" and profile.get("serverPort") == 443
                and profile.get("serverName") == SERVER_NAME and profile.get("password"), "S2N_PROFILE_INVALID")
        require(profile.get("serverIp") == server, "S2N_PROFILE_MISMATCH")
        require(profile.get("certificatePem") == system.path(DIRECTORY + "/cert.pem").read_text(encoding="ascii"),
                "S2N_PROFILE_INVALID")
        ssl.PEM_cert_to_DER_cert(profile["certificatePem"])
        certificate = system.run(["openssl", "x509", "-checkend", "86400", "-noout", "-in",
                                  str(system.path(DIRECTORY + "/cert.pem"))])
        require(certificate.returncode == 0, "S2N_TLS_FAILURE")
        return profile
    except DeploymentError:
        raise
    except (ValueError, OSError):
        raise DeploymentError("S2N_OWNERSHIP_CONFLICT") from None


def require_running(system):
    # Type=simple becomes active before the Go process has finished binding UDP.
    for _ in range(50):
        if active(system):
            listeners = port_users(system)
            if listeners and port_is_owned(system, listeners):
                return
        system.sleep(0.2)
    raise DeploymentError("S2N_START_FAILED")


def deploy(system, server):
    require(valid_server(server), "S2N_PROFILE_INVALID")
    asset = ASSETS.get(system.architecture())
    require(asset is not None, "S2N_ARCH_UNSUPPORTED")
    existing = load_owned(system, server)
    listeners = port_users(system)
    require(not listeners or (existing is not None and port_is_owned(system, listeners)), "S2N_UDP_CONFLICT")
    if existing is not None:
        # Keep a healthy service and all existing accounts/certificates unchanged.
        if not enabled(system):
            require(system.run(["systemctl", "enable", SERVICE]).returncode == 0, "S2N_START_FAILED")
        if not active(system):
            require(system.run(["systemctl", "start", SERVICE]).returncode == 0, "S2N_START_FAILED")
        require_running(system)
        return existing

    architecture, expected = asset
    with tempfile.TemporaryDirectory(prefix="s2n-hysteria-staging-") as temporary:
        staging = pathlib.Path(temporary)
        binary = staging / "hysteria"
        try:
            system.download("https://github.com/HyNetworks/hysteria/releases/download/app/v" + VERSION
                            + "/hysteria-linux-" + architecture, binary)
        except (OSError, ValueError):
            raise DeploymentError("S2N_DOWNLOAD_FAILED") from None
        require(digest(binary) == expected, "S2N_HASH_MISMATCH")
        os.chmod(binary, 0o700)
        cert, key = staging / "cert.pem", staging / "key.pem"
        result = system.run(["openssl", "req", "-x509", "-newkey", "ec", "-pkeyopt", "ec_paramgen_curve:P-256",
                             "-days", "3650", "-nodes", "-keyout", str(key), "-out", str(cert),
                             "-subj", "/CN=" + SERVER_NAME, "-addext", "subjectAltName=DNS:" + SERVER_NAME,
                             "-addext", "basicConstraints=critical,CA:FALSE"])
        require(result.returncode == 0 and cert.is_file() and key.is_file(), "S2N_TLS_FAILURE")
        certificate = cert.read_text(encoding="ascii")
        try:
            ssl.PEM_cert_to_DER_cert(certificate)
        except ValueError:
            raise DeploymentError("S2N_TLS_FAILURE") from None
        require(system.run(["openssl", "verify", "-CAfile", str(cert), "-verify_hostname", SERVER_NAME,
                            str(cert)]).returncode == 0, "S2N_TLS_FAILURE")
        password = secrets.token_urlsafe(32)
        profile = {"transport": "hysteria2", "serverIp": server, "serverPort": 443,
                   "serverName": SERVER_NAME, "password": password, "certificatePem": certificate}
        configuration = {"listen": ":443", "tls": {"cert": DIRECTORY + "/cert.pem", "key": DIRECTORY + "/key.pem"},
                         "auth": {"type": "password", "password": password},
                         "masquerade": {"type": "string", "string": {"content": "Not found", "statusCode": 404}},
                         "sniff": {"enable": False}}
        contents = {BINARY: binary.read_bytes(), UNIT: unit_text(),
                    DIRECTORY + "/cert.pem": certificate,
                    DIRECTORY + "/key.pem": key.read_bytes(),
                    DIRECTORY + "/profile.json": json.dumps(profile, indent=2) + "\n",
                    DIRECTORY + "/config.json": json.dumps(configuration, indent=2) + "\n"}
        created_files, created_directories = [], []
        attempted_start = attempted_enable = False
        try:
            for absolute in (DIRECTORY, BINARY_DIRECTORY):
                path = ensure_safe(system, absolute)
                if not path.exists():
                    path.mkdir(parents=True, mode=0o700)
                    created_directories.append(path)
            for absolute, value in contents.items():
                path = ensure_safe(system, absolute)
                path.parent.mkdir(parents=True, exist_ok=True)
                write_private(path, value, 0o700 if absolute == BINARY else 0o644 if absolute == UNIT else 0o600)
                created_files.append(path)
            owner = {"owner": OWNER, "schemaVersion": 1, "version": VERSION,
                     "files": {absolute: digest(system.path(absolute)) for absolute in contents}}
            manifest_path = ensure_safe(system, DIRECTORY + "/owner.json")
            write_private(manifest_path, json.dumps(owner, indent=2) + "\n")
            created_files.append(manifest_path)
            require(system.run(["systemctl", "daemon-reload"]).returncode == 0, "S2N_START_FAILED")
            attempted_enable = True
            require(system.run(["systemctl", "enable", SERVICE]).returncode == 0, "S2N_START_FAILED")
            attempted_start = True
            require(system.run(["systemctl", "start", SERVICE]).returncode == 0, "S2N_START_FAILED")
            require_running(system)
            return profile
        except Exception:
            # This branch only owns a fresh installation. Never stop/disable a foreign service.
            if attempted_start:
                system.run(["systemctl", "stop", SERVICE])
            if attempted_enable:
                system.run(["systemctl", "disable", SERVICE])
            for path in reversed(created_files):
                if path.is_file() and not path.is_symlink():
                    path.unlink()
            for path in reversed(created_directories):
                try:
                    path.rmdir()
                except OSError:
                    pass
            system.run(["systemctl", "daemon-reload"])
            raise


def main():
    require(os.geteuid() == 0, "S2N_ROOT_REQUIRED")
    release = pathlib.Path("/etc/os-release").read_text(encoding="utf-8")
    require(bool(re.search(r'^ID=["\']?(?:ubuntu|debian)["\']?$', release, re.M)), "S2N_OS_UNSUPPORTED")
    request = json.loads(base64.b64decode(sys.argv[1], validate=True))
    profile = deploy(System(), request["server"])
    print("S2N_PROFILE_JSON=" + json.dumps(profile, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except DeploymentError as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
    except Exception:
        # Do not print commands, profile JSON, passwords or private certificate material.
        print("S2N_DEPLOY_FAILED", file=sys.stderr)
        raise SystemExit(1)
