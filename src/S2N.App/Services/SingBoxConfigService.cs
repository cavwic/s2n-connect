using System.Net;
using System.Text.Json;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class SingBoxConfigService
{
    public async Task WriteAsync(
        AppSettings settings,
        VpsProfile profile,
        string localDns,
        CancellationToken cancellationToken = default)
    {
        var exclude = new List<string>
        {
            "10.0.0.0/8",
            "100.64.0.0/10",
            "127.0.0.0/8",
            "169.254.0.0/16",
            "172.16.0.0/12",
            "192.168.0.0/16",
            "224.0.0.0/4",
            "240.0.0.0/4"
        };

        if (IPAddress.TryParse(profile.ServerIp, out var serverIp))
            exclude.Add(
                serverIp.AddressFamily
                == System.Net.Sockets.AddressFamily.InterNetwork
                    ? profile.ServerIp + "/32"
                    : profile.ServerIp + "/128");

        var routeRules = new List<object>
        {
            new { action = "sniff" },
            new { port = 53, action = "hijack-dns" },
            new
            {
                ip_is_private = true,
                action = "route",
                outbound = "direct"
            },
            new
            {
                inbound = new[] { "health-in" },
                action = "route",
                outbound = "vless-out"
            }
        };
        var processNames = settings.ProcessNames
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (processNames.Length > 0)
            routeRules.Add(new
            {
                process_name = processNames,
                action = "route",
                outbound = "vless-out"
            });

        var domainSuffixes = settings.DomainSuffixes
            .Select(NormalizeDomain)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (domainSuffixes.Length > 0)
            routeRules.Add(new
            {
                domain_suffix = domainSuffixes,
                action = "route",
                outbound = "vless-out"
            });

        var config = new
        {
            log = new
            {
                level = "fatal",
                output = Path.Combine(
                    AppPaths.LogDirectory,
                    "sing-box.log")
            },
            inbounds = new object[]
            {
                new
                {
                    type = "mixed",
                    tag = "health-in",
                    listen = "127.0.0.1",
                    listen_port = 2080,
                    set_system_proxy = false
                },
                new
                {
                    type = "tun",
                    tag = "tun-in",
                    interface_name = "s2n-tun",
                    address = new[] { "198.18.0.1/30" },
                    mtu = 1500,
                    auto_route = true,
                    strict_route = false,
                    stack = "mixed",
                    route_exclude_address = exclude
                }
            },
            outbounds = new object[]
            {
                new
                {
                    type = "vless",
                    tag = "vless-out",
                    server = profile.ServerIp,
                    server_port = profile.ServerPort,
                    uuid = profile.Uuid,
                    flow = "xtls-rprx-vision",
                    tls = new
                    {
                        enabled = true,
                        server_name = profile.ServerName,
                        utls = new
                        {
                            enabled = true,
                            fingerprint = "chrome"
                        },
                        reality = new
                        {
                            enabled = true,
                            public_key = profile.PublicKey,
                            short_id = profile.ShortId
                        }
                    }
                },
                new { type = "direct", tag = "direct" }
            },
            dns = new
            {
                servers = new object[]
                {
                    new
                    {
                        type = "udp",
                        tag = "lan-dns",
                        server = localDns,
                        server_port = 53
                    },
                    new
                    {
                        type = "https",
                        tag = "remote-doh",
                        server = "1.1.1.1",
                        server_port = 443,
                        path = "/dns-query",
                        tls = new
                        {
                            enabled = true,
                            server_name = "cloudflare-dns.com"
                        },
                        detour = "vless-out"
                    }
                },
                rules = new object[]
                {
                    new
                    {
                        domain_regex = new[] { "^[^.]+$" },
                        action = "route",
                        server = "lan-dns"
                    },
                    new
                    {
                        domain_suffix = new[]
                        {
                            "local",
                            "lan",
                            "home.arpa",
                            "in-addr.arpa",
                            "ip6.arpa"
                        },
                        action = "route",
                        server = "lan-dns"
                    }
                },
                final = "remote-doh",
                strategy = "prefer_ipv4"
            },
            route = new
            {
                auto_detect_interface = true,
                default_domain_resolver = new
                {
                    server = "remote-doh",
                    strategy = "prefer_ipv4"
                },
                rules = routeRules,
                final = settings.GlobalMode ? "vless-out" : "direct"
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        Directory.CreateDirectory(
            Path.GetDirectoryName(settings.ManagedConfigPath)!);
        await File.WriteAllTextAsync(
            settings.ManagedConfigPath,
            JsonSerializer.Serialize(config, options),
            cancellationToken);
    }

    private static string NormalizeDomain(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            text = uri.Host;
        text = text.Trim('.');

        if (text.Contains('/') || text.Contains(':'))
            throw new InvalidDataException(
                L10n.T(
                    $"无效的域名规则：{value}",
                    $"Invalid domain rule: {value}"));
        return text;
    }
}
