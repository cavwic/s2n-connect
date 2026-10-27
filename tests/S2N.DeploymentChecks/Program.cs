using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Renci.SshNet;
using S2N.Models;
using S2N.Services;

if (args.Length == 2 && args[0] == "--write-legacy-script")
{
    File.WriteAllText(args[1], VpsDeploymentService.CreateDeploymentScript("vless", "203.0.113.20"));
    return;
}

var passed = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAIL: " + label);
    passed++;
}

void Reject(Action action, string label, string? secret = null)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException)
    {
        Check(secret is null || !error.Message.Contains(secret, StringComparison.Ordinal), label + " hides credentials");
        return;
    }
    throw new InvalidOperationException("FAIL: " + label);
}

using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var request = new CertificateRequest("CN=s2n.internal", key, HashAlgorithmName.SHA256);
var names = new SubjectAlternativeNameBuilder();
names.AddDnsName("s2n.internal");
request.CertificateExtensions.Add(names.Build());
using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1));
var fixturePassword = "synthetic-password-do-not-print";
var profile = new VpsProfile
{
    Transport = "hysteria2", ServerIp = "203.0.113.20", ServerPort = 443,
    Password = fixturePassword, CertificatePem = certificate.ExportCertificatePem(), ServerName = "s2n.internal"
};
var serialized = JsonSerializer.Serialize(profile);
var parsed = VpsDeploymentService.ParseDeploymentProfile(serialized, "hysteria2", "203.0.113.20");
Check(parsed.Password == fixturePassword && parsed.CertificatePem.Trim() == profile.CertificatePem.Trim(),
    "valid fingerprint-delivered UDP profile retains authentication and certificate");
Reject(() => VpsDeploymentService.ParseDeploymentProfile(serialized, "hysteria2", "203.0.113.21"),
    "wrong target cannot overwrite previous profile", fixturePassword);
Reject(() => VpsDeploymentService.ParseDeploymentProfile(serialized, "vless", "203.0.113.20"),
    "unexpected transport rejected", fixturePassword);
profile.CertificatePem = "invalid certificate";
Reject(() => VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), "hysteria2", profile.ServerIp),
    "invalid trust material rejected", fixturePassword);
profile.CertificatePem = certificate.ExportCertificatePem();
profile.ServerPort = 0;
Reject(() => VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), "hysteria2", profile.ServerIp),
    "invalid UDP port rejected", fixturePassword);
profile.ServerPort = 443;
profile.ServerName = "incorrect.invalid";
Reject(() => VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), "hysteria2", profile.ServerIp),
    "unexpected TLS name rejected", fixturePassword);
Reject(() => VpsDeploymentService.ParseDeploymentProfile("{\"password\":\"" + fixturePassword + "\",bad}", "hysteria2", "example.com"),
    "malformed profile hides raw JSON", fixturePassword);
using var expired = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(-1));
profile.ServerName = "s2n.internal";
profile.CertificatePem = expired.ExportCertificatePem();
Reject(() => VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), "hysteria2", profile.ServerIp),
    "expired certificate rejected", fixturePassword);
var legacy = """{"serverIp":"203.0.113.10","serverPort":443,"uuid":"00000000-0000-4000-8000-000000000001","publicKey":"AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA","serverName":"example.com"}""";
Check(VpsDeploymentService.ParseDeploymentProfile(legacy, "vless", "203.0.113.10").Transport == "vless",
    "legacy profiles without a transport still use VLESS");
Reject(() => VpsDeploymentService.CreateDeploymentScript("hysteria2", "example.com; id"),
    "remote target cannot inject shell syntax");
Reject(() => VpsDeploymentService.CreateDeploymentScript("unknown", "example.com"),
    "unknown server transport rejected");
var udpScript = VpsDeploymentService.CreateDeploymentScript("hysteria2", "vps.example.com");
Check(udpScript.Contains("python3 - '") && udpScript.Contains("S2N_HYSTERIA_PY"),
    "server resource is embedded and expands without external file dependencies");
Check(!udpScript.Contains('\r'), "Linux deployment scripts use LF on Windows builds as well");
Check(!udpScript.Contains("vps.example.com", StringComparison.Ordinal)
    && udpScript.Contains("907ba8c9693edb104b20582681fb7dc15639d5b64a9cbb616a7b539190a86691")
    && udpScript.Contains("a68a61a84452ca250ce0368202521965ca9cc9d801a404f1dc9008ac6cf677a7"),
    "target encoded and both server architectures have pinned official hashes");
var legacyScript = VpsDeploymentService.CreateDeploymentScript("vless", "203.0.113.10");
Check(legacyScript.Contains("if ! systemctl is-active --quiet xray; then systemctl start xray; fi"),
    "redeploy starts an existing stopped Xray without rotating credentials");
Check(legacyScript.IndexOf("echo S2N_OWNERSHIP_CONFLICT", StringComparison.Ordinal)
    < legacyScript.IndexOf("apt-get update", StringComparison.Ordinal),
    "legacy ownership is checked before package or service mutations");

var mutable = new AppSettings
{
    VpsHost = "203.0.113.20", VpsPort = 22, VpsUser = "root", SshKeyPath = "fixture-key",
    SshProxyPort = 2080, PreferredTransport = "hysteria2", VpsProfilePath = "fixture-profile.json",
    KnownHostFingerprints = new() { ["203.0.113.20:22"] = "fixture-original-fingerprint" }
};
var snapshot = VpsDeploymentService.CaptureDeploymentSettings(mutable);
mutable.VpsHost = "203.0.113.21"; mutable.PreferredTransport = "vless"; mutable.VpsProfilePath = "other-profile.json";
mutable.SshKeyPath = "other-key"; mutable.SshProxyPort = 0;
mutable.KnownHostFingerprints["203.0.113.20:22"] = "changed-fingerprint";
Check(snapshot.VpsHost == "203.0.113.20" && snapshot.PreferredTransport == "hysteria2"
    && snapshot.VpsProfilePath == "fixture-profile.json" && snapshot.SshKeyPath == "fixture-key"
    && snapshot.SshProxyPort == 2080 && snapshot.KnownHostFingerprints["203.0.113.20:22"] == "fixture-original-fingerprint",
    "deployment freezes target, transport, key, proxy, output path and host pin before awaiting SSH");
profile.ServerName = "s2n.internal"; profile.CertificatePem = certificate.ExportCertificatePem();
Check(VpsDeploymentService.ParseDeploymentProfile(JsonSerializer.Serialize(profile), snapshot.PreferredTransport, snapshot.VpsHost).ServerIp == snapshot.VpsHost,
    "in-flight UI changes cannot redirect the parsed deployment result");
foreach (var code in new[] { "S2N_UDP_CONFLICT", "S2N_OWNERSHIP_CONFLICT", "S2N_PROFILE_MISMATCH",
    "S2N_HASH_MISMATCH", "S2N_DOWNLOAD_FAILED", "S2N_TLS_FAILURE", "S2N_START_FAILED",
    "S2N_ARCH_UNSUPPORTED", "S2N_OS_UNSUPPORTED", "S2N_ROOT_REQUIRED", "S2N_PROFILE_INVALID", "S2N_DEPLOY_FAILED" })
{
    var message = VpsDeploymentService.DescribeDeploymentError(code + " " + fixturePassword);
    Check(!string.IsNullOrWhiteSpace(message) && !message.Contains(fixturePassword, StringComparison.Ordinal),
        code + " has a safe localized error");
}
Check(VpsDeploymentService.DescribeDeploymentError("other SSH failure") is null, "unrecognized SSH failure not mislabelled");

using var auth = new NoneAuthenticationMethod("fixture-user");
var settings = new AppSettings { VpsHost = "203.0.113.20", VpsPort = 22, VpsUser = "fixture-user" };
var direct = SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5));
Check(direct.ProxyType == ProxyTypes.None && direct.Host == settings.VpsHost && direct.Port == 22,
    "SSH is direct unless a local proxy was explicitly selected");
settings.SshProxyPort = 2080;
var proxy = SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5));
Check(proxy.ProxyType == ProxyTypes.Http && proxy.ProxyHost == "127.0.0.1" && proxy.ProxyPort == 2080
    && proxy.Host == settings.VpsHost && proxy.Port == 22,
    "HTTP CONNECT stays loopback and preserves the original SSH target");
foreach (var invalid in new[] { -1, 80, 65536 })
{
    settings.SshProxyPort = invalid;
    Reject(() => SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5)), "invalid local SSH proxy port rejected");
}
Console.WriteLine($"PASS: {passed} deployment/profile/SSH checks. No remote connections or user settings were used.");
