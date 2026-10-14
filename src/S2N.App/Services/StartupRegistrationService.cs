using System.Reflection;
using System.Security;
using System.Text;
using S2N.Localization;

namespace S2N.Services;

public sealed class StartupRegistrationService
{
    private const string WindowsTaskName = "S2N Connect";
    private const string MacLabel = "com.s2n.connect";
    private const int StartupDelaySeconds = 60;

    public async Task<bool> IsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        if (PlatformInfo.IsWindows)
        {
            var result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                ["/Query", "/TN", WindowsTaskName],
                cancellationToken: cancellationToken);
            return result.ExitCode == 0;
        }

        if (PlatformInfo.IsMacOS)
            return File.Exists(GetMacPlistPath());

        if (PlatformInfo.IsLinux)
            return File.Exists(GetLinuxDesktopPath());

        return false;
    }

    public async Task SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (enabled)
            await RegisterAsync(cancellationToken);
        else
            await UnregisterAsync(cancellationToken);
    }

    private static async Task RegisterAsync(
        CancellationToken cancellationToken)
    {
        if (PlatformInfo.IsWindows)
        {
            var command = BuildWindowsLaunchCommand();
            var result = await ProcessRunner.RunAsync(
                "schtasks.exe",
                [
                    "/Create",
                    "/F",
                    "/TN", WindowsTaskName,
                    "/SC", "ONLOGON",
                    "/DELAY", "0001:00",
                    "/RL", "LIMITED",
                    "/TR", command
                ],
                cancellationToken: cancellationToken);

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    L10n.T(
                        "创建 Windows 开机启动任务失败：",
                        "Failed to create the Windows startup task: ")
                    + PreferError(result));
            return;
        }

        var scriptPath = await WriteUnixLaunchScriptAsync(
            cancellationToken);

        if (PlatformInfo.IsMacOS)
        {
            var path = GetMacPlistPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var plist = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{MacLabel}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>/bin/sh</string>
                        <string>{SecurityElement.Escape(scriptPath)}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>ProcessType</key>
                    <string>Interactive</string>
                </dict>
                </plist>
                """;
            await File.WriteAllTextAsync(
                path,
                plist,
                new UTF8Encoding(false),
                cancellationToken);
            return;
        }

        if (PlatformInfo.IsLinux)
        {
            var path = GetLinuxDesktopPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var desktop = $"""
                [Desktop Entry]
                Type=Application
                Version=1.0
                Name=S2N Connect
                Comment=Start S2N Connect after login
                Exec=/bin/sh {QuoteDesktopArgument(scriptPath)}
                Terminal=false
                Hidden=false
                X-GNOME-Autostart-enabled=true
                """;
            await File.WriteAllTextAsync(
                path,
                desktop,
                new UTF8Encoding(false),
                cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException();
    }

    private static async Task UnregisterAsync(
        CancellationToken cancellationToken)
    {
        if (PlatformInfo.IsWindows)
        {
            await ProcessRunner.RunAsync(
                "schtasks.exe",
                ["/Delete", "/F", "/TN", WindowsTaskName],
                cancellationToken: cancellationToken);
            return;
        }

        if (PlatformInfo.IsMacOS)
        {
            TryDelete(GetMacPlistPath());
            TryDelete(GetUnixScriptPath());
            return;
        }

        if (PlatformInfo.IsLinux)
        {
            TryDelete(GetLinuxDesktopPath());
            TryDelete(GetUnixScriptPath());
            return;
        }

        throw new PlatformNotSupportedException();
    }

    private static async Task<string> WriteUnixLaunchScriptAsync(
        CancellationToken cancellationToken)
    {
        var path = GetUnixScriptPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var command = BuildUnixLaunchCommand();
        var script = $"""
            #!/bin/sh
            sleep {StartupDelaySeconds}
            exec {command} --autostart
            """;

        await File.WriteAllTextAsync(
            path,
            script,
            new UTF8Encoding(false),
            cancellationToken);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static string BuildWindowsLaunchCommand()
    {
        var (host, managedEntry) = ResolveLaunchTarget();
        var builder = new StringBuilder();
        builder.Append(QuoteWindowsArgument(host));
        if (!string.IsNullOrWhiteSpace(managedEntry))
        {
            builder.Append(' ');
            builder.Append(QuoteWindowsArgument(managedEntry));
        }
        builder.Append(" --autostart");
        return builder.ToString();
    }

    private static string BuildUnixLaunchCommand()
    {
        var (host, managedEntry) = ResolveLaunchTarget();
        var builder = new StringBuilder();
        builder.Append(QuoteShellArgument(host));
        if (!string.IsNullOrWhiteSpace(managedEntry))
        {
            builder.Append(' ');
            builder.Append(QuoteShellArgument(managedEntry));
        }
        return builder.ToString();
    }

    private static (string Host, string? ManagedEntry) ResolveLaunchTarget()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException(
                L10n.T(
                    "无法确定 S2N 可执行文件路径。",
                    "Unable to determine the S2N executable path."));

        var isDotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(processPath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

        if (!isDotnetHost)
            return (processPath, null);

        var assemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new InvalidOperationException(
                L10n.T(
                    "无法确定 S2N 程序集路径。",
                    "Unable to determine the S2N assembly path."));

        return (processPath, assemblyPath);
    }

    private static string GetMacPlistPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "LaunchAgents",
            MacLabel + ".plist");

    private static string GetLinuxDesktopPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config")
            : xdg;
        return Path.Combine(root, "autostart", "s2n-connect.desktop");
    }

    private static string GetUnixScriptPath() =>
        Path.Combine(AppPaths.DataDirectory, "s2n-autostart.sh");

    private static string QuoteWindowsArgument(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private static string QuoteShellArgument(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";

    private static string QuoteDesktopArgument(string value) =>
        "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("$", "\\$")
            .Replace("`", "\\`")
        + "\"";

    private static string PreferError(ProcessResult result) =>
        string.IsNullOrWhiteSpace(result.StdErr)
            ? result.StdOut.Trim()
            : result.StdErr.Trim();

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
