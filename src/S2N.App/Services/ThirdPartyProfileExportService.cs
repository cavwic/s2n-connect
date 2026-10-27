using System.Net;
using System.Text.Json;
using S2N.Models;

namespace S2N.Services;

/// <summary>Portable sing-box profile; contains no desktop settings, file paths or server private keys.</summary>
public sealed class ThirdPartyProfileExportService
{
    public string Serialize(VpsProfile profile)
    {
        profile = VpsProfileValidator.Normalize(profile);
        var tun = new Dictionary<string, object>
        {
            ["type"] = "tun", ["tag"] = "tun-in",
            ["address"] = new[] { "172.19.0.1/30", "fdfe:dcba:9876::1/126" },
            ["mtu"] = 1500, ["auto_route"] = true, ["stack"] = "mixed"
        };
        if (IPAddress.TryParse(profile.ServerIp, out var address))
            tun["route_exclude_address"] = new[] { profile.ServerIp + (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "/32" : "/128") };
        var configuration = new
        {
            log = new { level = "fatal" },
            inbounds = new[] { tun },
            outbounds = new object[]
            {
                new { type = "selector", tag = "proxy", outbounds = new[] { "S2N" }, @default = "S2N" },
                SingBoxOutboundBuilder.Build(profile, "S2N", "bootstrap-dns"),
                new { type = "direct", tag = "direct" }
            },
            dns = new
            {
                servers = new object[]
                {
                    new { type = "local", tag = "bootstrap-dns" },
                    new
                    {
                        type = "https", tag = "remote-doh", server = "1.1.1.1", server_port = 443,
                        path = "/dns-query", tls = new { enabled = true, server_name = "cloudflare-dns.com" },
                        detour = "proxy"
                    }
                },
                rules = new object[]
                {
                    new { domain_regex = new[] { "^[^.]+$" }, action = "route", server = "bootstrap-dns" },
                    new { domain_suffix = new[] { "local", "lan", "home.arpa", "in-addr.arpa", "ip6.arpa" }, action = "route", server = "bootstrap-dns" }
                },
                final = "remote-doh", strategy = "prefer_ipv4"
            },
            route = new
            {
                auto_detect_interface = true,
                default_domain_resolver = new { server = "remote-doh", strategy = "prefer_ipv4" },
                rules = new object[]
                {
                    new { action = "sniff" }, new { port = 53, action = "hijack-dns" },
                    new { ip_is_private = true, action = "route", outbound = "direct" }
                },
                final = "proxy"
            }
        };
        return JsonSerializer.Serialize(configuration, new JsonSerializerOptions { WriteIndented = true });
    }
}
