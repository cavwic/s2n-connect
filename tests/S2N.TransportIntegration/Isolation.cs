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
        // Only referenced by WriteAsync, which this harness never invokes.
        public static string LogDirectory => Path.GetTempPath();
    }
}
