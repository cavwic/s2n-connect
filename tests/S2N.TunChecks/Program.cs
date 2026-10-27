using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using S2N.Services;

if (args is ["--fixture-child", var marker])
{
    await File.WriteAllTextAsync(marker, Environment.ProcessId.ToString());
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

if (args is ["--native-windows", var core, var resultDirectory])
{
    await NativeWindowsChecks.RunAsync(core, resultDirectory);
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "s2n-tun-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try { await RunChecksAsync(directory); }
catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
finally { Directory.Delete(directory, recursive: true); }

static async Task RunChecksAsync(string directory)
{
    var passed = 0;
    void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        passed++;
    }

    var names = Enumerable.Range(0, 500).Select(_ => TunLifecycle.NewInterfaceName(true)!).ToArray();
    Check(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 500,
        "each Windows connection avoids all previous name/GUID pairs on its first attempt");
    Check(names.All(x => x.StartsWith("S2N-") && x.Length == 20 && x.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')),
        "Windows names are short owned aliases, never shell commands or another VPN's alias");
    Check(names.All(x => x != "s2n-tun" && x != "CavwicS2N"), "legacy stale aliases are never reused");
    Check(TunLifecycle.NewInterfaceName(false) is null, "Unix interface selection remains unchanged");
    Check(TunLifecycle.SelectMacInterfaceName(["utun128", "utun129", "en0"]) == "utun130",
        "macOS selects a valid available utun alias without reusing another VPN interface");

    var occupied = new TcpListener(IPAddress.Loopback, 0);
    occupied.Start();
    try
    {
        var requested = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var selected = TunLifecycle.SelectAvailableProxyPort(requested);
        Check(selected != requested && selected is > 0 and <= 65535, "foreign listener selects a free port without stopping it");
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, requested);
        using var accepted = await occupied.AcceptTcpClientAsync();
        Check(client.Connected, "existing private listener remains available");
        Check(TunLifecycle.SelectAvailableProxyPort(selected) == selected, "available requested port is retained");
    }
    finally { occupied.Stop(); }

    var calls = 0;
    await TunLifecycle.WaitForReadyAsync(_ => Task.FromResult(++calls switch
    {
        1 => new TunStartupSnapshot(true, false, false),
        2 => new TunStartupSnapshot(true, true, false),
        _ => new TunStartupSnapshot(true, true, true)
    }), TimeSpan.FromSeconds(1), delay: (_, _) => Task.CompletedTask);
    Check(calls == 6, "first start waits for TUN and stable listener, not just a running process");

    calls = 0;
    await TunLifecycle.WaitForReadyAsync(_ => Task.FromResult(++calls switch
    {
        1 or 2 or 3 => new TunStartupSnapshot(true, true, true),
        4 => new TunStartupSnapshot(true, false, true),
        _ => new TunStartupSnapshot(true, true, true)
    }), TimeSpan.FromSeconds(1), delay: (_, _) => Task.CompletedTask);
    Check(calls == 8, "transient listener readiness does not finish startup");

    foreach (var error in new[]
    {
        "create adapter: Cannot create a file when that file already exists. | open existing adapter: Element not found.",
        "authentication failed", "invalid configuration", "TLS certificate verification failed"
    })
    {
        calls = 0;
        try
        {
            await TunLifecycle.WaitForReadyAsync(_ =>
            {
                calls++;
                return Task.FromResult(new TunStartupSnapshot(false, false, false, error));
            }, TimeSpan.FromSeconds(1), delay: (_, _) => Task.CompletedTask);
            throw new InvalidOperationException("An exited core was accepted.");
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains(error) && calls == 1,
                "startup failure is reported once, without retrying unrelated failures: " + error.Split(':')[0]);
        }
    }

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        calls = 0;
        try
        {
            await TunLifecycle.WaitForReadyAsync(_ =>
            {
                calls++;
                return Task.FromResult(new TunStartupSnapshot(true, false, false));
            }, TimeSpan.FromSeconds(1), cancelled.Token);
            throw new InvalidOperationException("Cancelled startup was accepted.");
        }
        catch (OperationCanceledException) { Check(calls == 0, "cancelled startup creates no additional attempt"); }
    }

    var elapsed = TimeSpan.Zero;
    calls = 0;
    try
    {
        await TunLifecycle.WaitForReadyAsync(_ =>
        {
            calls++;
            elapsed += TimeSpan.FromSeconds(1);
            return Task.FromResult(new TunStartupSnapshot(true, true, false));
        }, TimeSpan.FromSeconds(3), delay: (_, _) => Task.CompletedTask, elapsed: () => elapsed);
        throw new InvalidOperationException("Missing TUN was accepted.");
    }
    catch (TimeoutException) { Check(calls == 3, "missing TUN has a bounded startup deadline"); }

    var executable = Path.Combine(directory, "sing-box.exe");
    var config = Path.Combine(directory, "config.json");
    var record = new OwnedCoreProcess(123, 456, executable, config, Ready: true);
    Check(record.Matches(123, 456, executable, config, true), "matching owned core identity is accepted");
    Check(!record.Matches(123, 457, executable, config, true), "recycled PID is rejected");
    Check(!record.Matches(123, 456, Path.Combine(directory, "foreign.exe"), config, true), "foreign executable is rejected");
    Check(!record.Matches(123, 456, executable, Path.Combine(directory, "private-config.json"), true), "same binary with another config is rejected");
    Check(!record.Matches(124, 456, executable, config, true), "different core PID is rejected");

    using var self = Process.GetCurrentProcess();
    var selfPath = ProcessLocator.TryGetExecutablePath(self)!;
    var state = Path.Combine(directory, "sing-box.pid");
    var launcher = new PrivilegeLauncher(directory);
    await File.WriteAllTextAsync(state, self.Id.ToString());
    Check(!launcher.IsRunning(selfPath, config) && !launcher.HasOwnedProcess(selfPath, config),
        "a live legacy integer PID is never adopted as a ready owned connection");
    try
    {
        launcher.EnsureNoLegacyConnection(selfPath);
        throw new InvalidOperationException("A live legacy same-binary connection was not blocked.");
    }
    catch (InvalidOperationException exception)
    {
        Check(exception.Message.Contains("previous version"),
            "a live legacy connection requires the old version to stop before a new TUN can start");
    }
    await launcher.StopAsync(selfPath, configPath: config);
    Check(!self.HasExited && (await File.ReadAllTextAsync(state)).Trim() == self.Id.ToString(),
        "stopping the new version never terminates or rewrites an ambiguous legacy PID");
    launcher.EnsureNoLegacyConnection(Path.Combine(directory, "different-core.exe"));
    Check(!self.HasExited, "a reused legacy PID from a clearly different executable is ignored");
    Check(!launcher.HasOwnedProcess("", config) && !launcher.HasOwnedProcess(selfPath, "")
        && !launcher.IsRunning("", config), "empty ownership arguments are safe and never match a running core");
    if (OperatingSystem.IsWindows())
    {
        using var lockedState = new FileStream(state, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            launcher.EnsureNoLegacyConnection(selfPath);
            throw new InvalidOperationException("An unreadable connection record was ignored.");
        }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains("connection record"),
                "an unreadable legacy record blocks a new connection instead of hiding a live previous TUN");
        }
    }
    await File.WriteAllTextAsync(state, "not-a-record");
    launcher.EnsureNoLegacyConnection(selfPath);
    Check(!launcher.HasOwnedProcess(selfPath, config), "invalid legacy state is not adopted");
    await File.WriteAllTextAsync(state, JsonSerializer.Serialize(new OwnedCoreProcess(
        self.Id, self.StartTime.ToUniversalTime().Ticks + 1, selfPath, config, Ready: true)));
    Check(!launcher.IsRunning(selfPath, config), "launcher ignores a reused saved PID");
    await launcher.StopAsync(selfPath, configPath: config);
    Check(!self.HasExited, "stale PID cleanup never terminates an unrelated process");
    await File.WriteAllTextAsync(state, JsonSerializer.Serialize(new OwnedCoreProcess(
        self.Id, self.StartTime.ToUniversalTime().Ticks, selfPath, config, Ready: true)));
    Check(launcher.IsRunning(selfPath, config), "saved PID/path/start identity can be resumed");
    await launcher.StopAsync(selfPath, configPath: config + ".other");
    Check(!self.HasExited && File.Exists(state), "different requested config cannot stop the saved core");
    File.Delete(state);
    await launcher.StopAsync(selfPath, configPath: config);
    Check(!self.HasExited, "missing ownership record never enumerates and kills same-binary processes");

    await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
    {
        log = new { output = Path.Combine(directory, "core.log") },
        inbounds = new object[]
        {
            new { type = "mixed", tag = "health-in", listen = "127.0.0.1", listen_port = 2086 },
            new { type = "tun", interface_name = names[0] }
        }
    }));
    var options = TunLifecycle.ReadOptions(config);
    Check(options.ListenerPort == 2086 && options.InterfaceName == names[0], "readiness follows configured health port and TUN alias");
    var log = Path.Combine(directory, "core.log");
    await File.WriteAllTextAsync(log, "old startup failure\n");
    var oldLength = TunLifecycle.LogLength(log);
    await File.AppendAllTextAsync(log, "current startup failure\n");
    Check(TunLifecycle.ReadNewLog(log, oldLength) == "current startup failure", "old log errors cannot be attributed to a new startup");

    var marker = Path.Combine(directory, "child.pid");
    var childArguments = new List<string>();
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        childArguments.Add(Assembly.GetExecutingAssembly().Location);
    childArguments.AddRange(["--fixture-child", marker]);
    using var cancellation = new CancellationTokenSource();
    var pendingChild = ProcessRunner.RunAsync(Environment.ProcessPath!, childArguments,
        cancellationToken: cancellation.Token);
    var childStartup = Stopwatch.StartNew();
    while (!File.Exists(marker) && !pendingChild.IsCompleted && childStartup.Elapsed < TimeSpan.FromSeconds(10))
        await Task.Delay(20);
    cancellation.Cancel();
    try
    {
        await pendingChild;
        throw new InvalidOperationException("Cancellation did not stop the fixture child.");
    }
    catch (OperationCanceledException)
    {
        Check(File.Exists(marker), "owned fixture child started before cancellation");
        var childPid = int.Parse(await File.ReadAllTextAsync(marker));
        var alive = false;
        try { using var child = Process.GetProcessById(childPid); alive = !child.HasExited; }
        catch (ArgumentException) { }
        Check(!alive, "cancelled process check leaves no child process running");
    }

    Console.WriteLine($"PASS: {passed} TUN lifecycle and process ownership checks; no adapters or production services were changed.");
}
