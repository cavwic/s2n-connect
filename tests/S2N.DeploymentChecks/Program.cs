using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;
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
    VpsHost = "203.0.113.20", VpsPort = 2222, VpsUser = "root", SshKeyPath = "fixture-key",
    SshProxyPort = 2080, PreferredTransport = "hysteria2", VpsProfilePath = "fixture-profile.json",
    KnownHostFingerprints = new() { ["203.0.113.20:2222"] = "fixture-original-fingerprint" }
};
var snapshot = VpsDeploymentService.CaptureDeploymentSettings(mutable);
mutable.VpsHost = "203.0.113.21"; mutable.PreferredTransport = "vless"; mutable.VpsProfilePath = "other-profile.json";
mutable.SshKeyPath = "other-key"; mutable.SshProxyPort = 0; mutable.VpsPort = 22;
mutable.KnownHostFingerprints["203.0.113.20:2222"] = "changed-fingerprint";
Check(snapshot.VpsHost == "203.0.113.20" && snapshot.PreferredTransport == "hysteria2"
    && snapshot.VpsProfilePath == "fixture-profile.json" && snapshot.SshKeyPath == "fixture-key"
    && snapshot.VpsPort == 2222 && snapshot.SshProxyPort == 2080
    && snapshot.KnownHostFingerprints["203.0.113.20:2222"] == "fixture-original-fingerprint",
    "deployment freezes target, SSH port, transport, key, proxy, output path and host pin before awaiting SSH");
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
Check(new AppSettings().VpsPort == 22, "the standard SSH default is unchanged");
var settings = new AppSettings { VpsHost = "203.0.113.20", VpsPort = 2222, VpsUser = "fixture-user" };
var direct = SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5));
Check(direct.ProxyType == ProxyTypes.None && direct.Host == settings.VpsHost && direct.Port == 2222,
    "direct SSH preserves a configured nonstandard remote port");
settings.SshProxyPort = 2080;
var proxy = SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5));
Check(proxy.ProxyType == ProxyTypes.Http && proxy.ProxyHost == "127.0.0.1" && proxy.ProxyPort == 2080
    && proxy.Host == settings.VpsHost && proxy.Port == 2222,
    "HTTP CONNECT stays loopback and preserves the nonstandard remote SSH port");
foreach (var valid in new[] { 1, 65535 })
{
    settings.VpsPort = valid;
    Check(SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5)).Port == valid,
        "remote SSH port boundary is accepted without opening a connection");
}
foreach (var invalid in new[] { -1, 0, 65536 })
{
    settings.VpsPort = invalid;
    Reject(() => SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5)), "invalid remote SSH port rejected");
    // The public service entry points must reject invalid ports before touching
    // a private key or attempting a connection. The fixture path never exists.
    Reject(() => new SshService().TestConnectionAsync(settings, "not-a-key", null, _ => false).GetAwaiter().GetResult(),
        "SSH test validates its remote port before accessing a key");
    Reject(() => new SshService().ExecuteAsync(settings, "not-a-key", null, "unused").GetAwaiter().GetResult(),
        "SSH execution validates its remote port before accessing a key");
}
settings.VpsPort = 2222;
foreach (var invalid in new[] { -1, 80, 65536 })
{
    settings.SshProxyPort = invalid;
    Reject(() => SshService.CreateConnectionInfo(settings, auth, TimeSpan.FromSeconds(5)), "invalid local SSH proxy port rejected");
}

var previousCulture = CultureInfo.CurrentUICulture;
try
{
    foreach (var chinese in new[] { false, true })
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(chinese ? "zh-CN" : "en-US");
        foreach (var proxyPort in new[] { 0, 2080 })
        {
            settings.SshProxyPort = proxyPort;
            string Diagnose(Exception error) => SshService.DescribeConnectionError(error, settings);
            void CheckFailure(Exception error, string expected, string label)
            {
                var message = Diagnose(error);
                Check(message.Contains("203.0.113.20:2222", StringComparison.Ordinal)
                    && message.Contains(proxyPort == 0 ? (chinese ? "直连" : "direct connection") : "127.0.0.1:2080", StringComparison.Ordinal)
                    && message.Contains(expected, StringComparison.Ordinal), label);
            }
            CheckFailure(new SshOperationTimeoutException("fixture timeout"), chinese ? "连接或响应超时" : "connection or response timed out",
                "SSH timeout identifies the actual destination and chosen route");
            CheckFailure(new TimeoutException("fixture timeout"), chinese ? "连接或响应超时" : "connection or response timed out",
                "standard timeout is classified without claiming a blocked port");
            CheckFailure(new SocketException((int)SocketError.TimedOut), chinese ? "连接或响应超时" : "connection or response timed out",
                "socket timeout has the timeout diagnosis");
            CheckFailure(new IOException("fixture wrapper", new SocketException((int)SocketError.ConnectionRefused)),
                chinese ? "连接被拒绝" : "connection was refused", "nested connection refusal is identified");
            CheckFailure(new SocketException((int)SocketError.HostNotFound), chinese ? "网络连接失败" : "network connection failed",
                "DNS socket failure has network guidance and retains the error code");
            var hostTrust = Diagnose(new SshConnectionException("fixture host key", DisconnectReason.HostKeyNotVerifiable,
                new SocketException((int)SocketError.TimedOut)));
            Check(hostTrust.Contains(chinese ? "主机指纹验证未通过" : "host key verification failed", StringComparison.Ordinal)
                && !hostTrust.Contains(chinese ? "连接或响应超时" : "connection or response timed out", StringComparison.Ordinal),
                "host-key rejection takes precedence over a nested transport error");
            var authentication = Diagnose(new SshAuthenticationException("fixture authentication", new TimeoutException("fixture nested timeout")));
            Check(authentication.Contains(chinese ? "身份验证失败" : "authentication failed", StringComparison.Ordinal)
                && !authentication.Contains(chinese ? "网络连接失败" : "network connection failed", StringComparison.Ordinal)
                && !authentication.Contains(chinese ? "连接或响应超时" : "connection or response timed out", StringComparison.Ordinal),
                "authentication failure is not misreported as a port or proxy outage");
            var unknown = Diagnose(new SshConnectionException("fixture protocol negotiation failed"));
            Check(unknown.Contains("fixture protocol negotiation failed", StringComparison.Ordinal)
                && unknown.Contains(chinese ? "技术原因" : "Technical reason", StringComparison.Ordinal)
                && unknown.Contains(chinese ? "连接未完成" : "connection did not complete", StringComparison.Ordinal)
                && !unknown.Contains(chinese ? "网络连接失败" : "network connection failed", StringComparison.Ordinal),
                "unclassified SSH negotiation failure retains its technical reason without assuming network blocking");
            var network = Diagnose(new SocketException((int)SocketError.NetworkUnreachable));
            Check(network.Contains("NetworkUnreachable", StringComparison.Ordinal)
                && network.Contains(proxyPort == 0 ? (chinese ? "服务器已配置其他 SSH 端口" : "already configured on the server")
                    : (chinese ? "CONNECT 规则" : "CONNECT rules"), StringComparison.Ordinal),
                "network suggestions match the selected route and do not invent a server port");
        }
        settings.SshProxyPort = 2080;
        var proxyFailure = SshService.DescribeConnectionError(
            new ProxyException("fixture CONNECT denial", new SocketException((int)SocketError.ConnectionRefused)), settings);
        Check(proxyFailure.Contains(chinese ? "HTTP 代理连接失败" : "HTTP proxy connection failed", StringComparison.Ordinal)
            && proxyFailure.Contains("127.0.0.1:2080", StringComparison.Ordinal)
            && !proxyFailure.Contains(chinese ? "SSH 连接被拒绝" : "SSH connection was refused", StringComparison.Ordinal),
            "explicit proxy failure takes precedence over its inner socket error");
        settings.SshKeyPath = "fixture-stored-key-path";
        var secretText = "fixture-key-body-do-not-print";
        var secretError = new InvalidOperationException("fixture-candidate-key-path " + fixturePassword + " " + settings.SshKeyPath
            + " -----BEGIN OPENSSH PRIVATE KEY-----\n" + secretText + "\n-----END OPENSSH PRIVATE KEY-----");
        var redacted = SshService.DescribeConnectionError(secretError, settings, "fixture-candidate-key-path", fixturePassword);
        Check(!redacted.Contains(fixturePassword, StringComparison.Ordinal)
            && !redacted.Contains(settings.SshKeyPath, StringComparison.Ordinal)
            && !redacted.Contains("fixture-candidate-key-path", StringComparison.Ordinal)
            && !redacted.Contains(secretText, StringComparison.Ordinal)
            && redacted.Contains(chinese ? "已隐藏" : "redacted", StringComparison.Ordinal),
            "both languages redact key paths, passphrases and private-key material");
        var incompletePem = SshService.DescribeConnectionError(new Exception("-----BEGIN PRIVATE KEY-----\n" + secretText), settings);
        Check(!incompletePem.Contains(secretText, StringComparison.Ordinal), "unterminated private-key material is also redacted");
    }
}
finally { CultureInfo.CurrentUICulture = previousCulture; }
Console.WriteLine($"PASS: {passed} deployment/profile/SSH checks. No remote connections or user settings were used.");
