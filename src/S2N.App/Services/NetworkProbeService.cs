using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using S2N.Localization;

namespace S2N.Services;

public sealed class NetworkProbeService
{
    public bool IsNetworkAvailable() =>
        NetworkInterface.GetIsNetworkAvailable();

    private static readonly Uri IpIpUri = new("https://myip.ipip.net/");
    private static readonly Uri CloudflareTraceUri =
        new("https://www.cloudflare.com/cdn-cgi/trace");
    private static readonly Uri GoogleConnectivityUri =
        new("https://www.google.com/generate_204");

    // One second is too aggressive on variable residential and mobile links.
    // The UI only calls this probe after a sing-box process has been detected.
    private static readonly TimeSpan ManagedConnectTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ManagedRequestTimeout =
        TimeSpan.FromSeconds(10);

    public async Task<string> GetLocalIpLocationAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(5);
        try
        {
            var text = (await client.GetStringAsync(
                IpIpUri,
                cancellationToken)).Trim();
            return string.IsNullOrWhiteSpace(text) ? L10n.T("获取失败", "Failed to retrieve") : text;
        }
        catch
        {
            return L10n.T("获取失败", "Failed to retrieve");
        }
    }

    public async Task<(bool Connected, string ExitInfo)> ProbeManagedConnectionAsync(
        CancellationToken cancellationToken = default,
        int localProxyPort = 2080)
    {
        if (localProxyPort is < 1024 or > 65535)
            return (false, L10n.T("本地代理端口无效", "Invalid local proxy port"));
        var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{localProxyPort}"),
            UseProxy = true,
            ConnectTimeout = ManagedConnectTimeout
        };

        using var client = new HttpClient(handler)
        {
            Timeout = ManagedRequestTimeout
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("S2N-Connect", "0.4.0-beta.4"));

        // Google is the authoritative connectivity test requested by the UI.
        // Any successful HTTP response proves that the managed proxy path works.
        try
        {
            using var response = await client.GetAsync(
                GoogleConnectivityUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (false, L10n.T("未连接", "Disconnected"));
        }
        catch
        {
            return (false, L10n.T("未连接", "Disconnected"));
        }

        // Connectivity is already confirmed. Exit IP lookup is best-effort and
        // must never downgrade a working connection to disconnected.
        try
        {
            var text = await client.GetStringAsync(
                CloudflareTraceUri,
                cancellationToken);
            var ip = MatchTrace(text, "ip");
            var loc = MatchTrace(text, "loc");
            if (!string.IsNullOrWhiteSpace(ip))
                return (true,
                    string.IsNullOrWhiteSpace(loc)
                        ? ip
                        : $"{ip} / {loc}");
        }
        catch { }

        return (true, L10n.T(
            "已连接，出口 IP 检测失败",
            "Connected; exit IP check failed"));
    }

    public async Task<bool> IsTcpPortOpenAsync(
        string host, int port, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        using var tcp = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await tcp.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? DetectLocalDns()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;

            foreach (var address in nic.GetIPProperties().DnsAddresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address))
                    return address.ToString();
            }
        }

        return null;
    }

    private static HttpClient CreateClient(int timeoutSeconds)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("S2N-Connect", "0.4.0-beta.4"));
        return client;
    }

    private static string MatchTrace(string text, string key)
    {
        var match = Regex.Match(
            text,
            $"(?m)^{Regex.Escape(key)}=(.+)$",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}
