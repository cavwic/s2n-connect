using S2N.Models;

namespace S2N.Services;

public sealed class ExistingInstallDiscoveryService
{
    private readonly SingBoxInstaller _installer;

    public ExistingInstallDiscoveryService(SingBoxInstaller installer)
    {
        _installer = installer;
    }

    public async Task<string?> DiscoverAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.SingBoxExecutablePath))
            candidates.Add(settings.SingBoxExecutablePath);

        candidates.AddRange(ProcessLocator.FindRunningExecutablePaths());

        var pathCandidate = FindOnPath(PlatformInfo.ExecutableName);
        if (!string.IsNullOrWhiteSpace(pathCandidate))
            candidates.Add(pathCandidate);

        foreach (var commonPath in CommonInstallPaths())
            candidates.Add(commonPath);

        foreach (var candidate in candidates
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(PlatformInfo.IsWindows
                         ? StringComparer.OrdinalIgnoreCase
                         : StringComparer.Ordinal))
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                await _installer.ValidateAsync(candidate, cancellationToken);
                return Path.GetFullPath(candidate);
            }
            catch
            {
                // Continue looking. Discovery must never make startup fail.
            }
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executableName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }

    private static IEnumerable<string> CommonInstallPaths()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        if (PlatformInfo.IsWindows)
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "sing-box", "sing-box.exe");
            yield return Path.Combine(home, "Tools", "sing-box", "sing-box.exe");
            yield return Path.Combine(home, "scoop", "apps", "sing-box", "current", "sing-box.exe");
        }
        else if (PlatformInfo.IsMacOS)
        {
            yield return "/opt/homebrew/bin/sing-box";
            yield return "/usr/local/bin/sing-box";
            yield return Path.Combine(home, ".local", "bin", "sing-box");
        }
        else
        {
            yield return "/usr/bin/sing-box";
            yield return "/usr/local/bin/sing-box";
            yield return Path.Combine(home, ".local", "bin", "sing-box");
        }
    }
}
