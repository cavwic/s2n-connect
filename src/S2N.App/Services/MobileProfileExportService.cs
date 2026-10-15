using System.Text.Json;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class MobileProfileExportService
{
    public string Serialize(VpsProfile? profile)
    {
        if (profile is null)
            throw new InvalidDataException(
                L10n.T("请先部署 VPS，再导出手机配置。", "Deploy the VPS before exporting a mobile profile."));

        var server = (profile.ServerIp ?? "").Trim();
        var serverName = (profile.ServerName ?? "").Trim();
        var publicKey = (profile.PublicKey ?? "").Trim();
        var shortId = (profile.ShortId ?? "").Trim();

        if (Uri.CheckHostName(server) == UriHostNameType.Unknown
            || server.Length == 0)
            throw InvalidField("服务器地址", "server address");
        if (profile.ServerPort is < 1 or > 65535)
            throw InvalidField("服务器端口", "server port");
        if (!Guid.TryParse(profile.Uuid, out var uuid) || uuid == Guid.Empty)
            throw InvalidField("UUID", "UUID");
        if (publicKey.Length != 43
            || !publicKey.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            throw InvalidField("REALITY 公钥", "REALITY public key");
        if (shortId.Length > 16
            || shortId.Length % 2 != 0
            || !shortId.All(Uri.IsHexDigit))
            throw InvalidField("REALITY short ID", "REALITY short ID");
        if (serverName.Length == 0
            || Uri.CheckHostName(serverName) == UriHostNameType.Unknown)
            throw InvalidField("TLS 服务器名称", "TLS server name");

        // Explicitly allow only client connection fields. Never serialize
        // AppSettings or add SSH credentials to the mobile export.
        var exported = new
        {
            serverIp = server,
            serverPort = profile.ServerPort,
            uuid = uuid.ToString(),
            publicKey,
            shortId = shortId.ToLowerInvariant(),
            serverName,
            updatedAt = profile.UpdatedAt
        };
        return JsonSerializer.Serialize(
            exported,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static InvalidDataException InvalidField(string chinese, string english) =>
        new(L10n.T(
            $"VPS 配置中的{chinese}无效，请检查配置或重新部署。",
            $"The VPS profile contains an invalid {english}. Check the profile or redeploy."));
}
