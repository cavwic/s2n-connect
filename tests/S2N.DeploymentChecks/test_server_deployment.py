"""Run deployment behavior in a temporary filesystem with a fake service manager."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / "src/S2N.App/Server/deploy_hysteria.py"
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("s2n_server", SOURCE)
server = importlib.util.module_from_spec(spec)
spec.loader.exec_module(server)
FIXTURE_CERT = Path(__file__).with_name("fixture-certificate.txt").read_text(encoding="ascii")
BINARY = b"synthetic Hysteria binary; never executed"
BINARY_HASH = hashlib.sha256(BINARY).hexdigest()
TARGET = "203.0.113.20"


class FixtureSystem(server.System):
    def __init__(self, root):
        super().__init__(root)
        self.commands = []
        self.downloads = []
        self.running = False
        self.enabled = False
        self.foreign_listener = False
        self.fail_start = False
        self.fail_reload = False
        self.bad_digest = False
        self.bad_certificate = False
        self.download_failure = False
        self.readiness_delay = 0
        self.sleeps = 0
        self.machine = "x86_64"

    def architecture(self):
        return self.machine

    def download(self, url, destination):
        self.downloads.append(url)
        if self.download_failure:
            raise OSError("fixture network unavailable")
        destination.write_bytes(b"corrupted download" if self.bad_digest else BINARY)

    def sleep(self, seconds):
        self.sleeps += 1

    def run(self, arguments, timeout=35):
        self.commands.append(tuple(arguments))
        output, code = "", 0
        if arguments[0] == "openssl":
            if arguments[1] == "req":
                cert = Path(arguments[arguments.index("-out") + 1])
                key = Path(arguments[arguments.index("-keyout") + 1])
                if self.bad_certificate:
                    cert.write_text("not a certificate", encoding="ascii")
                    key.write_text("not a real private key", encoding="ascii")
                elif shutil.which("openssl"):
                    # Linux CI uses real key generation and certificate/name checks.
                    return subprocess.run(arguments, capture_output=True, text=True, timeout=timeout)
                else:
                    cert.write_text(FIXTURE_CERT, encoding="ascii", newline="\n")
                    key.write_text("not a real private key", encoding="ascii")
            elif shutil.which("openssl"):
                return subprocess.run(arguments, capture_output=True, text=True, timeout=timeout)
        elif arguments[0] == "ss":
            if self.foreign_listener:
                output = 'UNCONN 0 0 0.0.0.0:443 0.0.0.0:* users:(("foreign",pid=900,fd=3))\n'
            elif self.running:
                if self.readiness_delay > 0:
                    self.readiness_delay -= 1
                else:
                    output = 'UNCONN 0 0 *:443 *:* users:(("hysteria",pid=500,fd=3))\n'
        elif arguments[0] == "systemctl":
            action = arguments[1]
            if action == "is-active":
                code = 0 if self.running else 3
            elif action == "is-enabled":
                code = 0 if self.enabled else 1
            elif action == "show":
                output = "500\n" if self.running else "0\n"
            elif action == "enable":
                self.enabled = True
            elif action == "disable":
                self.enabled = False
            elif action == "start":
                if self.fail_start:
                    code = 1
                else:
                    self.running = True
            elif action == "stop":
                self.running = False
            elif action == "daemon-reload":
                code = 1 if self.fail_reload else 0
            else:
                raise AssertionError("Unexpected mutating service command")
        else:
            raise AssertionError("Unexpected command: " + str(arguments[0]))
        return subprocess.CompletedProcess(arguments, code, output, "")


class DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="s2n-deploy-fixture-")
        self.system = FixtureSystem(self.temporary.name)
        self.original_assets = dict(server.ASSETS)
        self.assets = patch.dict(server.ASSETS, {"x86_64": ("amd64", BINARY_HASH), "aarch64": ("arm64", BINARY_HASH)}, clear=True)
        self.assets.start()
        self.sentinels = {
            "/etc/ssh/sshd_config": b"original SSH options\n",
            "/usr/local/etc/xray/config.json": b'{"clients":["existing-account"]}',
            "/root/s2n-profile.json": b"original TCP account\n",
        }
        for path, value in self.sentinels.items():
            absolute = self.system.path(path)
            absolute.parent.mkdir(parents=True, exist_ok=True)
            absolute.write_bytes(value)

    def tearDown(self):
        for path, expected in self.sentinels.items():
            self.assertEqual(self.system.path(path).read_bytes(), expected, "foreign SSH/Xray file changed")
        for arguments in self.system.commands:
            if arguments[0] == "systemctl" and arguments[1] in ("start", "stop", "enable", "disable", "restart"):
                self.assertEqual(arguments[2], server.SERVICE, "foreign service touched")
        self.assets.stop()
        self.temporary.cleanup()

    def deploy(self):
        return server.deploy(self.system, TARGET)

    def owned_bytes(self):
        owner = json.loads(self.system.path(server.DIRECTORY + "/owner.json").read_text())
        return {path: self.system.path(path).read_bytes() for path in owner["files"]}

    def assert_no_install(self):
        self.assertFalse(self.system.path(server.UNIT).exists())
        self.assertFalse(self.system.path(server.BINARY).exists())
        self.assertFalse(self.system.path(server.DIRECTORY + "/profile.json").exists())
        self.assertFalse(any(args[0] == "systemctl" and args[1] in ("enable", "start", "stop", "disable") for args in self.system.commands))

    def test_fresh_install_has_private_auth_verified_certificate_and_own_unit(self):
        profile = self.deploy()
        self.assertEqual((profile["transport"], profile["serverPort"]), ("hysteria2", 443))
        self.assertGreaterEqual(len(profile["password"]), 40)
        self.assertEqual(profile["certificatePem"], self.system.path(server.DIRECTORY + "/cert.pem").read_text())
        configuration = json.loads(self.system.path(server.DIRECTORY + "/config.json").read_text())
        self.assertEqual(configuration["auth"]["password"], profile["password"])
        self.assertNotIn("insecure", configuration["tls"])
        self.assertEqual(configuration["listen"], ":443")
        self.assertTrue(self.system.running and self.system.enabled)
        self.assertIn("app/v2.13.0/hysteria-linux-amd64", self.system.downloads[0])
        self.assertTrue(any(args[:2] == ("openssl", "verify") and "-verify_hostname" in args for args in self.system.commands))
        if os.name != "nt":
            self.assertEqual(self.system.path(server.DIRECTORY + "/key.pem").stat().st_mode & 0o777, 0o600)
            self.assertEqual(self.system.path(server.DIRECTORY + "/profile.json").stat().st_mode & 0o777, 0o600)

    def test_healthy_redeploy_preserves_credentials_files_and_active_process(self):
        original = self.deploy()
        original_files = self.owned_bytes()
        self.system.commands.clear()
        restored = self.deploy()
        self.assertEqual(restored, original)
        self.assertEqual(self.owned_bytes(), original_files)
        self.assertEqual(len(self.system.downloads), 1)
        self.assertFalse(any(args[0] == "systemctl" and args[1] in ("start", "stop", "restart", "enable") for args in self.system.commands))

    def test_stopped_owned_service_recovers_without_new_account_or_certificate(self):
        original = self.deploy()
        original_files = self.owned_bytes()
        self.system.running = self.system.enabled = False
        self.system.commands.clear()
        self.assertEqual(self.deploy(), original)
        self.assertTrue(self.system.running and self.system.enabled)
        self.assertEqual(self.owned_bytes(), original_files)
        self.assertIn(("systemctl", "start", server.SERVICE), self.system.commands)
        self.assertEqual(len(self.system.downloads), 1)

    def test_foreign_udp_listener_fails_before_install_or_download(self):
        self.system.foreign_listener = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_UDP_CONFLICT"):
            self.deploy()
        self.assert_no_install()
        self.assertEqual(self.system.downloads, [])

    def test_foreign_owned_path_is_never_replaced(self):
        directory = self.system.path(server.DIRECTORY)
        directory.mkdir(parents=True)
        foreign = directory / "existing-config.json"
        foreign.write_text("keep this file")
        with self.assertRaisesRegex(server.DeploymentError, "S2N_OWNERSHIP_CONFLICT"):
            self.deploy()
        self.assertEqual(foreign.read_text(), "keep this file")
        self.assert_no_install()

    def test_foreign_service_unit_is_never_replaced(self):
        path = self.system.path(server.UNIT)
        path.parent.mkdir(parents=True)
        path.write_text("foreign-unit")
        with self.assertRaisesRegex(server.DeploymentError, "S2N_OWNERSHIP_CONFLICT"):
            self.deploy()
        self.assertEqual(path.read_text(), "foreign-unit")
        self.assertEqual(self.system.downloads, [])

    def test_modified_owned_configuration_fails_without_restart(self):
        self.deploy()
        path = self.system.path(server.DIRECTORY + "/config.json")
        value = path.read_bytes() + b"\n"
        path.write_bytes(value)
        self.system.commands.clear()
        with self.assertRaisesRegex(server.DeploymentError, "S2N_OWNERSHIP_CONFLICT"):
            self.deploy()
        self.assertEqual(path.read_bytes(), value)
        self.assertEqual(self.system.commands, [])

    def test_different_target_does_not_rotate_existing_profile(self):
        self.deploy()
        original_files = self.owned_bytes()
        with self.assertRaisesRegex(server.DeploymentError, "S2N_PROFILE_MISMATCH"):
            server.deploy(self.system, "other.example.com")
        self.assertEqual(self.owned_bytes(), original_files)

    def test_checksum_mismatch_never_executes_or_installs_download(self):
        self.system.bad_digest = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_HASH_MISMATCH"):
            self.deploy()
        self.assert_no_install()
        self.assertFalse(any(args[0] == "openssl" for args in self.system.commands))

    def test_download_failure_does_not_create_managed_files(self):
        self.system.download_failure = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_DOWNLOAD_FAILED"):
            self.deploy()
        self.assert_no_install()

    def test_bad_certificate_fails_before_managed_files(self):
        self.system.bad_certificate = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_TLS_FAILURE"):
            self.deploy()
        self.assert_no_install()

    def test_service_start_failure_rolls_back_only_fresh_owned_install(self):
        self.system.fail_start = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_START_FAILED"):
            self.deploy()
        self.assertFalse(self.system.path(server.UNIT).exists())
        self.assertFalse(self.system.path(server.BINARY).exists())
        self.assertFalse(self.system.path(server.DIRECTORY + "/profile.json").exists())
        self.assertIn(("systemctl", "stop", server.SERVICE), self.system.commands)
        self.assertIn(("systemctl", "disable", server.SERVICE), self.system.commands)
        self.assertFalse(self.system.running or self.system.enabled)

    def test_daemon_reload_failure_rolls_back_without_starting(self):
        self.system.fail_reload = True
        with self.assertRaisesRegex(server.DeploymentError, "S2N_START_FAILED"):
            self.deploy()
        self.assertFalse(self.system.path(server.UNIT).exists())
        self.assertFalse(self.system.path(server.BINARY).exists())
        self.assertFalse(any(args[:2] == ("systemctl", "start") for args in self.system.commands))

    def test_waits_for_real_socket_after_simple_service_reports_active(self):
        self.system.readiness_delay = 3
        self.deploy()
        self.assertGreaterEqual(self.system.sleeps, 3)

    def test_nonowned_pid_cannot_be_reported_as_running_owned_service(self):
        self.deploy()
        self.system.foreign_listener = True
        original_files = self.owned_bytes()
        with self.assertRaisesRegex(server.DeploymentError, "S2N_UDP_CONFLICT"):
            self.deploy()
        self.assertEqual(self.owned_bytes(), original_files)

    def test_unrecognized_architecture_fails_without_changes(self):
        self.system.machine = "riscv64"
        with self.assertRaisesRegex(server.DeploymentError, "S2N_ARCH_UNSUPPORTED"):
            self.deploy()
        self.assert_no_install()

    def test_arm64_uses_separate_pinned_official_asset(self):
        self.system.machine = "aarch64"
        self.deploy()
        self.assertTrue(self.system.downloads[0].endswith("hysteria-linux-arm64"))
        self.assertNotEqual(self.original_assets["x86_64"][1], self.original_assets["aarch64"][1])

    def test_invalid_host_cannot_reach_files_or_commands(self):
        for value in ("example.com;id", "", "0.0.0.0", "a/b", "$(touch /tmp/test)"):
            with self.assertRaisesRegex(server.DeploymentError, "S2N_PROFILE_INVALID"):
                server.deploy(self.system, value)
        self.assertEqual(self.system.commands, [])
        self.assert_no_install()

    def test_symlink_root_cannot_redirect_install(self):
        elsewhere = self.system.path("/elsewhere")
        elsewhere.mkdir()
        directory = self.system.path(server.DIRECTORY)
        directory.parent.mkdir(parents=True, exist_ok=True)
        try:
            directory.symlink_to(elsewhere, target_is_directory=True)
        except OSError:
            self.skipTest("symlink creation is not permitted on this Windows runner")
        with self.assertRaisesRegex(server.DeploymentError, "S2N_OWNERSHIP_CONFLICT"):
            self.deploy()
        self.assertEqual(list(elsewhere.iterdir()), [])


class LegacyPreservationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.harness = Path(__file__).with_name("bin") / "Release/net8.0/S2N.DeploymentChecks.dll"
        if os.name == "nt" or not shutil.which("bash") or not shutil.which("dotnet") or not cls.harness.is_file():
            raise unittest.SkipTest("legacy shell guards run on Linux after the C# deployment checks build")

    def check_conflict(self, config_exists, profile_exists, occupied):
        with tempfile.TemporaryDirectory(prefix="s2n-legacy-preservation-") as temporary:
            work = Path(temporary)
            script_path = work / "legacy.sh"
            subprocess.run(["dotnet", str(self.harness), "--write-legacy-script", str(script_path)], check=True,
                           capture_output=True, timeout=20)
            config_dir = work / "xray"
            config_dir.mkdir()
            config, profile = config_dir / "config.json", work / "profile.json"
            expected = {}
            if config_exists:
                config.write_text("existing foreign Xray account", encoding="utf-8")
                expected[config] = config.read_bytes()
            if profile_exists:
                profile.write_text("original S2N account", encoding="utf-8")
                expected[profile] = profile.read_bytes()
            release = work / "os-release"
            release.write_text("ID=ubuntu\n", encoding="ascii")
            mutation = work / "unexpected-mutation"
            script = script_path.read_text().replace("/etc/os-release", str(release))
            script = script.replace("/usr/local/etc/xray", str(config_dir)).replace("/root/s2n-profile.json", str(profile))
            prefix = "id() { printf '0\\n'; }\n"
            prefix += "ss() { " + ("printf 'LISTEN fixture TCP443\\n';" if occupied else ":;") + " }\n"
            prefix += "apt-get() { printf mutation > '" + str(mutation) + "'; return 77; }\n"
            result = subprocess.run(["bash", "-s"], input=prefix + script, text=True, capture_output=True, timeout=10)
            self.assertEqual(result.returncode, 22, "legacy conflict did not fail at the ownership guard")
            self.assertIn("S2N_OWNERSHIP_CONFLICT", result.stderr)
            self.assertFalse(mutation.exists(), "legacy conflict reached a mutating installation step")
            for path, value in expected.items():
                self.assertEqual(path.read_bytes(), value)

    def test_foreign_xray_without_s2n_profile_is_preserved(self):
        self.check_conflict(True, False, False)

    def test_s2n_account_without_configuration_is_not_rotated(self):
        self.check_conflict(False, True, False)

    def test_occupied_fresh_tcp_port_is_preserved_even_if_owner_is_xray(self):
        self.check_conflict(False, False, True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
