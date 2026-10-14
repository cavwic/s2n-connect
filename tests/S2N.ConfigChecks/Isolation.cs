namespace S2N.Services
{
    // Replace production AppPaths so checks never read user AppData or credentials.
    public static class AppPaths
    {
        public static string DataDirectory { get; } =
            Directory.CreateTempSubdirectory("S2N.ConfigChecks-").FullName;
        public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
        public static string ManagedConfigPath => Path.Combine(DataDirectory, "config.json");
        public static string VpsProfilePath => Path.Combine(DataDirectory, "vps-profile.json");
        public static string LogDirectory => Path.Combine(DataDirectory, "logs");

        public static void Cleanup()
        {
            // Only remove this run's generated directory, within the OS temp root.
            var directory = Path.GetFullPath(DataDirectory);
            var tempRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(directory).StartsWith(
                    "S2N.ConfigChecks-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected test directory.");
            }

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

namespace S2N.Localization
{
    public static class L10n
    {
        public static string T(string chinese, string english) => english;
    }
}
