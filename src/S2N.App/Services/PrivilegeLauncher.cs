using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using S2N.Localization;

namespace S2N.Services;

public sealed class PrivilegeLauncher
{
    private readonly string _pidPath;
    private readonly Dictionary<int, Process> _launchHandles = new();
    private readonly Action<OwnedCoreProcess>? _afterLaunch;

    public PrivilegeLauncher(string? stateDirectory = null) : this(stateDirectory, null) { }

    internal PrivilegeLauncher(string? stateDirectory, Action<OwnedCoreProcess>? afterLaunch)
    {
        _pidPath = Path.Combine(stateDirectory ?? AppPaths.DataDirectory, "sing-box.pid");
        _afterLaunch = afterLaunch;
    }

    public bool IsRunning(string executablePath, string configPath)
    {
        var record = ReadRecord(executablePath, configPath);
        if (record is not { Ready: true }) return false;
        using var process = FindOwnedProcess(record);
        return process is not null;
    }

    public bool HasOwnedProcess(string executablePath, string configPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(configPath)) return false;
        var record = ReadRecord(executablePath, configPath);
        if (record is null) return false;
        using var process = FindOwnedProcess(record);
        return process is not null;
    }

    public void EnsureNoLegacyConnection(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return;
        int pid;
        try
        {
            if (!int.TryParse(File.ReadAllText(_pidPath).Trim(), out pid) || pid <= 0) return;
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(L10n.T(
                "无法核验旧连接记录，未启动新连接。请先停止旧版连接，再重试。",
                "The previous connection record could not be checked. Stop the previous connection before trying again."), exception);
        }

        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }
        using (process)
        {
            try { if (process.HasExited) return; }
            catch (Win32Exception) { }
            var actualPath = ProcessLocator.TryGetExecutablePath(process);
            if (actualPath is not null
                && !OwnedCoreProcess.SamePath(actualPath, executablePath, PlatformInfo.IsWindows)) return;
            // An integer PID cannot identify the configuration or creation time.
            // Do not adopt it, stop it, or overwrite a potentially live TUN config.
            throw new InvalidOperationException(L10n.T(
                "旧版连接可能仍在运行。请先在旧版 S2N 中停止连接，再使用新版连接。",
                "A connection from the previous version may still be running. Stop it in the previous S2N version before connecting here."));
        }
    }

    public async Task StartAsync(
        string executablePath,
        string configPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoLegacyConnection(executablePath);
        var options = TunLifecycle.ReadOptions(configPath);
        var record = ReadRecord(executablePath, configPath);
        using var previous = record is null ? null : FindOwnedProcess(record);
        if (previous is not null && record!.Ready) return;
        var offset = TunLifecycle.LogLength(options.LogPath);
        if (previous is null)
        {
            // This check is repeated just before elevation, after the manager
            // selected a free port, to catch a concurrent foreign listener.
            if (await TunLifecycle.IsListenerReadyAsync(options.ListenerAddress,
                    options.ListenerPort, cancellationToken))
                throw new InvalidOperationException(L10n.T(
                    "本地代理端口已被其他程序占用，请重新连接。",
                    "Another application is using the local proxy port. Connect again."));
            record = await LaunchAsync(executablePath, configPath, cancellationToken);
        }

        try
        {
            await TunLifecycle.WaitForReadyAsync(async token =>
            {
                using var process = FindOwnedProcess(record!);
                if (process is null)
                    return new TunStartupSnapshot(false, false, false,
                        TunLifecycle.ReadNewLog(options.LogPath, offset));
                var listening = await TunLifecycle.IsListenerReadyAsync(
                    options.ListenerAddress, options.ListenerPort, token);
                return new TunStartupSnapshot(true, listening,
                    TunLifecycle.IsInterfaceReady(options.InterfaceName));
            }, TimeSpan.FromSeconds(45), cancellationToken);
            await SaveRecordAsync(record! with { Ready = true }, cancellationToken);
        }
        catch (Exception startupError)
        {
            try { await StopAsync(executablePath, CancellationToken.None, configPath); }
            catch (Exception cleanupError)
            {
                throw new InvalidOperationException(L10n.T(
                    "连接未就绪，停止本次启动的 sing-box 也失败，请点击停止后重试。",
                    "The connection did not become ready, and its sing-box process could not be stopped. Stop it before trying again."),
                    new AggregateException(startupError, cleanupError));
            }
            throw;
        }
    }

    private async Task<OwnedCoreProcess> LaunchAsync(
        string executablePath, string configPath, CancellationToken cancellationToken)
    {
        Process? process = null;
        if (PlatformInfo.IsWindows)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(configPath);

            process = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    L10n.T("无法启动 sing-box。", "Failed to start sing-box."));
        }
        else if (PlatformInfo.IsMacOS)
        {
            var shell =
                $"nohup {Quote(executablePath)} run -c {Quote(configPath)} "
                + $"> {Quote(Path.Combine(AppPaths.LogDirectory, "sing-box-console.log"))} "
                + "2>&1 & echo $!";
            var escaped = shell
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            var result = await ProcessRunner.RunAsync(
                "osascript",
                [
                    "-e",
                    $"do shell script \"{escaped}\" with administrator privileges"
                ],
                cancellationToken: CancellationToken.None);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(result.StdErr);
            if (!int.TryParse(result.StdOut.Trim(), out var pid))
                throw new InvalidOperationException(L10n.T(
                    "管理员启动操作未返回进程编号。", "The privileged launcher did not return a process ID."));
            process = Process.GetProcessById(pid);
        }
        else if (PlatformInfo.IsLinux)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pkexec",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            psi.ArgumentList.Add(executablePath);
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(configPath);

            process = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    L10n.T("无法启动 pkexec。", "Failed to start pkexec."));
            // Drain both pipes so even a failing pkexec/core cannot block on a
            // full stderr buffer. The core's configured log remains authoritative.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        else throw new PlatformNotSupportedException();

        var record = new OwnedCoreProcess(process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            Path.GetFullPath(executablePath), Path.GetFullPath(configPath),
            LauncherExecutablePath: ProcessLocator.TryGetExecutablePath(process) ?? "");
        _launchHandles[process.Id] = process;
        try
        {
            // Persist ownership even if the caller cancelled just after UAC;
            // the subsequent cancellation handler can then stop that exact PID.
            await SaveRecordAsync(record, CancellationToken.None);
            _afterLaunch?.Invoke(record);
            return record;
        }
        catch
        {
            await StopVerifiedProcessAsync(record, CancellationToken.None);
            if (_launchHandles.Remove(record.Pid, out var handle)) handle.Dispose();
            throw;
        }
    }

    public async Task StopAsync(
        string executablePath,
        CancellationToken cancellationToken = default,
        string? configPath = null)
    {
        var record = ReadRecord(executablePath, configPath);
        if (record is null) return;
        using var process = FindOwnedProcess(record);
        if (process is not null)
            await StopVerifiedProcessAsync(record, cancellationToken);
        if (_launchHandles.Remove(record.Pid, out var handle)) handle.Dispose();
        try { File.Delete(_pidPath); } catch { }
    }

    private static async Task StopVerifiedProcessAsync(
        OwnedCoreProcess record, CancellationToken cancellationToken)
    {
        using var process = FindOwnedProcess(record);
        if (process is null) return;
        if (PlatformInfo.IsWindows)
            await StopWindowsProcessAsync(record.Pid, record.ExecutablePath,
                record.StartTimeUtcTicks, cancellationToken);
        else if (PlatformInfo.IsMacOS)
        {
            var command = $"kill -TERM {record.Pid} || true";
            var escaped = command.Replace("\"", "\\\"");
            await ProcessRunner.RunAsync(
                "osascript",
                [
                    "-e",
                    $"do shell script \"{escaped}\" with administrator privileges"
                ],
                cancellationToken: cancellationToken);
        }
        else
        {
            var result = await ProcessRunner.RunAsync("pkexec",
                ["kill", "-TERM", record.Pid.ToString()], cancellationToken: cancellationToken);
            if (result.ExitCode != 0 && !process.HasExited)
                throw new InvalidOperationException(result.StdErr);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
    }

    private static async Task StopWindowsProcessAsync(
        int pid,
        string executablePath,
        long startTimeUtcTicks,
        CancellationToken cancellationToken)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }

        using (process)
        {
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTimeUtcTicks) return;

            var actualPath = ProcessLocator.TryGetExecutablePath(process);
            if (actualPath is null)
            {
                if (process.HasExited) return;
                throw new InvalidOperationException(
                    L10n.T(
                        "无法核验 sing-box 进程路径，未执行停止。",
                        "Unable to verify the sing-box process path; the process was not stopped."));
            }

            // A saved PID can be reused after sing-box exits. Never elevate
            // a termination request for a different executable.
            if (!string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(executablePath),
                    StringComparison.OrdinalIgnoreCase))
                return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
                {
                    var start = new ProcessStartInfo
                    {
                        FileName = Path.Combine(
                            Environment.SystemDirectory, "taskkill.exe"),
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    start.ArgumentList.Add("/PID");
                    start.ArgumentList.Add(pid.ToString());
                    start.ArgumentList.Add("/T");
                    start.ArgumentList.Add("/F");

                    using var terminator = Process.Start(start)
                        ?? throw new InvalidOperationException(
                            L10n.T(
                                "无法启动管理员停止操作。",
                                "Failed to start the administrator stop operation."));
                    await terminator.WaitForExitAsync(timeout.Token);
                    if (terminator.ExitCode != 0 && !process.HasExited)
                        throw new InvalidOperationException(
                            L10n.T(
                                "管理员停止操作失败，sing-box 仍在运行。",
                                "The administrator stop operation failed; sing-box is still running."));
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    return;
                }

                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    L10n.T(
                        "停止 sing-box 超时，请重试。",
                        "Stopping sing-box timed out. Try again."));
            }
        }
    }

    private OwnedCoreProcess? ReadRecord(string executablePath, string? configPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)
            || (configPath is not null && string.IsNullOrWhiteSpace(configPath))) return null;
        try
        {
            var text = File.ReadAllText(_pidPath).Trim();
            if (int.TryParse(text, out _)) return null;
            var record = JsonSerializer.Deserialize<OwnedCoreProcess>(text);
            if (record is null || record.Pid <= 0
                || string.IsNullOrWhiteSpace(record.ExecutablePath)
                || string.IsNullOrWhiteSpace(record.ConfigPath)
                || !OwnedCoreProcess.SamePath(record.ExecutablePath, executablePath, PlatformInfo.IsWindows)
                || (configPath is not null && !OwnedCoreProcess.SamePath(record.ConfigPath, configPath, PlatformInfo.IsWindows)))
                return null;
            return record;
        }
        catch { return null; }
    }

    private static Process? FindOwnedProcess(OwnedCoreProcess record)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(record.Pid);
            var actualPath = ProcessLocator.TryGetExecutablePath(process);
            if (!process.HasExited && actualPath is not null
                && record.MatchesLiveProcess(process.Id, process.StartTime.ToUniversalTime().Ticks,
                    actualPath, PlatformInfo.IsWindows)) return process;
        }
        catch { }
        process?.Dispose();
        return null;
    }

    private async Task SaveRecordAsync(OwnedCoreProcess record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_pidPath)!);
        var temporary = _pidPath + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record), cancellationToken);
            File.Move(temporary, _pidPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
