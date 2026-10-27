using System.Text;
using System.Text.Json;
using S2N.Models;
using S2N.Services;

var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    passed++;
}
void Reject(object value, string description)
{
    try { new ProfileImportService().Parse(JsonSerializer.SerializeToUtf8Bytes(value)); }
    catch (InvalidDataException error)
    {
        Check(!error.Message.Contains("credential-sentinel", StringComparison.Ordinal), description + " without credential disclosure");
        return;
    }
    throw new Exception("FAIL: accepted " + description);
}
try
{
    var importer = new ProfileImportService();
    var oldProfile = new { serverIp = "203.0.113.12", serverPort = 443,
        uuid = "9fe7208c-e3d6-445f-bec4-c90cb36fd503", publicKey = new string('A', 43),
        shortId = "12ab", serverName = "www.example.com" };
    var old = importer.Parse(JsonSerializer.SerializeToUtf8Bytes(oldProfile));
    Check(old.Transport == "vless" && old.ServerPort == 443, "legacy profile remains VLESS");
    var udp = new { schemaVersion = 2, transport = "hysteria2", serverIp = "203.0.113.12", serverPort = 443,
        serverName = "203.0.113.12", password = "credential-sentinel", certificatePem = "" };
    var imported = importer.Parse(JsonSerializer.SerializeToUtf8Bytes(udp));
    Check(imported.Transport == "hysteria2" && imported.Password == udp.password, "mobile UDP profile imports credentials correctly");
    var tls = new { enabled = true, server_name = "203.0.113.12", insecure = false };
    var outbound = new { type = "hysteria2", server = "203.0.113.12", server_port = 443, password = udp.password, tls };
    var raw = new { inbounds = new[] { new { type = "tun", auto_route = true } },
        outbounds = new object[] { outbound, new { type = "direct" } },
        sshKeyPath = "must-not-be-imported", route = new { final = "direct" } };
    var extracted = importer.Parse(JsonSerializer.SerializeToUtf8Bytes(raw));
    Check(extracted.Password == udp.password && extracted.Transport == "hysteria2", "extracts connection fields from existing core config");
    Check(!JsonSerializer.Serialize(extracted).Contains("must-not-be-imported"), "local settings and private paths are excluded");
    Reject(new { outbounds = new[] { outbound, outbound } }, "ambiguous multiple servers");
    Reject(new { schemaVersion = 3, transport = "hysteria2", password = udp.password }, "unsupported schema");
    Reject(new { schemaVersion = 1, transport = "hysteria2", password = udp.password }, "UDP in legacy schema");
    Reject(new { schemaVersion = 2, transport = "vless", password = udp.password }, "VLESS in UDP schema");
    Reject(new { outbounds = new[] { new { type = "hysteria2", server = "203.0.113.12", server_port = 443,
        password = udp.password, tls = new { enabled = true, server_name = "203.0.113.12", insecure = true } } } }, "disabled certificate verification");
    Reject(new { outbounds = new[] { new { type = "hysteria2", server = "203.0.113.12", server_port = 443,
        password = udp.password, tls = new { enabled = true, server_name = "203.0.113.12", certificate_path = "private-path" } } } }, "external certificate dependency");
    Reject(new { outbounds = new[] { new { type = "hysteria2", server = "203.0.113.12", server_port = 443,
        password = udp.password, tls = new { enabled = true, server_name = "203.0.113.12", ech = new { enabled = true } } } } }, "unsupported TLS ECH");
    Reject(new { outbounds = new[] { new { type = "hysteria2", server = "203.0.113.12", server_port = 443,
        password = udp.password, tls = new { enabled = true, server_name = "203.0.113.12", client_key_path = "private-path" } } } }, "unsupported client authentication");
    foreach (var invalid in new[] { "{\"transport\":\"vless\",\"Transport\":\"hysteria2\"}", "[]", "null", "{\"password\":\"credential-sentinel\",broken}" })
    {
        try { importer.Parse(Encoding.UTF8.GetBytes(invalid)); throw new Exception("FAIL: accepted malformed JSON"); }
        catch (InvalidDataException error) { Check(!error.Message.Contains("credential-sentinel"), "invalid JSON is rejected safely"); }
    }
    var bom = new byte[] { 0xef, 0xbb, 0xbf }.Concat(JsonSerializer.SerializeToUtf8Bytes(udp)).ToArray();
    Check(importer.Parse(bom).Transport == "hysteria2", "UTF-8 BOM config imports");
    await using (var large = new MemoryStream(new byte[ProfileImportService.MaximumFileBytes + 1]))
    {
        try { await importer.ReadAsync(large); throw new Exception("FAIL: oversized file accepted"); }
        catch (InvalidDataException) { Check(true, "oversized stream stops at the limit"); }
    }
    var store = new SettingsStore();
    await store.SaveVpsProfileAsync(imported, AppPaths.VpsProfilePath);
    var previous = await File.ReadAllBytesAsync(AppPaths.VpsProfilePath);
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await store.SaveVpsProfileAsync(old, AppPaths.VpsProfilePath, canceled.Token); throw new Exception("FAIL: canceled save succeeded"); }
    catch (OperationCanceledException)
    {
        var afterCanceledSave = await File.ReadAllBytesAsync(AppPaths.VpsProfilePath);
        Check(previous.SequenceEqual(afterCanceledSave), "canceled save preserves previous credentials");
    }
    Check(!Directory.EnumerateFiles(AppPaths.DataDirectory, "*.tmp").Any(), "temporary credential file is cleaned up");
    Console.WriteLine($"PASS: {passed} profile import checks");
}
catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
finally { AppPaths.Cleanup(); }
