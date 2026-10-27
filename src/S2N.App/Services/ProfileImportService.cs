using System.Text.Json;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class ProfileImportService
{
    public const int MaximumFileBytes = 1024 * 1024;

    public async Task<VpsProfile> ReadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaximumFileBytes)
                throw Invalid("配置文件不能超过 1 MB。", "The profile must be 1 MB or smaller.");
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        return Parse(buffer.ToArray());
    }

    public VpsProfile Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaximumFileBytes)
            throw Invalid("配置文件不能超过 1 MB。", "The profile must be 1 MB or smaller.");
        if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            RejectDuplicateProperties(root);
            if (root.ValueKind != JsonValueKind.Object) throw InvalidFormat();
            VpsProfile profile;
            if (Find(root, "outbounds", out var outbounds))
            {
                if (outbounds.ValueKind != JsonValueKind.Array) throw InvalidFormat();
                var candidates = outbounds.EnumerateArray().Where(value =>
                    value.ValueKind == JsonValueKind.Object
                    && Text(value, "type") is "vless" or "hysteria2").ToArray();
                if (candidates.Length != 1)
                    throw Invalid("请选择只包含一个受支持节点的配置。", "Select a configuration with exactly one supported server.");
                profile = FromOutbound(candidates[0]);
            }
            else
            {
                var transport = Text(root, "transport");
                if (Find(root, "schemaVersion", out var version))
                {
                    if (!version.TryGetInt32(out var schema) || schema is not (1 or 2))
                        throw Invalid("不支持此手机配置版本。", "This mobile profile version is not supported.");
                    if (schema == 1 && transport.Length > 0 && transport != "vless"
                        || schema == 2 && transport != "hysteria2") throw InvalidFormat();
                }
                profile = new VpsProfile
                {
                    Transport = transport.Length == 0 ? "vless" : transport,
                    ServerIp = Text(root, "serverIp"), ServerPort = Port(root, "serverPort"),
                    ServerName = Text(root, "serverName"), Uuid = Text(root, "uuid"),
                    PublicKey = Text(root, "publicKey"), ShortId = Text(root, "shortId"),
                    Password = Text(root, "password"), CertificatePem = Text(root, "certificatePem")
                };
            }
            return VpsProfileValidator.Normalize(profile);
        }
        catch (JsonException) { throw InvalidFormat(); }
        catch (InvalidOperationException) { throw InvalidFormat(); }
        catch (FormatException) { throw InvalidFormat(); }
    }

    private static VpsProfile FromOutbound(JsonElement value)
    {
        if (!Find(value, "tls", out var tls) || tls.ValueKind != JsonValueKind.Object
            || !IsTrue(tls, "enabled") || IsTrue(tls, "insecure") || IsTrue(tls, "disable_sni"))
            throw Invalid("导入的节点必须启用 TLS 证书验证。", "The imported server must verify its TLS certificate.");
        var profile = new VpsProfile
        {
            Transport = Text(value, "type"), ServerIp = Text(value, "server"),
            ServerPort = Port(value, "server_port"), ServerName = Text(tls, "server_name")
        };
        var allowedTls = profile.Transport == "hysteria2"
            ? new[] { "enabled", "server_name", "insecure", "certificate" }
            : new[] { "enabled", "server_name", "insecure", "utls", "reality" };
        if (tls.EnumerateObject().Any(property => !allowedTls.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            || Find(value, "transport", out _))
            throw Invalid("此节点包含暂不支持的 TLS 或传输选项。", "This server uses unsupported TLS or transport options.");
        if (profile.Transport == "hysteria2")
        {
            if (Find(value, "obfs", out _) || Find(value, "server_ports", out _)
                || Find(tls, "certificate_path", out _) || Find(tls, "certificate_public_key_sha256", out _)
                || Find(tls, "certificate_sha256", out _) || Find(tls, "client_certificate", out _))
                throw Invalid("此节点包含暂不支持的选项，请使用 S2N 导出的连接配置。", "This server uses unsupported options. Import a profile exported by S2N.");
            profile.Password = Text(value, "password");
            if (Find(tls, "certificate", out var certificate))
                profile.CertificatePem = certificate.ValueKind == JsonValueKind.Array
                    ? string.Join("\n", certificate.EnumerateArray().Select(item => item.GetString()))
                    : certificate.GetString() ?? "";
        }
        else
        {
            if (!Find(tls, "reality", out var reality) || !IsTrue(reality, "enabled")
                || Text(value, "flow") != "xtls-rprx-vision") throw InvalidFormat();
            profile.Uuid = Text(value, "uuid"); profile.PublicKey = Text(reality, "public_key");
            profile.ShortId = Text(reality, "short_id");
        }
        return profile;
    }

    private static bool Find(JsonElement value, string name, out JsonElement found)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                { found = property.Value; return true; }
        found = default; return false;
    }

    private static string Text(JsonElement value, string name) => Find(value, name, out var found)
        ? found.GetString() ?? "" : "";
    private static int Port(JsonElement value, string name) => Find(value, name, out var found)
        && found.TryGetInt32(out var port) ? port : 0;
    private static bool IsTrue(JsonElement value, string name) => Find(value, name, out var found)
        && found.GetBoolean();

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw InvalidFormat();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static InvalidDataException InvalidFormat() => Invalid(
        "连接配置格式无效。", "The connection profile is invalid.");
    private static InvalidDataException Invalid(string chinese, string english) => new(L10n.T(chinese, english));
}
