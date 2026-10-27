using System.Text.Json;
using S2N.Models;

namespace S2N.Services;

public sealed class SettingsStore
{
    private static readonly SemaphoreSlim SettingsWriteGate = new(1, 1);
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
        await SettingsWriteGate.WaitAsync(cancellationToken);
        try
        {
            Normalize(settings);
            Directory.CreateDirectory(AppPaths.DataDirectory);
            var temporary = AppPaths.SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, AppPaths.SettingsPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { SettingsWriteGate.Release(); }
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
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await JsonSerializer.SerializeAsync(stream, profile, Options, cancellationToken);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
        settings.PreferredTransport = string.Equals(settings.PreferredTransport,
            "vless", StringComparison.OrdinalIgnoreCase) ? "vless" : "hysteria2";
        if (settings.LocalProxyPort is < 1024 or > 65535)
            settings.LocalProxyPort = 2080;
        if (settings.SshProxyPort is < 0 or > 65535
            || settings.SshProxyPort is > 0 and < 1024)
            settings.SshProxyPort = 0;

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
