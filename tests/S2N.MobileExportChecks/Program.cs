using System.Text.Json;
using S2N.Models;
using S2N.Services;

try
{
    RunChecks();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}

static void RunChecks()
{
    var passed = 0;
    var exporter = new MobileProfileExportService();
    void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        passed++;
    }

    void Rejects(Action<VpsProfile> change, string description)
    {
        var invalid = Fixture();
        change(invalid);
        try { exporter.Serialize(invalid); }
        catch (InvalidDataException)
        {
            passed++;
            return;
        }
        throw new InvalidOperationException("FAIL: " + description);
    }

    var fixture = Fixture();
    var json = exporter.Serialize(fixture);
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    var expectedFields = new[]
    {
        "serverIp", "serverPort", "uuid", "publicKey", "shortId", "serverName", "updatedAt"
    };
    Check(root.EnumerateObject().Select(property => property.Name).Order().SequenceEqual(expectedFields.Order()),
        "only seven explicitly allowed mobile connection fields are exported");
    Check(root.GetProperty("serverIp").GetString() == fixture.ServerIp, "server address is preserved");
    Check(root.GetProperty("serverPort").GetInt32() == fixture.ServerPort, "server port is numeric and preserved");
    Check(root.GetProperty("uuid").GetString() == fixture.Uuid, "UUID is preserved");
    Check(root.GetProperty("publicKey").GetString() == fixture.PublicKey, "REALITY public key is preserved");
    Check(root.GetProperty("shortId").GetString() == fixture.ShortId, "REALITY short ID is preserved");
    Check(root.GetProperty("serverName").GetString() == fixture.ServerName, "TLS server name is preserved");
    Check(root.GetProperty("updatedAt").GetDateTimeOffset() == fixture.UpdatedAt, "profile timestamp is preserved");

    foreach (var forbidden in new[]
    {
        "privateKey", "private_key", "sshKeyPath", "sshPrivateKey", "passphrase", "password",
        "knownHostFingerprints", "vpsUser", "vpsHost", "managedConfigPath", "singBoxExecutablePath"
    })
        Check(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "export omits " + forbidden);

    var roundTrip = JsonSerializer.Deserialize<VpsProfile>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    Check(roundTrip.ServerIp == fixture.ServerIp && roundTrip.ServerPort == fixture.ServerPort
        && roundTrip.Uuid == fixture.Uuid && roundTrip.PublicKey == fixture.PublicKey
        && roundTrip.ShortId == fixture.ShortId && roundTrip.ServerName == fixture.ServerName
        && roundTrip.UpdatedAt == fixture.UpdatedAt, "export JSON can be read back as a complete client profile");

    var normalized = Fixture();
    normalized.ServerIp = "  vps.example.invalid  ";
    normalized.Uuid = normalized.Uuid.ToUpperInvariant();
    normalized.PublicKey = "  " + normalized.PublicKey + "  ";
    normalized.ShortId = "  ABCDEF12  ";
    normalized.ServerName = "  www.example.invalid  ";
    using var normalizedJson = JsonDocument.Parse(exporter.Serialize(normalized));
    Check(normalizedJson.RootElement.GetProperty("serverIp").GetString() == "vps.example.invalid"
        && normalizedJson.RootElement.GetProperty("shortId").GetString() == "abcdef12"
        && normalizedJson.RootElement.GetProperty("uuid").GetString() == fixture.Uuid
        && normalizedJson.RootElement.GetProperty("publicKey").GetString() == fixture.PublicKey,
        "safe whitespace and identifier normalization preserve compatible values");

    foreach (var address in new[] { "2001:db8::1", "vps.example.invalid" })
    {
        var profile = Fixture(); profile.ServerIp = address;
        Check(exporter.Serialize(profile).Length > 0, "IPv6 and DNS server addresses are supported");
    }
    var emptyShortId = Fixture(); emptyShortId.ShortId = "";
    Check(JsonDocument.Parse(exporter.Serialize(emptyShortId)).RootElement.GetProperty("shortId").GetString() == "",
        "a protocol-valid empty REALITY short ID remains supported");
    try { exporter.Serialize(null); throw new InvalidOperationException("FAIL: undeployed profile must be rejected"); }
    catch (InvalidDataException) { passed++; }

    Rejects(profile => profile.ServerIp = "", "missing server address is rejected");
    Rejects(profile => profile.ServerIp = "https://example.invalid/profile", "URL is not a server hostname");
    Rejects(profile => profile.ServerPort = 0, "zero port is rejected");
    Rejects(profile => profile.ServerPort = 65536, "out-of-range port is rejected");
    Rejects(profile => profile.Uuid = "", "missing UUID is rejected");
    Rejects(profile => profile.Uuid = "invalid", "malformed UUID is rejected");
    Rejects(profile => profile.Uuid = Guid.Empty.ToString(), "empty UUID is rejected");
    Rejects(profile => profile.PublicKey = "", "missing public key is rejected");
    Rejects(profile => profile.PublicKey = new string('A', 42), "short public key is rejected");
    Rejects(profile => profile.PublicKey = new string('A', 44), "long public key is rejected");
    Rejects(profile => profile.PublicKey = new string('!', 43), "non-base64url public key is rejected");
    Rejects(profile => profile.ShortId = "a", "odd-length short ID is rejected");
    Rejects(profile => profile.ShortId = "xz", "non-hexadecimal short ID is rejected");
    Rejects(profile => profile.ShortId = new string('a', 18), "long short ID is rejected");
    Rejects(profile => profile.ServerName = "", "missing TLS server name is rejected");
    Rejects(profile => profile.ServerName = "https://example.invalid/", "TLS server name must not be a URL");

    const string marker = "SYNTHETIC_PRIVATE_DATA_MUST_NOT_APPEAR_IN_ERRORS";
    var sensitiveInvalid = Fixture(); sensitiveInvalid.PublicKey = marker;
    try { exporter.Serialize(sensitiveInvalid); throw new InvalidOperationException("FAIL: invalid marker must be rejected"); }
    catch (InvalidDataException exception)
    {
        Check(!exception.Message.Contains(marker, StringComparison.Ordinal), "validation errors do not expose rejected credential values");
    }

    passed += HysteriaExportChecks.Run();
    passed += ThirdPartyExportChecks.Run();
    Console.WriteLine($"PASS: {passed} mobile and third-party export checks. Temporary fixtures only; no network or user settings.");
}

static VpsProfile Fixture() => new()
{
    ServerIp = "203.0.113.10",
    ServerPort = 443,
    Uuid = "12345678-1234-4234-8234-123456789abc",
    PublicKey = new string('A', 43),
    ShortId = "1234abcd",
    ServerName = "www.example.invalid",
    UpdatedAt = DateTimeOffset.Parse("2026-10-15T00:00:00+00:00")
};
