namespace S2N.Localization
{
    public static class L10n
    {
        public static string T(string chinese, string english) => english;
    }
}

namespace S2N.Services
{
    public static class AppPaths
    {
        public static string DataDirectory =>
            throw new InvalidOperationException("TUN checks must use their isolated data directory.");
        public static string LogDirectory =>
            throw new InvalidOperationException("TUN checks must not access user logs.");
    }

    public static class PlatformInfo
    {
        public static bool IsWindows => OperatingSystem.IsWindows();
        public static bool IsLinux => OperatingSystem.IsLinux();
        public static bool IsMacOS => OperatingSystem.IsMacOS();
    }
}
