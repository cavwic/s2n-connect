using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using S2N.Models;
using S2N.Services;

internal static class HysteriaExportChecks
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL: " + message);
            count++;
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=s2n-test.example.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pem = certificate.ExportCertificatePem();
        VpsProfile Fixture() => new()
        {
            Transport = "hysteria2", ServerIp = "192.0.2.10", ServerPort = 443,
            ServerName = "s2n-test.example.invalid", Password = "SYNTHETIC +/:@PASSWORD",
            // Stale legacy values must never appear in a UDP connection export.
            Uuid = "SYNTHETIC_LEGACY_UUID", PublicKey = "SYNTHETIC_LEGACY_KEY"
        };
        var exporter = new MobileProfileExportService();
        foreach (var customCertificate in new[] { false, true })
        {
            var profile = Fixture(); profile.CertificatePem = customCertificate ? pem : "";
            var json = exporter.Serialize(profile);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var fields = new[] { "schemaVersion", "transport", "serverIp", "serverPort", "password", "serverName", "certificatePem", "updatedAt" };
            Check(root.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(fields.Order()), "UDP schema has an explicit client-only allowlist");
            Check(root.GetProperty("schemaVersion").GetInt32() == 2 && root.GetProperty("transport").GetString() == "hysteria2", "UDP mobile export declares schema 2 and protocol");
            Check(root.GetProperty("password").GetString() == profile.Password && !json.Contains("SYNTHETIC_LEGACY"), "UDP password preserved without stale VLESS fields");
            Check(root.GetProperty("certificatePem").GetString()!.Length > 0 == customCertificate && !json.Contains("PRIVATE KEY"), "only configured public certificate trust is exported");
            var parsed = JsonSerializer.Deserialize<VpsProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Check(VpsProfileValidator.Normalize(parsed).Password == profile.Password, "UDP mobile export round-trips as a validated profile");
        }
        var caProfile = Fixture(); caProfile.ServerIp = "2001:db8::10";
        var uri = exporter.ExportConnectionUri(caProfile);
        Check(uri.StartsWith("hysteria2://SYNTHETIC%20%2B%2F%3A%40PASSWORD@[2001:db8::10]:443/"), "Hysteria URI escapes credentials and brackets IPv6");
        Check(!uri.Contains("insecure", StringComparison.OrdinalIgnoreCase), "URI never weakens certificate verification");
        var pinned = Fixture(); pinned.CertificatePem = pem;
        var rejected = false;
        try { _ = exporter.ExportConnectionUri(pinned); }
        catch (InvalidOperationException exception) { rejected = !exception.Message.Contains(pinned.Password); }
        Check(rejected, "custom certificate profile cannot silently become an insecure URI");
        foreach (var mutate in new Action<VpsProfile>[]
        {
            p => p.Transport = "future-protocol", p => p.Password = "", p => p.Password = "secret\nvalue",
            p => p.CertificatePem = "NOT_A_CERTIFICATE", p => p.CertificatePem = "-----BEGIN PRIVATE KEY-----\nPRIVATE\n-----END PRIVATE KEY-----"
        })
        {
            var invalid = Fixture(); mutate(invalid);
            var caught = false;
            try { exporter.Serialize(invalid); }
            catch (InvalidDataException exception) { caught = true; Check(!exception.Message.Contains("SYNTHETIC"), "invalid exports do not disclose rejected credentials"); }
            Check(caught, "unsupported protocols, missing auth and malformed trust are rejected");
        }
        return count;
    }
}
