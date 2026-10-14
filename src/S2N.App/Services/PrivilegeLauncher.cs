using System.Diagnostics;
using System.ComponentModel;
using S2N.Localization;

namespace S2N.Services;

public sealed class PrivilegeLauncher
{
    private readonly string _pidPath =
        Path.Combine(AppPaths.DataDirectory, "sing-box.pid");

    public async Task StartAsync(
        string executablePath,
        string configPath,
        CancellationToken cancellationToken = default)
    {
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

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    L10n.T("无法启动 sing-box。", "Failed to start sing-box."));
            await File.WriteAllTextAsync(
                _pidPath,
                process.Id.ToString(),
                cancellationToken);
            return;
        }

        if (PlatformInfo.IsMacOS)
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
                cancellationToken: cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(result.StdErr);
            await File.WriteAllTextAsync(
                _pidPath,
                result.StdOut.Trim(),
                cancellationToken);
            return;
        }

        if (PlatformInfo.IsLinux)
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

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException(
                    L10n.T("无法启动 pkexec。", "Failed to start pkexec."));
            await File.WriteAllTextAsync(
                _pidPath,
                process.Id.ToString(),
                cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException();
    }

    public async Task StopAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var pids = ProcessLocator.FindMatchingProcessIds(
            executablePath).ToHashSet();

        if (File.Exists(_pidPath)
            && int.TryParse(
                await File.ReadAllTextAsync(
                    _pidPath,
                    cancellationToken),
                out var savedPid))
            pids.Add(savedPid);

        if (PlatformInfo.IsWindows)
        {
            foreach (var pid in pids)
                await StopWindowsProcessAsync(
                    pid, executablePath, cancellationToken);
        }
        else if (PlatformInfo.IsMacOS)
        {
            foreach (var pid in pids)
            {
                var command = $"kill -TERM {pid} || true";
                var escaped = command.Replace("\"", "\\\"");
                await ProcessRunner.RunAsync(
                    "osascript",
                    [
                        "-e",
                        $"do shell script \"{escaped}\" with administrator privileges"
                    ],
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            foreach (var pid in pids)
            {
                try
                {
                    await ProcessRunner.RunAsync(
                        "pkexec",
                        ["kill", "-TERM", pid.ToString()],
                        cancellationToken: cancellationToken);
                }
                catch { }
            }
        }

        try { File.Delete(_pidPath); } catch { }
    }

    private static async Task StopWindowsProcessAsync(
        int pid,
        string executablePath,
        CancellationToken cancellationToken)
    {
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; }

        using (process)
        {
            if (process.HasExited) return;

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

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
