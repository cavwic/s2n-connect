using System.Diagnostics;

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
                    "Failed to start sing-box.");
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
                    "Failed to start pkexec.");
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
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                }
                catch { }
            }
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

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
