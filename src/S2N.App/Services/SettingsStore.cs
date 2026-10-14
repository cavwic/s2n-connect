using System.Text.Json;
using S2N.Models;

namespace S2N.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return CreateDefaults();
        }

        await using var stream = File.OpenRead(AppPaths.SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream, Options, cancellationToken) ?? CreateDefaults();

        Normalize(settings);
        return settings;
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        Normalize(settings);
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temp = AppPaths.SettingsPath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(
                stream, settings, Options, cancellationToken);
        }

        File.Move(temp, AppPaths.SettingsPath, true);
    }

    public async Task<VpsProfile?> LoadVpsProfileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<VpsProfile>(
            stream, Options, cancellationToken);
    }

    public async Task SaveVpsProfileAsync(
        VpsProfile profile,
        string path,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream, profile, Options, cancellationToken);
    }

    private static AppSettings CreateDefaults() =>
        new()
        {
            ManagedConfigPath = AppPaths.ManagedConfigPath,
            VpsProfilePath = AppPaths.VpsProfilePath
        };

    private static void Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ManagedConfigPath))
            settings.ManagedConfigPath = AppPaths.ManagedConfigPath;
        if (string.IsNullOrWhiteSpace(settings.VpsProfilePath))
            settings.VpsProfilePath = AppPaths.VpsProfilePath;

        settings.ThemeMode = (settings.ThemeMode ?? "").Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

        if (!settings.HasSuccessfulConnection)
            settings.AutoConnect = false;

        settings.ProcessNames ??= [];
        settings.DomainSuffixes ??= [];
        settings.KnownHostFingerprints ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
