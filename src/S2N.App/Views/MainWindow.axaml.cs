using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using S2N.Models;
using S2N.Services;

namespace S2N.Views;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly NetworkProbeService _networkProbe = new();
    private readonly SshService _sshService = new();
    private readonly SingBoxInstaller _singBoxInstaller = new();
    private readonly SingBoxConfigService _configService = new();
    private readonly PrivilegeLauncher _privilegeLauncher = new();
    private readonly ExistingInstallDiscoveryService _installDiscovery;

    private AppSettings _settings = new();
    private VpsProfile? _profile;
    private SingBoxManager? _singBoxManager;
    private string _candidateSshKeyPath = "";
    private string _keyPassphrase = "";
    private readonly DispatcherTimer _statusTimer;
    private int _refreshInProgress;

    public bool IsExiting { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        _installDiscovery = new ExistingInstallDiscoveryService(_singBoxInstaller);

        _singBoxManager = new SingBoxManager(
            _singBoxInstaller,
            _configService,
            _privilegeLauncher,
            _networkProbe);

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusTimer.Tick += async (_, _) => await RefreshStateAsync();

        Opened += async (_, _) =>
        {
            await InitializeAsync();
            _statusTimer.Start();
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty
                && WindowState == WindowState.Minimized)
                Hide();
        };
    }

    public Task ConnectFromTrayAsync() => ConnectInternalAsync();
    public Task StopFromTrayAsync() => StopInternalAsync();

    public async Task ExitAsync()
    {
        IsExiting = true;
        _statusTimer.Stop();
        await StopInternalAsync();
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes
                .IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        VpsHostBox.Text = _settings.VpsHost;
        VpsUserBox.Text = _settings.VpsUser;
        VpsPortBox.Value = _settings.VpsPort;
        SshKeyBox.Text = _settings.SshKeyPath;
        SingBoxPathBox.Text = _settings.SingBoxInstallDirectory;

        var discovered = await _installDiscovery.DiscoverAsync(_settings);
        if (!string.IsNullOrWhiteSpace(discovered)
            && !string.Equals(discovered, _settings.SingBoxExecutablePath,
                PlatformInfo.IsWindows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            _settings.SingBoxExecutablePath = discovered;
            _settings.SingBoxInstallDirectory = Path.GetDirectoryName(discovered) ?? "";
            SingBoxPathBox.Text = _settings.SingBoxInstallDirectory;
            await _settingsStore.SaveAsync(_settings);
            MessageText.Text = "已识别现有 sing-box 安装。";
        }
        _profile = await _settingsStore.LoadVpsProfileAsync(
            _settings.VpsProfilePath);
        await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        // A slow network probe can last longer than the five-second timer.
        // Skip overlapping refreshes instead of stacking concurrent probes.
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
            return;

        try
        {
            LocalIpText.Text =
                await _networkProbe.GetLocalIpLocationAsync();

            var installed = _singBoxManager!.IsInstalled(_settings);
            var running = _singBoxManager.IsRunning(_settings);

            // Connection state is process-first:
            // 1. No sing-box process => disconnected, with no Google probe.
            // 2. sing-box process exists => probe Google through the managed
            //    local proxy before showing connected.
            if (!running)
            {
                ConnectionText.Text = "未连接";
                ExitIpText.Text = "未连接";
            }
            else
            {
                var proxy =
                    await _networkProbe.ProbeManagedConnectionAsync();

                if (proxy.Connected)
                {
                    ConnectionText.Text = "已连接";
                    ExitIpText.Text = proxy.ExitInfo;
                }
                else
                {
                    ConnectionText.Text =
                        "未连接（sing-box 已启动，外网检测失败）";
                    ExitIpText.Text = "检测失败";
                }
            }

            SingBoxStatusText.Text = installed ? "已部署" : "未部署";

            if (_profile is not null)
            {
                var portOpen = await _networkProbe.IsTcpPortOpenAsync(
                    _profile.ServerIp,
                    _profile.ServerPort,
                    TimeSpan.FromSeconds(2));
                VpsStatusText.Text = portOpen
                    ? "已部署"
                    : "已配置，但 TCP 443 当前不可达";
            }
            else
            {
                VpsStatusText.Text = "未部署";
            }

            // Button availability depends only on the real process state.
            // A slow or failed Google probe must never disable Stop.
            ConnectButton.IsEnabled =
                installed && _profile is not null && !running;
            StopButton.IsEnabled = running;
        }
        catch (Exception ex)
        {
            MessageText.Text = ex.Message;
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);
        }
    }

    private async void ChooseSshKey_Click(
        object? sender,
        RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "选择 SSH 私钥",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("SSH private keys")
                    {
                        Patterns =
                        [
                            "*.pem",
                            "*.key",
                            "id_rsa",
                            "id_ed25519",
                            "*"
                        ]
                    }
                ]
            });

        var selected = files.FirstOrDefault();
        var path = selected?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            SshService.ValidatePrivateKey(path);
            _candidateSshKeyPath = path;
            SshKeyBox.Text = path + "（待验证）";
            MessageText.Text =
                "已选择私钥。只有 VPS 连接成功后才会保存该路径。";
        }
        catch (Exception ex)
        {
            MessageText.Text = "导入失败：" + ex.Message;
        }
    }

    private async void TestVps_Click(
        object? sender,
        RoutedEventArgs e) =>
        await TestVpsInternalAsync();

    private async Task<bool> TestVpsInternalAsync()
    {
        try
        {
            _settings.VpsHost = (VpsHostBox.Text ?? "").Trim();
            _settings.VpsUser = string.IsNullOrWhiteSpace(VpsUserBox.Text)
                ? "root"
                : VpsUserBox.Text.Trim();
            _settings.VpsPort = (int)(VpsPortBox.Value ?? 22);
            _keyPassphrase = KeyPassphraseBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(_settings.VpsHost))
                throw new InvalidOperationException("请输入 VPS 地址。");

            var keyPath = !string.IsNullOrWhiteSpace(_candidateSshKeyPath)
                ? _candidateSshKeyPath
                : _settings.SshKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
                throw new InvalidOperationException("请选择 SSH 私钥。");

            var result = await _sshService.TestConnectionAsync(
                _settings,
                keyPath,
                _keyPassphrase,
                ConfirmFingerprint);

            if (!result.Success)
                throw new InvalidOperationException(result.Error);

            var hostKey =
                $"{_settings.VpsHost}:{_settings.VpsPort}";
            _settings.KnownHostFingerprints[hostKey] =
                result.Fingerprint;

            _settings.SshKeyPath = keyPath;
            _candidateSshKeyPath = "";
            SshKeyBox.Text = keyPath;
            await _settingsStore.SaveAsync(_settings);

            MessageText.Text =
                "VPS 连接成功：" + result.ServerInfo
                    .Replace(Environment.NewLine, " / ");
            return true;
        }
        catch (Exception ex)
        {
            MessageText.Text = "VPS 连接失败：" + ex.Message;
            return false;
        }
    }

    private bool ConfirmFingerprint(string fingerprint)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(
            () => new FingerprintWindow(fingerprint)
                .ShowDialog<bool>(this));

        var dialogTask = operation.GetAwaiter().GetResult();
        return dialogTask.GetAwaiter().GetResult();
    }

    private async void InstallSingBox_Click(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择 sing-box 安装目录",
                    AllowMultiple = false
                });

            var selected = folders.FirstOrDefault();
            var directory = selected?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(directory)) return;

            ProgressBar.IsVisible = true;
            ProgressBar.Value = 0;
            var progress = new Progress<double>(
                value => ProgressBar.Value = value * 100);

            var executable =
                await _singBoxManager!.InstallAsync(
                    directory,
                    progress);

            _settings.SingBoxInstallDirectory = directory;
            _settings.SingBoxExecutablePath = executable;
            SingBoxPathBox.Text = directory;
            await _settingsStore.SaveAsync(_settings);

            MessageText.Text =
                "sing-box 安装完成，下载包和临时目录已删除。";
        }
        catch (Exception ex)
        {
            MessageText.Text =
                "sing-box 安装失败：" + ex.Message;
        }
        finally
        {
            ProgressBar.IsVisible = false;
            await RefreshStateAsync();
        }
    }

    private async void OneClickDeploy_Click(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!_singBoxManager!.IsInstalled(_settings))
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "选择 sing-box 安装目录",
                        AllowMultiple = false
                    });
                var directory = folders.FirstOrDefault()?.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(directory)) return;

                ProgressBar.IsVisible = true;
                ProgressBar.Value = 0;
                var progress = new Progress<double>(
                    value => ProgressBar.Value = value * 100);
                var executable = await _singBoxManager.InstallAsync(
                    directory, progress);
                _settings.SingBoxInstallDirectory = directory;
                _settings.SingBoxExecutablePath = executable;
                SingBoxPathBox.Text = directory;
                await _settingsStore.SaveAsync(_settings);
            }

            var keyPath = !string.IsNullOrWhiteSpace(_candidateSshKeyPath)
                ? _candidateSshKeyPath
                : _settings.SshKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                MessageText.Text = "请先选择 SSH 私钥，然后再次点击一键部署。";
                return;
            }

            if (!await TestVpsInternalAsync()) return;
            var service = new VpsDeploymentService(
                _sshService, _settingsStore);
            _profile = await service.DeployAsync(
                _settings, _keyPassphrase);
            await _settingsStore.SaveAsync(_settings);
            MessageText.Text = "VPS 与本地 sing-box 已完成部署。";
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageText.Text = "一键部署失败：" + ex.Message;
        }
        finally
        {
            ProgressBar.IsVisible = false;
        }
    }

    private async void DeployVps_Click(
        object? sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!await TestVpsInternalAsync()) return;

            var service = new VpsDeploymentService(
                _sshService,
                _settingsStore);
            _profile = await service.DeployAsync(
                _settings,
                _keyPassphrase);

            MessageText.Text = "VPS 部署完成。";
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageText.Text = "VPS 部署失败：" + ex.Message;
        }
    }

    private async void Connect_Click(
        object? sender,
        RoutedEventArgs e) =>
        await ConnectInternalAsync();

    private async Task ConnectInternalAsync()
    {
        try
        {
            _profile ??= await _settingsStore.LoadVpsProfileAsync(
                _settings.VpsProfilePath);
            if (_profile is null)
                throw new InvalidOperationException("请先部署 VPS。");

            await _singBoxManager!.ConnectAsync(
                _settings,
                _profile);
            await Task.Delay(800);
            MessageText.Text = "连接命令已执行。";
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageText.Text = "连接失败：" + ex.Message;
        }
    }

    private async void Stop_Click(
        object? sender,
        RoutedEventArgs e) =>
        await StopInternalAsync();

    private async Task StopInternalAsync()
    {
        try
        {
            await _singBoxManager!.StopAsync(_settings);
            MessageText.Text = "已停止。";
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            MessageText.Text = "停止失败：" + ex.Message;
        }
    }

    private async void Rules_Click(
        object? sender,
        RoutedEventArgs e)
    {
        var dialog = new RulesWindow(
            _settings.ProcessNames,
            _settings.DomainSuffixes);
        var result = await dialog.ShowDialog<bool>(this);
        if (!result) return;

        _settings.ProcessNames = dialog.ProcessNames;
        _settings.DomainSuffixes = dialog.DomainSuffixes;
        await _settingsStore.SaveAsync(_settings);

        if (_singBoxManager!.IsRunning(_settings))
        {
            await StopInternalAsync();
            await ConnectInternalAsync();
        }

        MessageText.Text = "规则已保存。";
    }

    private async void Refresh_Click(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshStateAsync();

    private async void Exit_Click(
        object? sender,
        RoutedEventArgs e) =>
        await ExitAsync();
}
