using S2N.Models;

namespace S2N.Services;

public sealed class SingBoxManager
{
    private readonly SingBoxInstaller _installer;
    private readonly SingBoxConfigService _configService;
    private readonly PrivilegeLauncher _launcher;
    private readonly NetworkProbeService _networkProbe;

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
        && ProcessLocator.IsRunning(
            settings.SingBoxExecutablePath);

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
        if (!IsInstalled(settings))
            throw new InvalidOperationException(
                "sing-box is not installed.");

        var localDns = _networkProbe.DetectLocalDns()
            ?? "192.168.1.1";

        await _configService.WriteAsync(
            settings,
            profile,
            localDns,
            cancellationToken);

        var check = await ProcessRunner.RunAsync(
            settings.SingBoxExecutablePath,
            ["check", "-c", settings.ManagedConfigPath],
            cancellationToken: cancellationToken);

        if (check.ExitCode != 0)
            throw new InvalidOperationException(
                "sing-box configuration check failed: "
                + check.StdErr);

        if (IsRunning(settings)) return;

        await _launcher.StartAsync(
            settings.SingBoxExecutablePath,
            settings.ManagedConfigPath,
            cancellationToken);
    }

    public Task StopAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled(settings)) return Task.CompletedTask;
        return _launcher.StopAsync(
            settings.SingBoxExecutablePath,
            cancellationToken);
    }
}
