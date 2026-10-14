using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

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

    internal static string? TryGetExecutablePath(Process process)
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
            try
            {
                // Limited query access also works for an elevated sing-box
                // launched by the normal, non-elevated desktop application.
                using var handle = OpenProcess(0x1000, false, process.Id);
                if (handle.IsInvalid) return null;
                var path = new StringBuilder(32768);
                var size = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref size)
                    ? path.ToString()
                    : null;
            }
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder path,
        ref int size);
}
