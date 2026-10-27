using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using S2N.Localization;

namespace S2N.Services;

public static class TunLifecycle
{
    // sing-tun derives the Windows adapter GUID from its interface name. A fresh
    // name avoids the stale-name/GUID collision without removing other adapters.
    // https://github.com/SagerNet/sing-tun/blob/bca4bf029237/tun_windows.go
    public static string? NewInterfaceName(bool isWindows, bool isMacOS = false) =>
        isWindows ? "S2N-" + Guid.NewGuid().ToString("N")[..16]
        : isMacOS ? SelectMacInterfaceName(NetworkInterface.GetAllNetworkInterfaces().Select(x => x.Name))
        : null;

    internal static string SelectMacInterfaceName(IEnumerable<string> existing)
    {
        var occupied = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(128, 3968).Select(index => "utun" + index)
            .FirstOrDefault(name => !occupied.Contains(name))
            ?? throw new InvalidOperationException(L10n.T(
                "没有可用的 TUN 网卡名称。", "No TUN interface name is available."));
    }

    public static int SelectAvailableProxyPort(int requestedPort)
    {
        if (requestedPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(requestedPort));
        try
        {
            var requested = new TcpListener(IPAddress.Loopback, requestedPort);
            try { requested.Start(); return requestedPort; }
            finally { requested.Stop(); }
        }
        catch (SocketException)
        {
            var available = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                available.Start();
                return ((IPEndPoint)available.LocalEndpoint).Port;
            }
            finally { available.Stop(); }
        }
    }

    internal static TunStartupOptions ReadOptions(string configPath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(configPath));
        var inbounds = json.RootElement.GetProperty("inbounds");
        var tun = inbounds.EnumerateArray().Single(x =>
            x.GetProperty("type").GetString() == "tun");
        var health = inbounds.EnumerateArray().Single(x =>
            x.TryGetProperty("tag", out var tag) && tag.GetString() == "health-in");
        var listen = health.GetProperty("listen").GetString();
        if (!IPAddress.TryParse(listen, out var address) || !IPAddress.IsLoopback(address))
            throw new InvalidDataException(L10n.T(
                "S2N 本地代理只能监听回环地址。", "The S2N proxy must listen on a loopback address."));
        var port = health.GetProperty("listen_port").GetInt32();
        if (port is < 1 or > 65535) throw new InvalidDataException(L10n.T(
            "本地代理端口无效。", "The local proxy port is invalid."));
        var name = tun.TryGetProperty("interface_name", out var value) ? value.GetString() : null;
        var log = json.RootElement.GetProperty("log").GetProperty("output").GetString();
        return new TunStartupOptions(address, port, name ?? "", log ?? "");
    }

    internal static bool IsInterfaceReady(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName)) return false;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(x =>
                string.Equals(x.Name, interfaceName, StringComparison.OrdinalIgnoreCase)
                && x.OperationalStatus == OperationalStatus.Up);
        }
        catch { return false; }
    }

    internal static async Task<bool> IsListenerReadyAsync(
        IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        try { await client.ConnectAsync(address, port, timeout.Token); return true; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (SocketException) { return false; }
    }

    internal static long LogLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    internal static string ReadNewLog(string path, long offset)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (file.Length < offset) offset = 0;
            file.Position = Math.Max(offset, file.Length - 4096);
            using var reader = new StreamReader(file, Encoding.UTF8);
            return reader.ReadToEnd().Trim();
        }
        catch { return ""; }
    }

    public static async Task WaitForReadyAsync(
        Func<CancellationToken, Task<TunStartupSnapshot>> snapshot,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<TimeSpan>? elapsed = null)
    {
        var watch = Stopwatch.StartNew();
        elapsed ??= () => watch.Elapsed;
        delay ??= Task.Delay;
        var consecutiveReady = 0;
        while (elapsed() < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await snapshot(cancellationToken);
            if (!state.ProcessRunning)
                throw new InvalidOperationException(L10n.T(
                    "sing-box 启动失败。", "sing-box failed to start.")
                    + (string.IsNullOrWhiteSpace(state.Error) ? "" : Environment.NewLine + state.Error));
            consecutiveReady = state.ListenerReady && state.InterfaceReady
                ? consecutiveReady + 1 : 0;
            // A listener alone can appear before the TUN adapter has initialized.
            if (consecutiveReady >= 4) return;
            await delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        throw new TimeoutException(L10n.T(
            "TUN 网卡启动超时，请查看连接日志。",
            "The TUN adapter did not become ready. Check the connection log."));
    }
}

public sealed record TunStartupSnapshot(
    bool ProcessRunning, bool ListenerReady, bool InterfaceReady, string Error = "");

internal sealed record TunStartupOptions(
    IPAddress ListenerAddress, int ListenerPort, string InterfaceName, string LogPath);

public sealed record OwnedCoreProcess(
    int Pid, long StartTimeUtcTicks, string ExecutablePath, string ConfigPath, bool Ready = false,
    string LauncherExecutablePath = "", string InterfaceName = "", string InterfaceGuid = "", string PnpDeviceId = "",
    bool AdapterAbsentBeforeLaunch = false, bool PendingAdapterIdentityRecovered = false)
{
    public bool Matches(int pid, long startTimeUtcTicks, string executablePath, string configPath, bool isWindows) =>
        Pid == pid && StartTimeUtcTicks == startTimeUtcTicks
        && SamePath(ExecutablePath, executablePath, isWindows)
        && SamePath(ConfigPath, configPath, isWindows);

    internal bool MatchesLiveProcess(int pid, long started, string actualPath, bool isWindows) =>
        Pid == pid && StartTimeUtcTicks == started
        && (SamePath(ExecutablePath, actualPath, isWindows)
            || (!Ready && !string.IsNullOrWhiteSpace(LauncherExecutablePath)
                && SamePath(LauncherExecutablePath, actualPath, isWindows)));

    internal static bool SamePath(string first, string second, bool isWindows)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch { return false; }
    }
}
