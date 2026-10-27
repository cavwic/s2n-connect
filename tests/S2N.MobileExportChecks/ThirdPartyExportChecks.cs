using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using S2N.Models;
using S2N.Services;

internal static class ThirdPartyExportChecks
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            count++;
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=s2n-test.example.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var exporter = new ThirdPartyProfileExportService();
        var temporary = Directory.CreateTempSubdirectory("S2N.ThirdPartyChecks-").FullName;
        try
        {
            foreach (var transport in new[] { "vless", "hysteria2" })
            foreach (var server in new[] { "192.0.2.10", "2001:db8::10", "s2n-test.example.invalid" })
            foreach (var customTrust in transport == "hysteria2" ? new[] { false, true } : new[] { false })
            {
                var profile = new VpsProfile
                {
                    Transport = transport, ServerIp = server, ServerPort = 443, ServerName = "s2n-test.example.invalid",
                    Uuid = "12345678-1234-4234-8234-123456789abc", PublicKey = new string('A', 43), ShortId = "1234abcd",
                    Password = "SYNTHETIC_IOS_PASSWORD", CertificatePem = customTrust ? certificate.ExportCertificatePem() : ""
                };
                var json = exporter.Serialize(profile);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var outbound = root.GetProperty("outbounds")[1];
                Check(outbound.GetProperty("type").GetString() == transport && outbound.GetProperty("server").GetString() == server,
                    "portable export preserves selected protocol and IPv4/IPv6/DNS destination");
                var tls = outbound.GetProperty("tls");
                Check(tls.GetProperty("enabled").GetBoolean()
                    && (!tls.TryGetProperty("insecure", out var insecure) || !insecure.GetBoolean()), "third-party profile retains TLS verification");
                Check(tls.TryGetProperty("certificate", out _) == customTrust, "portable export retains configured custom PEM trust");
                Check(outbound.GetProperty("domain_resolver").GetString() == "bootstrap-dns"
                    && root.GetProperty("dns").GetProperty("servers")[0].GetProperty("type").GetString() == "local",
                    "server FQDN is bootstrapped without depending on its own proxy tunnel");
                Check(root.GetProperty("route").GetProperty("final").GetString() == "proxy"
                    && root.GetProperty("route").GetProperty("rules")[2].GetProperty("outbound").GetString() == "direct",
                    "portable TUN proxies public traffic and keeps private destinations direct");
                Check(root.GetProperty("inbounds")[0].GetProperty("address").GetArrayLength() == 2,
                    "portable profile captures IPv4 and IPv6 in its client-managed TUN");
                Check(!new[] { "process_name", "process_path", "interface_name", "certificate_path", "private_key", "sshKeyPath", "managedConfigPath" }
                    .Any(forbidden => json.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    && !root.GetProperty("log").TryGetProperty("output", out _), "portable profile has no desktop paths, process rules or server private keys");
                var nativeCore = Environment.GetEnvironmentVariable("S2N_CONFIGCHECK_CORE");
                if (!string.IsNullOrEmpty(nativeCore))
                {
                    var path = Path.Combine(temporary, "ios-check.json");
                    File.WriteAllText(path, json);
                    var start = new ProcessStartInfo(nativeCore)
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    foreach (var argument in new[] { "check", "-c", path }) start.ArgumentList.Add(argument);
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Native checker did not start.");
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30000)) { process.Kill(); throw new TimeoutException("Native portable profile check timed out."); }
                    Task.WaitAll(output, error);
                    Check(process.ExitCode == 0, "native sing-box accepts portable full configuration: " + error.Result);
                }
            }
        }
        finally
        {
            var resolved = Path.GetFullPath(temporary);
            var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("S2N.ThirdPartyChecks-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected portable profile test path.");
            Directory.Delete(resolved, true);
        }
        return count;
    }
}
