using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using S2N.Services;

internal static class NativeWindowsChecks
{
    public static async Task RunAsync(string executablePath, string resultDirectory)
    {
        if (!OperatingSystem.IsWindows()
            || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || !IsUserAnAdmin())
            throw new InvalidOperationException("Native TUN checks are restricted to an elevated Windows GitHub Actions runner.");
        executablePath = Path.GetFullPath(executablePath);
        resultDirectory = Path.GetFullPath(resultDirectory);
        if (!File.Exists(executablePath) || Directory.Exists(resultDirectory))
            throw new InvalidOperationException("Supply a verified core and a new isolated fixture directory.");
        Directory.CreateDirectory(resultDirectory);
        const string legacyName = "s2n-tun";
        Require(!InterfaceExists(legacyName), "The runner already has an unrelated legacy interface.");
        var version = await ProcessRunner.RunAsync(executablePath, ["version"]);
        Require(version.ExitCode == 0, "Core version check failed.");
        var runs = new List<object>();
        var launcher = new PrivilegeLauncher(Path.Combine(resultDirectory, "owned-state"));
        var foreignPort = FreePort();
        var foreignConfig = await WriteConfigAsync(resultDirectory, "unrelated", legacyName, foreignPort, 254);
        Process? unrelated = null;
        var mainConfig = "";
        var createdNames = new List<string>();
        var completed = false;
        var reportJson = "";
        try
        {
            await CheckConfigAsync(executablePath, foreignConfig);
            var start = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "run", "-c", foreignConfig }) start.ArgumentList.Add(arg);
            unrelated = Process.Start(start) ?? throw new InvalidOperationException("Could not start the unrelated fixture core.");
            unrelated.OutputDataReceived += (_, _) => { };
            unrelated.ErrorDataReceived += (_, _) => { };
            unrelated.BeginOutputReadLine();
            unrelated.BeginErrorReadLine();
            await TunLifecycle.WaitForReadyAsync(async token => new TunStartupSnapshot(
                !unrelated.HasExited,
                await TunLifecycle.IsListenerReadyAsync(IPAddress.Loopback, foreignPort, token),
                TunLifecycle.IsInterfaceReady(legacyName)), TimeSpan.FromSeconds(45));
            Require(!launcher.IsRunning(executablePath, foreignConfig), "An unrelated same-binary core was adopted.");

            for (var index = 0; index < 3; index++)
            {
                var name = TunLifecycle.NewInterfaceName(true)!;
                Require(!InterfaceExists(name), "A new interface name was already occupied.");
                createdNames.Add(name);
                var port = TunLifecycle.SelectAvailableProxyPort(foreignPort);
                Require(port != foreignPort, "The unrelated listener was selected.");
                mainConfig = await WriteConfigAsync(resultDirectory, "owned-" + index, name, port, 255);
                await CheckConfigAsync(executablePath, mainConfig);
                var watch = Stopwatch.StartNew();
                await launcher.StartAsync(executablePath, mainConfig);
                watch.Stop();
                Require(launcher.IsRunning(executablePath, mainConfig), "The owned core was not ready after its first launch.");
                Require(TunLifecycle.IsInterfaceReady(name), "The owned TUN interface is not up.");
                Require(!unrelated.HasExited && TunLifecycle.IsInterfaceReady(legacyName), "The unrelated core or interface was disturbed.");
                var identity = JsonSerializer.Deserialize<OwnedCoreProcess>(await File.ReadAllTextAsync(
                    Path.Combine(resultDirectory, "owned-state", "sing-box.pid")))!;
                await launcher.StopAsync(executablePath, configPath: mainConfig);
                await WaitForRemovalAsync(name, port, identity.Pid);
                runs.Add(new { Run = index + 1, Attempts = 1, Interface = name,
                    Port = port, Pid = identity.Pid, StartupMs = watch.ElapsedMilliseconds,
                    FirstLaunchReady = true, OwnInterfaceRemoved = true, OwnListenerRemoved = true,
                    OwnProcessStopped = true, UnrelatedCorePreserved = !unrelated.HasExited });
                Require(!unrelated.HasExited && await TunLifecycle.IsListenerReadyAsync(
                    IPAddress.Loopback, foreignPort, CancellationToken.None), "Stopping S2N interrupted the unrelated core.");
            }

            // Cancel after the production launcher has created and recorded a
            // real privileged process, before it can report readiness.
            using var cancellation = new CancellationTokenSource();
            OwnedCoreProcess? cancelledIdentity = null;
            var cancelledLauncher = new PrivilegeLauncher(Path.Combine(resultDirectory, "cancel-state"), identity =>
            {
                cancelledIdentity = identity;
                cancellation.Cancel();
            });
            var cancelledName = TunLifecycle.NewInterfaceName(true)!;
            createdNames.Add(cancelledName);
            var cancelledPort = FreePort();
            var cancelledConfig = await WriteConfigAsync(resultDirectory, "cancelled", cancelledName, cancelledPort, 255);
            await CheckConfigAsync(executablePath, cancelledConfig);
            try
            {
                await cancelledLauncher.StartAsync(executablePath, cancelledConfig, cancellation.Token);
                throw new InvalidOperationException("A cancelled privileged start was reported as ready.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Require(cancelledIdentity is not null, "The cancellation fixture never created its core process.");
            await WaitForRemovalAsync(cancelledName, cancelledPort, cancelledIdentity!.Pid);
            Require(!unrelated.HasExited && TunLifecycle.IsInterfaceReady(legacyName), "Cancellation disturbed the unrelated fixture.");
            Require(createdNames.Distinct().Count() == createdNames.Count, "A previous name/GUID was reused.");
            completed = true;
            var report = new
            {
                Result = "PASS", Platform = "Windows", ProductionLauncher = true,
                DirectOnly = true, AutoRoute = false, StrictRoute = false,
                DnsMode = "disabled",
                RealVpsAccess = false, GenericRetries = 0, FirstLaunchRuns = runs,
                CancellationAfterProcessCreation = true, CancellationCleanup = true,
                UnrelatedSameBinaryCorePreserved = true,
                CoreSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executablePath))).ToLowerInvariant(),
                CoreVersion = version.StdOut.Trim(), CompletedUtc = DateTimeOffset.UtcNow
            };
            reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        }
        finally
        {
            if (!string.IsNullOrEmpty(mainConfig))
                await launcher.StopAsync(executablePath, configPath: mainConfig);
            if (unrelated is not null)
            {
                if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
                await unrelated.WaitForExitAsync();
                var pid = unrelated.Id;
                unrelated.Dispose();
                await WaitForRemovalAsync(legacyName, foreignPort, pid);
            }
            if (completed) Console.WriteLine("Native fixture cleanup completed; no production VPN was used.");
        }
        await File.WriteAllTextAsync(Path.Combine(resultDirectory, "native-tun-result.json"), reportJson);
        Console.WriteLine(reportJson);
    }

    private static async Task<string> WriteConfigAsync(string directory, string label, string name, int port, int subnet)
    {
        var path = Path.Combine(directory, label + ".json");
        var config = new
        {
            log = new { level = "fatal", output = Path.Combine(directory, label + ".log") },
            inbounds = new object[]
            {
                new { type = "mixed", tag = "health-in", listen = "127.0.0.1", listen_port = port, set_system_proxy = false },
                new { type = "tun", tag = "tun-in", interface_name = name,
                    address = new[] { $"198.19.{subnet}.1/30" }, mtu = 1500,
                    auto_route = false, strict_route = false, dns_mode = "disabled", stack = "mixed" }
            },
            outbounds = new[] { new { type = "direct", tag = "direct" } },
            route = new { auto_detect_interface = true, final = "direct" }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static async Task CheckConfigAsync(string executable, string path)
    {
        var result = await ProcessRunner.RunAsync(executable, ["check", "-c", path]);
        Require(result.ExitCode == 0, "Fixture config failed the official core check: " + result.StdErr);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static bool InterfaceExists(string name) => NetworkInterface.GetAllNetworkInterfaces()
        .Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    private static async Task WaitForRemovalAsync(string name, int port, int pid)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            var running = false;
            try { using var process = Process.GetProcessById(pid); running = !process.HasExited; }
            catch (ArgumentException) { }
            if (!running && !InterfaceExists(name)
                && !await TunLifecycle.IsListenerReadyAsync(IPAddress.Loopback, port, CancellationToken.None)) return;
            await Task.Delay(200);
        }
        throw new InvalidOperationException("The owned native fixture was not fully removed: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsUserAnAdmin();
}
