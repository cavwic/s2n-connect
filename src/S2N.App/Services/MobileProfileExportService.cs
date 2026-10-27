using System.Text.Json;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class MobileProfileExportService
{
    public string Serialize(VpsProfile? profile)
    {
        if (profile is null)
            throw new InvalidDataException(L10n.T(
                "请先部署 VPS 或导入连接配置，再导出手机配置。",
                "Deploy the VPS or import a connection profile before exporting a mobile profile."));
        profile = VpsProfileValidator.Normalize(profile);
        // Connection credentials only; never SSH keys, VPS private keys or local settings.
        object exported = profile.Transport == "hysteria2"
            ? new
            {
                schemaVersion = 2, transport = "hysteria2",
                serverIp = profile.ServerIp, serverPort = profile.ServerPort,
                password = profile.Password, serverName = profile.ServerName,
                certificatePem = profile.CertificatePem, updatedAt = profile.UpdatedAt
            }
            : new
            {
                serverIp = profile.ServerIp, serverPort = profile.ServerPort,
                uuid = profile.Uuid, publicKey = profile.PublicKey,
                shortId = profile.ShortId, serverName = profile.ServerName,
                updatedAt = profile.UpdatedAt
            };
        return JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ExportConnectionUri(VpsProfile profile)
    {
        profile = VpsProfileValidator.Normalize(profile);
        var host = profile.ServerIp.Contains(':') ? $"[{profile.ServerIp}]" : profile.ServerIp;
        if (profile.Transport == "hysteria2")
        {
            // A URI cannot carry a trusted CA certificate. Never substitute insecure=1.
            if (profile.CertificatePem.Length > 0)
                throw new InvalidOperationException(L10n.T(
                    "此连接使用专属证书，请导出 JSON 配置并在支持自定义证书的客户端中导入。",
                    "This connection uses a custom certificate. Export JSON and use a client that supports custom certificate trust."));
            return $"hysteria2://{Uri.EscapeDataString(profile.Password)}@{host}:{profile.ServerPort}/?sni={Uri.EscapeDataString(profile.ServerName)}#S2N";
        }
        return $"vless://{profile.Uuid}@{host}:{profile.ServerPort}?security=reality&type=tcp&flow=xtls-rprx-vision&pbk={Uri.EscapeDataString(profile.PublicKey)}&sid={profile.ShortId}&sni={Uri.EscapeDataString(profile.ServerName)}#S2N";
    }
}
