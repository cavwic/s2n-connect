using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Diagnostics;
using S2N.Models;
using S2N.Services;

internal static class HysteriaChecks
{
    internal static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            count++;
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=s2n-test.example.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pem = certificate.ExportCertificatePem();
        var service = new SingBoxConfigService();
        var settings = new AppSettings
        {
            ManagedConfigPath = Path.Combine(AppPaths.DataDirectory, "hysteria-check.json"),
            ProcessNames = [], DomainSuffixes = [], LocalProxyPort = 2087, GlobalMode = true
        };
        VpsProfile Fixture() => new()
        {
            Transport = "hysteria2", ServerIp = "192.0.2.10", ServerName = "s2n-test.example.invalid",
            Password = "SYNTHETIC_UDP_PASSWORD_+/:@", ServerPort = 443
        };
        foreach (var host in new[] { "192.0.2.10", "2001:db8::10", "s2n-test.example.invalid" })
        foreach (var customCertificate in new[] { false, true })
        {
            var profile = Fixture(); profile.ServerIp = host; profile.CertificatePem = customCertificate ? pem : "";
            await service.WriteAsync(settings, profile, "192.168.1.1", interfaceName: "S2N-test-run");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.ManagedConfigPath));
            var root = document.RootElement;
            var outbound = root.GetProperty("outbounds")[0];
            Check(outbound.GetProperty("type").GetString() == "hysteria2" && outbound.GetProperty("server").GetString() == host,
                "UDP uses Hysteria2 for IPv4, IPv6 and DNS destinations");
            Check(outbound.GetProperty("password").GetString() == profile.Password && !outbound.TryGetProperty("uuid", out _),
                "UDP auth preserves special characters and excludes VLESS identifiers");
            var tls = outbound.GetProperty("tls");
            Check(tls.GetProperty("enabled").GetBoolean() && !tls.GetProperty("insecure").GetBoolean()
                && !tls.TryGetProperty("reality", out _) && !tls.TryGetProperty("utls", out _),
                "QUIC TLS verifies certificates and does not reuse incompatible REALITY/uTLS settings");
            Check(tls.TryGetProperty("certificate", out var certificates) == customCertificate,
                "explicit custom trust is present only when configured");
            if (customCertificate)
                Check(string.Join('\n', certificates.EnumerateArray().Select(line => line.GetString())) == pem.Replace("\r\n", "\n").Trim(),
                    "complete public PEM trust is embedded in TLS certificate line array");
            Check(outbound.GetProperty("domain_resolver").GetString() == "lan-dns", "server bootstrap avoids a proxy DNS dependency cycle");
            var inbounds = root.GetProperty("inbounds");
            Check(inbounds[0].GetProperty("listen_port").GetInt32() == 2087
                && !inbounds[0].GetProperty("set_system_proxy").GetBoolean(), "isolated local health listener uses selected port without system proxy changes");
            Check(inbounds[1].GetProperty("interface_name").GetString() == "S2N-test-run", "per-session TUN identity reaches generated config");
            Check(root.GetProperty("route").GetProperty("final").GetString() == "vless-out", "existing global routing uses the selected UDP outbound");
            var nativeCore = Environment.GetEnvironmentVariable("S2N_CONFIGCHECK_CORE");
            if (!string.IsNullOrEmpty(nativeCore))
            {
                var start = new ProcessStartInfo(nativeCore)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true, RedirectStandardOutput = true
                };
                foreach (var argument in new[] { "check", "-c", settings.ManagedConfigPath }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Native configuration checker did not start.");
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                await Task.WhenAll(error, output);
                Check(process.ExitCode == 0, "the installed native core accepts generated Hysteria2 configuration: " + error.Result);
            }
        }
        foreach (var mutate in new Action<VpsProfile>[]
        {
            p => p.Transport = "unknown", p => p.Password = "", p => p.Password = "secret\ninvalid",
            p => p.ServerIp = "192.0.2.999", p => p.ServerIp = "http://vps.example.invalid",
            p => p.ServerName = "", p => p.ServerName = "vps.example.invalid/path",
            p => p.CertificatePem = "-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----",
            p => p.CertificatePem = pem + "\n-----BEGIN PRIVATE KEY-----\nDO_NOT_EXPORT\n-----END PRIVATE KEY-----"
        })
        {
            var invalid = Fixture(); mutate(invalid);
            var previous = await File.ReadAllTextAsync(settings.ManagedConfigPath);
            var rejected = false;
            try { await service.WriteAsync(settings, invalid, "192.168.1.1"); }
            catch (InvalidDataException exception) { rejected = true; Check(invalid.Password.Length == 0 || !exception.Message.Contains(invalid.Password), "errors omit connection passwords"); }
            Check(rejected, "malformed protocol/authentication/TLS input is rejected before launch");
            Check(previous == await File.ReadAllTextAsync(settings.ManagedConfigPath), "invalid UDP profiles leave last valid configuration intact");
        }
        return count;
    }
}
