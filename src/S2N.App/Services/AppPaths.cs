using System.Runtime.InteropServices;

namespace S2N.Services;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var isolatedDirectory = Environment.GetEnvironmentVariable("S2N_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(isolatedDirectory))
            {
                if (!Path.IsPathFullyQualified(isolatedDirectory))
                    throw new InvalidDataException("S2N_DATA_DIRECTORY must be an absolute path.");
                var fullDirectory = Path.GetFullPath(isolatedDirectory);
                Directory.CreateDirectory(fullDirectory);
                return fullDirectory;
            }
            string path;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "S2N");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "S2N");
            }
            else
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                path = string.IsNullOrWhiteSpace(xdg)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config", "s2n")
                    : Path.Combine(xdg, "s2n");
            }

            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string VpsProfilePath => Path.Combine(DataDirectory, "vps-profile.json");
    public static string ManagedConfigPath => Path.Combine(DataDirectory, "config.json");
    public static string LogDirectory
    {
        get
        {
            var path = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
