namespace S2N.Localization
{
    public static class L10n
    {
        public static string T(string chinese, string english) => english;
    }
}

namespace S2N.Services
{
    public static class PlatformInfo
    {
        public static string OsToken => "windows";
        public static string ArchitectureToken => "amd64";
        public static string ExecutableName => "sing-box.exe";
    }

    public static class AppPaths
    {
        public static string LogDirectory =>
            throw new InvalidOperationException("Release checks must not access application data.");
    }

    public static class ProcessRunner
    {
        public static Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Release checks must not run downloaded executables.");
    }

    public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
