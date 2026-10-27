using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

/// <summary>Validates connection fields without including credential values in errors.</summary>
public static class VpsProfileValidator
{
    public static void Validate(VpsProfile profile) => _ = Normalize(profile);

    public static VpsProfile Normalize(VpsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var transport = (profile.Transport ?? "vless").Trim().ToLowerInvariant();
        if (transport is not ("vless" or "hysteria2")) throw Invalid("连接协议", "connection protocol");
        var server = (profile.ServerIp ?? "").Trim();
        var name = (profile.ServerName ?? "").Trim();
        if (!ValidHost(server)) throw Invalid("服务器地址", "server address");
        if (profile.ServerPort is < 1 or > 65535) throw Invalid("服务器端口", "server port");
        if (!ValidHost(name)) throw Invalid("TLS 服务器名称", "TLS server name");
        var result = new VpsProfile
        {
            Transport = transport, ServerIp = server, ServerPort = profile.ServerPort,
            ServerName = name, UpdatedAt = profile.UpdatedAt
        };
        if (transport == "hysteria2")
        {
            var password = profile.Password ?? "";
            if (string.IsNullOrWhiteSpace(password) || password.Length > 4096 || password.Any(char.IsControl))
                throw Invalid("连接密码", "connection password");
            result.Password = password;
            result.CertificatePem = NormalizeCertificates(profile.CertificatePem ?? "");
            result.Uuid = ""; result.PublicKey = ""; result.ShortId = "";
        }
        else
        {
            if (!Guid.TryParse(profile.Uuid, out var uuid) || uuid == Guid.Empty) throw Invalid("UUID", "UUID");
            var key = (profile.PublicKey ?? "").Trim();
            if (!Regex.IsMatch(key, "\\A[A-Za-z0-9_-]{43}\\z") || !IsPublicKey(key))
                throw Invalid("REALITY 公钥", "REALITY public key");
            var shortId = (profile.ShortId ?? "").Trim();
            if (shortId.Length > 16 || shortId.Length % 2 != 0 || !shortId.All(Uri.IsHexDigit))
                throw Invalid("REALITY short ID", "REALITY short ID");
            result.Uuid = uuid.ToString(); result.PublicKey = key; result.ShortId = shortId.ToLowerInvariant();
            result.Password = ""; result.CertificatePem = "";
        }
        return result;
    }

    private static bool IsPublicKey(string value)
    {
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=").Length == 32; }
        catch (FormatException) { return false; }
    }

    public static bool ValidHost(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace)) return false;
        if (value.Contains(':')) return Regex.IsMatch(value, "\\A[0-9a-fA-F:]+\\z") && IPAddress.TryParse(value, out _);
        if (value.All(character => char.IsAsciiDigit(character) || character == '.'))
            return value.Split('.').Length == 4 && value.Split('.').All(part =>
                part.Length is > 0 and <= 3 && int.TryParse(part, out var segment) && segment is >= 0 and <= 255);
        try
        {
            var ascii = new IdnMapping().GetAscii(value);
            return ascii.Length is > 0 and <= 253 && !ascii.EndsWith('.') && ascii.Contains('.')
                && ascii.Split('.').All(label => label.Length is > 0 and <= 63
                    && Regex.IsMatch(label, "\\A[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\\z"));
        }
        catch (ArgumentException) { return false; }
    }

    private static string NormalizeCertificates(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length > 32768 || normalized.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            throw Invalid("TLS 证书", "TLS certificate");
        const string pattern = "-----BEGIN CERTIFICATE-----\\s+[A-Za-z0-9+/=\\s]+?-----END CERTIFICATE-----";
        var matches = Regex.Matches(normalized, pattern);
        if (matches.Count is < 1 or > 8 || !string.IsNullOrWhiteSpace(Regex.Replace(normalized, pattern, "")))
            throw Invalid("TLS 证书", "TLS certificate");
        try
        {
            foreach (Match match in matches)
            {
                using var certificate = X509Certificate2.CreateFromPem(match.Value);
                if (certificate.HasPrivateKey) throw Invalid("TLS 证书", "TLS certificate");
            }
        }
        catch (System.Security.Cryptography.CryptographicException) { throw Invalid("TLS 证书", "TLS certificate"); }
        return normalized + "\n";
    }

    private static InvalidDataException Invalid(string chinese, string english) => new(L10n.T(
        $"VPS 配置中的{chinese}无效，请检查连接配置。",
        $"The VPS profile contains an invalid {english}. Check the connection profile."));
}
