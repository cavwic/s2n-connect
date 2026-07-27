using System.Diagnostics;

namespace S2N.Services;

public static class ProcessLocator
{
    public static IReadOnlyList<int> FindMatchingProcessIds(
        string executablePath)
    {
        var result = new List<int>();
        var expected = Path.GetFullPath(executablePath);

        foreach (var process in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                var actual = TryGetExecutablePath(process);
                if (actual is not null
                    && string.Equals(
                        Path.GetFullPath(actual),
                        expected,
                        PlatformInfo.IsWindows
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                    result.Add(process.Id);
            }
            catch { }
            finally { process.Dispose(); }
        }

        return result;
    }

    public static bool IsRunning(string executablePath) =>
        FindMatchingProcessIds(executablePath).Count > 0;

    public static IReadOnlyList<string> FindRunningExecutablePaths()
    {
        var result = new List<string>();
        foreach (var process in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                var path = TryGetExecutablePath(process);
                if (!string.IsNullOrWhiteSpace(path))
                    result.Add(path);
            }
            catch { }
            finally { process.Dispose(); }
        }

        return result.Distinct(
            PlatformInfo.IsWindows
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal).ToArray();
    }

    private static string? TryGetExecutablePath(Process process)
    {
        if (PlatformInfo.IsLinux)
        {
            try
            {
                return File.ResolveLinkTarget(
                    $"/proc/{process.Id}/exe",
                    true)?.FullName;
            }
            catch { return null; }
        }

        if (PlatformInfo.IsWindows)
        {
            try { return process.MainModule?.FileName; }
            catch { return null; }
        }

        if (PlatformInfo.IsMacOS)
        {
            try
            {
                var output = ProcessRunner.RunAsync(
                    "/bin/ps",
                    ["-p", process.Id.ToString(), "-o", "comm="])
                    .GetAwaiter().GetResult();
                return output.StdOut.Trim();
            }
            catch { return null; }
        }

        return null;
    }
}
