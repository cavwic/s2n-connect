using S2N.Models;

namespace S2N.Services;

public static class SingBoxOutboundBuilder
{
    public static Dictionary<string, object> Build(VpsProfile profile, string tag = "vless-out", string resolver = "lan-dns")
    {
        profile = VpsProfileValidator.Normalize(profile);
        var outbound = new Dictionary<string, object>
        {
            ["type"] = profile.Transport, ["tag"] = tag, ["server"] = profile.ServerIp,
            ["server_port"] = profile.ServerPort, ["domain_resolver"] = resolver
        };
        if (profile.Transport == "hysteria2")
        {
            var tls = new Dictionary<string, object>
            {
                ["enabled"] = true, ["server_name"] = profile.ServerName, ["insecure"] = false
            };
            if (profile.CertificatePem.Length > 0)
                tls["certificate"] = profile.CertificatePem.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            outbound["password"] = profile.Password;
            outbound["tls"] = tls;
        }
        else
        {
            outbound["uuid"] = profile.Uuid;
            outbound["flow"] = "xtls-rprx-vision";
            outbound["tls"] = new
            {
                enabled = true, server_name = profile.ServerName,
                utls = new { enabled = true, fingerprint = "chrome" },
                reality = new { enabled = true, public_key = profile.PublicKey, short_id = profile.ShortId }
            };
        }
        return outbound;
    }
}
