"""Real QUIC/HTTP integration using production outbound JSON. Loopback listeners only."""
import argparse
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import platform
import secrets
import shutil
import socket
import subprocess
import tarfile
import tempfile
import threading
import time
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
HYSTERIA_VERSION = "2.13.0"
SING_BOX_VERSION = "1.14.3"
ASSETS = {
    ("Linux", "x86_64"): (
        "hysteria-linux-amd64", "907ba8c9693edb104b20582681fb7dc15639d5b64a9cbb616a7b539190a86691",
        "sing-box-1.14.3-linux-amd64.tar.gz", "e2bdf179c15a3652955dc44867e33ca0965c9c9b91b34267dcc5a5d639a5feee"),
    ("Linux", "aarch64"): (
        "hysteria-linux-arm64", "a68a61a84452ca250ce0368202521965ca9cc9d801a404f1dc9008ac6cf677a7",
        "sing-box-1.14.3-linux-arm64.tar.gz", "29dae24d76bea3bc62d07dd3e60cfc12003de7debbd6b768df9be9c68360174e"),
    ("Windows", "amd64"): (
        "hysteria-windows-amd64.exe", "162ef8fe55dc7ec810dda662908f8e65ec36bd6da39cb87c3bd66184ab68f067",
        "sing-box-1.14.3-windows-amd64.zip", "d8b86ef58936dba9230ec6a57ffa4243933f3c14ef4809828b6c553440ed1518"),
}


def require(condition, description):
    if not condition:
        raise RuntimeError(description)


def sha256_file(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        while True:
            chunk = stream.read(1024 * 1024)
            if not chunk:
                return result.hexdigest()
            result.update(chunk)


def download(url, destination, expected):
    if destination.is_file() and sha256_file(destination) == expected:
        return destination
    partial = destination.with_name(destination.name + ".part")
    try:
        request = urllib.request.Request(url, headers={"User-Agent": "S2N-Loopback-Integration"})
        with urllib.request.urlopen(request, timeout=90) as response, partial.open("wb") as target:
            require(response.geturl().startswith("https://"), "Refusing a non-HTTPS binary download")
            shutil.copyfileobj(response, target)
        require(sha256_file(partial) == expected, "Official binary checksum mismatch: " + destination.name)
        partial.replace(destination)
    finally:
        if partial.exists():
            partial.unlink()
    return destination


def binaries(cache):
    variant = ASSETS.get((platform.system(), platform.machine().lower()))
    require(variant is not None, "Loopback integration supports Linux x64/ARM64 and Windows x64")
    hysteria_name, hysteria_hash, archive_name, archive_hash = variant
    cache.mkdir(parents=True, exist_ok=True)
    hysteria = download("https://github.com/HyNetworks/hysteria/releases/download/app/v" + HYSTERIA_VERSION + "/" + hysteria_name,
                        cache / hysteria_name, hysteria_hash)
    archive = download("https://github.com/SagerNet/sing-box/releases/download/v" + SING_BOX_VERSION + "/" + archive_name,
                       cache / archive_name, archive_hash)
    base = archive_name.removesuffix(".tar.gz").removesuffix(".zip")
    member = base + "/sing-box" + (".exe" if platform.system() == "Windows" else "")
    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as source:
            record = source.getinfo(member)
            require(record.file_size < 256 * 1024 * 1024, "Unexpected sing-box executable size")
            data = source.read(record)
    else:
        with tarfile.open(archive, "r:gz") as source:
            record = source.getmember(member)
            require(record.isfile() and record.size < 256 * 1024 * 1024, "Unexpected sing-box executable member")
            with source.extractfile(record) as stream:
                data = stream.read()
    executable = cache / (base + (".exe" if platform.system() == "Windows" else ""))
    executable.write_bytes(data)
    os.chmod(executable, 0o700)
    os.chmod(hysteria, 0o700)
    return hysteria, executable


def port(protocol):
    with socket.socket(socket.AF_INET, protocol) as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def wait_listener(process, number):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        require(process.poll() is None, "Owned test core exited before its loopback listener was ready")
        try:
            with socket.create_connection(("127.0.0.1", number), timeout=0.2):
                return
        except OSError:
            time.sleep(0.05)
    raise RuntimeError("Owned loopback listener did not become ready")


def stop(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def main():
    arguments = argparse.ArgumentParser(description=__doc__)
    arguments.add_argument("--cache", type=Path, default=ROOT / "artifacts/transport-test-cache")
    args = arguments.parse_args()
    hysteria, core = binaries(args.cache.resolve())
    project = ROOT / "tests/S2N.TransportIntegration/S2N.TransportIntegration.csproj"
    build = subprocess.run(["dotnet", "build", str(project), "-c", "Release", "--nologo"],
                           capture_output=True, text=True, timeout=120)
    require(build.returncode == 0, "Production outbound integration harness did not build:\n" + build.stdout[-4000:] + build.stderr[-1000:])
    harness = project.parent / "bin/Release/net8.0/S2N.TransportIntegration.dll"
    report = {"result": "PASS", "listener_scope": "loopback only", "tun_created": False,
              "services_modified": False, "user_profiles_read": False,
              "server_version": HYSTERIA_VERSION, "client_version": SING_BOX_VERSION, "cases": []}
    owned = []
    with tempfile.TemporaryDirectory(prefix="s2n-quic-integration-") as temporary:
        work = Path(temporary)
        cert, key = work / "server-cert.pem", work / "server-key.pem"
        other_cert, other_key = work / "other-cert.pem", work / "other-key.pem"
        for certificate_path, key_path in ((cert, key), (other_cert, other_key)):
            subprocess.run(["dotnet", str(harness), "certificate", str(certificate_path), str(key_path)],
                           check=True, capture_output=True, timeout=20)
        password = secrets.token_urlsafe(32)
        nonce = secrets.token_hex(24)

        class OriginHandler(BaseHTTPRequestHandler):
            count = 0

            def do_GET(self):
                if self.path != "/" + nonce:
                    self.send_error(404)
                    return
                OriginHandler.count += 1
                body = ("S2N_QUIC_" + nonce).encode("ascii")
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            def log_message(self, *unused):
                pass

        origin = ThreadingHTTPServer(("127.0.0.1", 0), OriginHandler)
        thread = threading.Thread(target=origin.serve_forever, daemon=True)
        thread.start()
        udp_port = port(socket.SOCK_DGRAM)
        server_config = work / "server.json"
        server_config.write_text(json.dumps({"listen": "127.0.0.1:" + str(udp_port),
            "tls": {"cert": str(cert), "key": str(key)}, "auth": {"type": "password", "password": password}}), encoding="utf-8")
        creation = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        server_log = (work / "server.log").open("wb")
        server_process = None
        try:
            server_process = subprocess.Popen([str(hysteria), "server", "--config", str(server_config)],
                                              stdout=server_log, stderr=server_log, creationflags=creation)
            owned.append(server_process)
            time.sleep(0.4)
            require(server_process.poll() is None, "Owned Hysteria server did not start")

            def run_case(label, expected, name="s2n.internal", certificate=cert, auth=None, repeated=1):
                proxy_port = port(socket.SOCK_STREAM)
                profile_path = work / (label + "-profile.json")
                outbound_path = work / (label + "-outbound.json")
                profile_path.write_text(json.dumps({"transport": "hysteria2", "serverIp": "127.0.0.1",
                    "serverPort": udp_port, "serverName": name, "password": auth if auth is not None else password,
                    "certificatePem": certificate.read_text(encoding="ascii")}), encoding="utf-8")
                subprocess.run(["dotnet", str(harness), "outbound", str(profile_path), str(outbound_path)],
                               check=True, capture_output=True, timeout=20)
                outbound = json.loads(outbound_path.read_text())
                require(outbound["tls"].get("insecure") is False, "Production outbound disabled certificate validation")
                client_config = work / (label + "-client.json")
                client_config.write_text(json.dumps({"log": {"level": "error"},
                    "inbounds": [{"type": "mixed", "tag": "fixture-in", "listen": "127.0.0.1", "listen_port": proxy_port}],
                    "outbounds": [outbound], "route": {"final": "vless-out"},
                    "dns": {"servers": [{"type": "local", "tag": "lan-dns"}]}}), encoding="utf-8")
                log_path = work / (label + ".log")
                with log_path.open("wb") as client_log:
                    process = subprocess.Popen([str(core), "run", "-c", str(client_config)],
                                               stdout=client_log, stderr=client_log, creationflags=creation)
                    owned.append(process)
                    before = OriginHandler.count
                    try:
                        wait_listener(process, proxy_port)
                        successes = 0
                        for _ in range(repeated):
                            # Explicit absolute-form proxy request: Python's localhost/no_proxy
                            # bypass logic cannot turn this into an accidental direct request.
                            connection = http.client.HTTPConnection("127.0.0.1", proxy_port, timeout=6)
                            try:
                                connection.request("GET", "http://127.0.0.1:" + str(origin.server_port) + "/" + nonce)
                                response = connection.getresponse()
                                body = response.read()
                                if response.status == 200 and body == ("S2N_QUIC_" + nonce).encode("ascii"):
                                    successes += 1
                            except (OSError, http.client.HTTPException):
                                pass
                            finally:
                                connection.close()
                        require(successes == repeated if expected else successes == 0,
                                "Unexpected authenticated QUIC result: " + label)
                        if expected:
                            require(OriginHandler.count == before + repeated, "Proxy response did not reach the owned origin")
                        else:
                            require(OriginHandler.count == before, "A rejected handshake bypassed the proxy")
                        report["cases"].append({"name": label, "requests": repeated, "successful": successes,
                                                "expected_success": expected, "result": "PASS"})
                    finally:
                        stop(process)

            run_case("production-profile-repeated-requests", True, repeated=3)
            run_case("wrong-server-name-rejected", False, name="wrong.invalid")
            run_case("untrusted-certificate-rejected", False, certificate=other_cert)
            run_case("incorrect-password-rejected", False, auth=secrets.token_urlsafe(32))
            run_case("valid-profile-after-failures", True, repeated=2)
            stop(server_process)
            run_case("offline-server-no-direct-fallback", False)
        except Exception as error:
            # Only ephemeral fixture material exists here; still keep auth out of logs.
            diagnostics = []
            for log in sorted(work.glob("*.log")):
                text = log.read_text(encoding="utf-8", errors="replace")[-3500:].replace(password, "[fixture-auth]")
                diagnostics.append(log.name + ": " + text)
            raise RuntimeError(str(error) + "\n" + "\n".join(diagnostics)) from None
        finally:
            for process in reversed(owned):
                stop(process)
            server_log.close()
            origin.shutdown()
            origin.server_close()
            thread.join(timeout=3)
        require(all(process.poll() is not None for process in owned), "Owned test process remained running")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
