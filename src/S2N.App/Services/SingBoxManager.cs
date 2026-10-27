using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class SingBoxManager
{
    private readonly SingBoxInstaller _installer;
    private readonly SingBoxConfigService _configService;
    private readonly PrivilegeLauncher _launcher;
    private readonly NetworkProbeService _networkProbe;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public SingBoxManager(
        SingBoxInstaller installer,
        SingBoxConfigService configService,
        PrivilegeLauncher launcher,
        NetworkProbeService networkProbe)
    {
        _installer = installer;
        _configService = configService;
        _launcher = launcher;
        _networkProbe = networkProbe;
    }

    public bool IsInstalled(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.SingBoxExecutablePath)
        && File.Exists(settings.SingBoxExecutablePath);

    public bool IsRunning(AppSettings settings) =>
        IsInstalled(settings)
        && _launcher.IsRunning(settings.SingBoxExecutablePath, settings.ManagedConfigPath);

    public Task<string> InstallAsync(
        string installDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default) =>
        _installer.InstallLatestAsync(
            installDirectory,
            progress,
            cancellationToken);

    public async Task ConnectAsync(
        AppSettings settings,
        VpsProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInstalled(settings))
                throw new InvalidOperationException(
                    L10n.T("尚未安装 sing-box。", "sing-box is not installed."));

            // Never rewrite a live core's configuration or adopt another application's
            // process merely because both applications use the same sing-box binary.
            _launcher.EnsureNoLegacyConnection(settings.SingBoxExecutablePath);
            if (_launcher.HasOwnedProcess(settings.SingBoxExecutablePath, settings.ManagedConfigPath))
            {
                await _launcher.StartAsync(settings.SingBoxExecutablePath,
                    settings.ManagedConfigPath, cancellationToken);
                return;
            }

            settings.LocalProxyPort = TunLifecycle.SelectAvailableProxyPort(settings.LocalProxyPort);

            var localDns = _networkProbe.DetectLocalDns()
                ?? "192.168.1.1";

            await _configService.WriteAsync(
                settings,
                profile,
                localDns,
                cancellationToken,
                interfaceName: TunLifecycle.NewInterfaceName(PlatformInfo.IsWindows, PlatformInfo.IsMacOS));

            var check = await ProcessRunner.RunAsync(
                settings.SingBoxExecutablePath,
                ["check", "-c", settings.ManagedConfigPath],
                cancellationToken: cancellationToken);

            if (check.ExitCode != 0)
                throw new InvalidOperationException(
                    L10n.T(
                        "sing-box 配置检查失败：",
                        "sing-box configuration check failed: ")
                    + check.StdErr);

            await _launcher.StartAsync(
                settings.SingBoxExecutablePath,
                settings.ManagedConfigPath,
                cancellationToken);
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsInstalled(settings)) return;
            await _launcher.StopAsync(
                settings.SingBoxExecutablePath,
                cancellationToken,
                settings.ManagedConfigPath);
        }
        finally { _lifecycleGate.Release(); }
    }
}
