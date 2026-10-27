using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Text;
using S2N.Localization;
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
    private readonly StartupRegistrationService _startupRegistration = new();
    private readonly ExistingInstallDiscoveryService _installDiscovery;

    private AppSettings _settings = new();
    private VpsProfile? _profile;
    private SingBoxManager? _singBoxManager;
    private string _candidateSshKeyPath = "";
    private string _keyPassphrase = "";
    private readonly DispatcherTimer _statusTimer;
    private int _refreshInProgress;
    private int _autoConnectInProgress;
    private int _connectionMutationInProgress;
    private bool _loadingPreferences;
    private bool _initialized;

    public bool IsExiting { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        L10n.Apply(this);

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
            if (_initialized) return;
            _initialized = true;

            try
            {
                await InitializeAsync();
                _statusTimer.Start();

                if (_settings.AutoConnect
                    && _settings.HasSuccessfulConnection)
                {
                    await AutoConnectAfterReadinessAsync();
                }
            }
            catch
            {
                _initialized = false;
                throw;
            }
        };

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty
                && WindowState == WindowState.Minimized)
                Hide();
        };
    }

    public async Task ConnectFromTrayAsync() =>
        await ConnectInternalAsync();

    public Task StopFromTrayAsync() => StopInternalAsync();

    public async Task ExitAsync()
    {
        if (!await StopInternalAsync(showResultDialog: false)) return;
        IsExiting = true;
        _statusTimer.Stop();
        if (Application.Current is App app) app.DisposeTrayIcon();
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes
                .IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();

        _loadingPreferences = true;
        try
        {
            ApplyTheme(_settings.ThemeMode);
            SelectThemeMode(_settings.ThemeMode);
            GlobalModeCheckBox.IsChecked = _settings.GlobalMode;

            try
            {
                _settings.AutoStart =
                    await _startupRegistration.IsEnabledAsync();
            }
            catch
            {
                // Keep the saved value when the operating system cannot be queried.
            }

            AutoStartCheckBox.IsChecked = _settings.AutoStart;
            AutoConnectCheckBox.IsEnabled =
                _settings.HasSuccessfulConnection;
            AutoConnectCheckBox.IsChecked =
                _settings.HasSuccessfulConnection
                && _settings.AutoConnect;
        }
        finally
        {
            _loadingPreferences = false;
        }

        VpsHostBox.Text = _settings.VpsHost;
        VpsUserBox.Text = _settings.VpsUser;
        VpsPortBox.Text = _settings.VpsPort.ToString();
        SshKeyBox.Text = _settings.SshKeyPath;
        SingBoxPathBox.Text = _settings.SingBoxInstallDirectory;
        SshProxyPortBox.Text = _settings.SshProxyPort.ToString();

        var discovered = await _installDiscovery.DiscoverAsync(_settings);
        if (!string.IsNullOrWhiteSpace(discovered)
            && !string.Equals(discovered, _settings.SingBoxExecutablePath,
                PlatformInfo.IsWindows
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            _settings.SingBoxExecutablePath = discovered;
            _settings.SingBoxInstallDirectory =
                Path.GetDirectoryName(discovered) ?? "";
            SingBoxPathBox.Text = _settings.SingBoxInstallDirectory;
        }

        _profile = await _settingsStore.LoadVpsProfileAsync(
            _settings.VpsProfilePath);
        if (_profile is not null)
            _settings.PreferredTransport = _profile.Transport;
        _loadingPreferences = true;
        try
        {
            TransportBox.SelectedIndex = _settings.PreferredTransport == "vless" ? 1 : 0;
        }
        finally { _loadingPreferences = false; }
        await _settingsStore.SaveAsync(_settings);
        await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
            return;

        try
        {
            var installed = _singBoxManager!.IsInstalled(_settings);
            var running = _singBoxManager.IsRunning(_settings);
            ConnectButton.IsEnabled =
                installed && _profile is not null && !running;
            StopButton.IsEnabled = running;

            LocalIpText.Text =
                await _networkProbe.GetLocalIpLocationAsync();

            if (!running)
            {
                ConnectionText.Text = L10n.T("未连接", "Disconnected");
                ExitIpText.Text = L10n.T("未连接", "Disconnected");
            }
            else
            {
                var proxy =
                    await _networkProbe.ProbeManagedConnectionAsync(localProxyPort: _settings.LocalProxyPort);

                if (proxy.Connected)
                {
                    ConnectionText.Text = L10n.T("已连接", "Connected");
                    ExitIpText.Text = proxy.ExitInfo;
                    await MarkConnectionSucceededAsync();
                }
                else
                {
                    ConnectionText.Text =
                        L10n.T(
                            "未连接（sing-box 已启动，外网检测失败）",
                            "Disconnected (sing-box is running, but the external connectivity check failed)");
                    ExitIpText.Text = L10n.T("检测失败", "Check failed");
                }
            }

            SingBoxStatusText.Text = installed
                ? L10n.T("已部署", "Deployed")
                : L10n.T("未部署", "Not deployed");

            if (_profile is not null)
            {
                if (_profile.Transport == "hysteria2")
                    VpsStatusText.Text = running && ConnectionText.Text == L10n.T("已连接", "Connected")
                        ? L10n.T("UDP 通道已验证", "UDP connection verified")
                        : L10n.T("UDP 已配置，连接后验证", "UDP configured; connect to verify");
                else
                {
                    var portOpen = await _networkProbe.IsTcpPortOpenAsync(
                        _profile.ServerIp, _profile.ServerPort, TimeSpan.FromSeconds(3));
                    VpsStatusText.Text = portOpen ? L10n.T("TCP 端口可达", "TCP port reachable")
                        : L10n.T("TCP 端口暂不可达", "TCP port currently unreachable");
                }
            }
            else
            {
                VpsStatusText.Text = L10n.T("未部署", "Not deployed");
            }

            ConnectButton.IsEnabled =
                installed && _profile is not null
                && !_singBoxManager.IsRunning(_settings);
            StopButton.IsEnabled = _singBoxManager.IsRunning(_settings);
            AutoConnectCheckBox.IsEnabled =
                _settings.HasSuccessfulConnection;
        }
        catch
        {
            ConnectionText.Text =
                L10n.T("状态检测失败", "Status check failed");
            ExitIpText.Text = L10n.T("检测失败", "Check failed");
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
                Title = L10n.T("选择 SSH 私钥", "Select SSH private key"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(
                        L10n.T("SSH 私钥", "SSH private keys"))
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
            SshKeyBox.Text = path + L10n.T("（待验证）", " (pending validation)");
            await ShowInfoAsync(
                L10n.T(
                    "已选择私钥。只有 VPS 连接成功后才会保存该路径。",
                    "Private key selected. The path is saved only after a successful VPS connection."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T("导入失败：", "Import failed: ") + ex.Message);
        }
    }

    private async void TestVps_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (Volatile.Read(ref _connectionMutationInProgress) == 0)
            await TestVpsInternalAsync();
    }

    private async Task<bool> TestVpsInternalAsync(
        bool showSuccessDialog = true)
    {
        try
        {
            _settings.VpsHost = (VpsHostBox.Text ?? "").Trim();
            _settings.VpsUser = string.IsNullOrWhiteSpace(VpsUserBox.Text)
                ? "root"
                : VpsUserBox.Text.Trim();
            var portText = (VpsPortBox.Text ?? "").Trim();

            if (!int.TryParse(portText, out var vpsPort) ||
                vpsPort < 1 ||
                vpsPort > 65535)
            {
                throw new InvalidOperationException(
                    L10n.T(
                        "SSH 端口必须是 1 到 65535 之间的整数。",
                        "The SSH port must be an integer between 1 and 65535."));
            }

            _settings.VpsPort = vpsPort;
            if (!int.TryParse(SshProxyPortBox.Text, out var sshProxyPort)
                || sshProxyPort is < 0 or > 65535 || sshProxyPort is > 0 and < 1024)
                throw new InvalidOperationException(L10n.T(
                    "SSH 代理端口应为 0 或 1024–65535。", "Use 0 or a port from 1024 to 65535 for the SSH proxy."));
            _settings.SshProxyPort = sshProxyPort;
            _keyPassphrase = KeyPassphraseBox.Text ?? "";
            if (string.IsNullOrWhiteSpace(_settings.VpsHost))
                throw new InvalidOperationException(
                    L10n.T("请输入 VPS 地址。", "Enter the VPS address."));

            var keyPath = !string.IsNullOrWhiteSpace(_candidateSshKeyPath)
                ? _candidateSshKeyPath
                : _settings.SshKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
                throw new InvalidOperationException(
                    L10n.T("请选择 SSH 私钥。", "Select an SSH private key."));

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

            if (showSuccessDialog)
            {
                await ShowSuccessAsync(
                    L10n.T("SSH 管理连接成功：", "SSH management connection succeeded: ")
                    + result.ServerInfo.Replace(Environment.NewLine, " / "));
            }

            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T("SSH 管理连接失败：", "SSH management connection failed: ")
                + ex.Message);
            return false;
        }
    }

    private bool ConfirmFingerprint(string fingerprint)
    {
        var operation = Dispatcher.UIThread.InvokeAsync(
            () => new FingerprintWindow(fingerprint)
                .ShowDialog<bool>(this));

        return operation.GetAwaiter().GetResult();
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
                    Title = L10n.T(
                        "选择 sing-box 安装目录",
                        "Select the sing-box installation directory"),
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

            await ShowSuccessAsync(
                L10n.T(
                    "sing-box 安装完成，下载包和临时目录已删除。",
                    "sing-box installation completed. The download package and temporary directory were deleted."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T(
                    "sing-box 安装失败：",
                    "sing-box installation failed: ")
                + ex.Message);
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
        if (Interlocked.CompareExchange(ref _connectionMutationInProgress, 1, 0) != 0) return;
        try
        {
            if (_privilegeLauncher.HasOwnedProcess(_settings.SingBoxExecutablePath, _settings.ManagedConfigPath))
                throw new InvalidOperationException(L10n.T("请先停止当前连接。", "Stop the current connection first."));
            if (!_singBoxManager!.IsInstalled(_settings))
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = L10n.T(
                            "选择 sing-box 安装目录",
                            "Select the sing-box installation directory"),
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
                await ShowWarningAsync(
                    L10n.T(
                        "请先选择 SSH 私钥，然后再次点击一键部署。",
                        "Select an SSH private key, then click One-click deploy again."));
                return;
            }

            if (!await TestVpsInternalAsync(showSuccessDialog: false)) return;
            var service = new VpsDeploymentService(
                _sshService, _settingsStore);
            _profile = await service.DeployAsync(
                _settings, _keyPassphrase);
            await _settingsStore.SaveAsync(_settings);
            await ShowSuccessAsync(
                L10n.T(
                    "VPS 与本地 sing-box 已完成部署。",
                    "The VPS and local sing-box deployment is complete."));
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T(
                    "一键部署失败：",
                    "One-click deployment failed: ")
                + ex.Message);
        }
        finally
        {
            ProgressBar.IsVisible = false;
            Volatile.Write(ref _connectionMutationInProgress, 0);
        }
    }

    private async void DeployVps_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _connectionMutationInProgress, 1, 0) != 0) return;
        try
        {
            if (_privilegeLauncher.HasOwnedProcess(_settings.SingBoxExecutablePath, _settings.ManagedConfigPath))
                throw new InvalidOperationException(L10n.T("请先停止当前连接。", "Stop the current connection first."));
            if (!await TestVpsInternalAsync(showSuccessDialog: false)) return;

            var service = new VpsDeploymentService(
                _sshService,
                _settingsStore);
            _profile = await service.DeployAsync(
                _settings,
                _keyPassphrase);

            await ShowSuccessAsync(
                L10n.T("VPS 部署完成。", "VPS deployment completed."));
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T("VPS 部署失败：", "VPS deployment failed: ")
                + ex.Message);
        }
        finally { Volatile.Write(ref _connectionMutationInProgress, 0); }
    }

    private async void Connect_Click(
        object? sender,
        RoutedEventArgs e) =>
        await ConnectInternalAsync();

    private async Task<bool> ConnectInternalAsync(
        bool showResultDialog = true)
    {
        if (Interlocked.CompareExchange(ref _connectionMutationInProgress, 1, 0) != 0) return false;
        try
        {
            _profile ??= await _settingsStore.LoadVpsProfileAsync(
                _settings.VpsProfilePath);
            if (_profile is null)
                throw new InvalidOperationException(
                    L10n.T("请先部署 VPS 或导入连接配置。", "Deploy a VPS or import a connection profile first."));

            await _singBoxManager!.ConnectAsync(
                _settings,
                _profile);
            await _settingsStore.SaveAsync(_settings);

            var connected = await WaitForManagedConnectionAsync(
                TimeSpan.FromSeconds(30));

            if (connected)
            {
                await MarkConnectionSucceededAsync();
                if (showResultDialog)
                {
                    await ShowSuccessAsync(
                        L10n.T("连接成功。", "Connected successfully."));
                }
            }
            else if (showResultDialog)
            {
                await ShowWarningAsync(
                    L10n.T(
                        "sing-box 已启动，但暂未通过外网检测；状态检查将继续重试。",
                        "sing-box started, but the external connectivity check has not passed yet. Status checks will continue."));
            }

            await RefreshStateAsync();
            return connected;
        }
        catch (Exception ex)
        {
            if (showResultDialog)
            {
                await ShowErrorAsync(
                    L10n.T("连接失败：", "Connection failed: ") + ex.Message);
            }
            return false;
        }
        finally { Volatile.Write(ref _connectionMutationInProgress, 0); }
    }

    private async Task<bool> WaitForManagedConnectionAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_singBoxManager!.IsRunning(_settings))
            {
                var probe =
                    await _networkProbe.ProbeManagedConnectionAsync(localProxyPort: _settings.LocalProxyPort);
                if (probe.Connected)
                {
                    ConnectionText.Text = L10n.T("已连接", "Connected");
                    ExitIpText.Text = probe.ExitInfo;
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return false;
    }

    private async Task MarkConnectionSucceededAsync()
    {
        if (_settings.HasSuccessfulConnection)
            return;

        _settings.HasSuccessfulConnection = true;
        AutoConnectCheckBox.IsEnabled = true;
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task AutoConnectAfterReadinessAsync()
    {
        if (Volatile.Read(ref _connectionMutationInProgress) != 0) return;
        if (Interlocked.Exchange(ref _autoConnectInProgress, 1) == 1)
            return;

        try
        {
            _profile ??= await _settingsStore.LoadVpsProfileAsync(
                _settings.VpsProfilePath);

            if (_profile is null || !_singBoxManager!.IsInstalled(_settings))
            {
                await ShowWarningAsync(
                    L10n.T(
                        "自动连接未执行：VPS 配置或 sing-box 不完整。",
                        "Auto-connect was not started because the VPS profile or sing-box installation is incomplete."));
                return;
            }

            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_singBoxManager.IsRunning(_settings))
                {
                    var existing =
                        await _networkProbe.ProbeManagedConnectionAsync(localProxyPort: _settings.LocalProxyPort);
                    if (existing.Connected)
                    {
                        await MarkConnectionSucceededAsync();
                        await RefreshStateAsync();
                        await ShowSuccessAsync(
                            L10n.T(
                                "自动连接已确认当前连接正常。",
                                "Auto-connect confirmed that the existing connection is healthy."));
                        return;
                    }

                    await _singBoxManager.StopAsync(_settings);
                }

                var networkReady = _networkProbe.IsNetworkAvailable()
                    && !string.IsNullOrWhiteSpace(
                        _networkProbe.DetectLocalDns());
                var vpsReady = networkReady
                    && (_profile.Transport == "hysteria2" || await _networkProbe.IsTcpPortOpenAsync(
                        _profile.ServerIp,
                        _profile.ServerPort,
                        TimeSpan.FromSeconds(3)));

                if (vpsReady)
                {
                    await ConnectInternalAsync();
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            await ShowWarningAsync(
                L10n.T(
                    "自动连接等待超时，请检查网络、VPS 和 sing-box 状态。",
                    "Auto-connect timed out. Check the network, VPS, and sing-box status."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T("自动连接失败：", "Auto-connect failed: ")
                + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _autoConnectInProgress, 0);
        }
    }

    private async void Stop_Click(
        object? sender,
        RoutedEventArgs e) =>
        await StopInternalAsync();

    private async Task<bool> StopInternalAsync(
        bool showResultDialog = true)
    {
        try
        {
            await _singBoxManager!.StopAsync(_settings);
            await RefreshStateAsync();
            if (showResultDialog)
            {
                await ShowInfoAsync(L10n.T("已停止。", "Stopped."));
            }
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T("停止失败：", "Stop failed: ") + ex.Message);
            return false;
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

        var reconnected = true;
        if (_singBoxManager!.IsRunning(_settings))
        {
            reconnected = await StopInternalAsync(showResultDialog: false)
                && await ConnectInternalAsync(showResultDialog: false);
        }

        if (reconnected)
        {
            await ShowSuccessAsync(
                L10n.T("规则已保存。", "Rules saved."));
        }
        else
        {
            await ShowWarningAsync(
                L10n.T(
                    "规则已保存，但连接未能重新建立，请手动连接。",
                    "Rules were saved, but the connection could not be re-established. Connect manually."));
        }
    }

    private async void Transport_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences || !_initialized) return;
        if (Volatile.Read(ref _connectionMutationInProgress) != 0)
        {
            _loadingPreferences = true;
            try { TransportBox.SelectedIndex = _settings.PreferredTransport == "vless" ? 1 : 0; }
            finally { _loadingPreferences = false; }
            return;
        }
        try
        {
            _settings.PreferredTransport = (TransportBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "hysteria2";
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex) { await ShowErrorAsync(L10n.T("设置保存失败：", "Could not save settings: ") + ex.Message); }
    }

    private async void ImportProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _connectionMutationInProgress, 1, 0) != 0) return;
        try
        {
            if (_privilegeLauncher.HasOwnedProcess(_settings.SingBoxExecutablePath, _settings.ManagedConfigPath))
            {
                await ShowInfoAsync(L10n.T("请先停止当前连接，再导入配置。", "Stop the current connection before importing a profile."));
                return;
            }
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L10n.T("导入连接配置", "Import connection profile"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            var file = files.FirstOrDefault();
            if (file is null) return;
            await using var input = await file.OpenReadAsync();
            var profile = await new ProfileImportService().ReadAsync(input);
            if (_privilegeLauncher.HasOwnedProcess(_settings.SingBoxExecutablePath, _settings.ManagedConfigPath))
                throw new InvalidOperationException(L10n.T("请先停止当前连接，再导入配置。", "Stop the current connection before importing a profile."));
            await _settingsStore.SaveVpsProfileAsync(profile, _settings.VpsProfilePath);
            _profile = profile;
            _settings.VpsHost = profile.ServerIp;
            _settings.PreferredTransport = profile.Transport;
            _settings.HasSuccessfulConnection = false;
            _settings.AutoConnect = false;
            VpsHostBox.Text = profile.ServerIp;
            _loadingPreferences = true;
            try
            {
                TransportBox.SelectedIndex = profile.Transport == "hysteria2" ? 0 : 1;
                AutoConnectCheckBox.IsChecked = false;
            }
            finally { _loadingPreferences = false; }
            await _settingsStore.SaveAsync(_settings);
            await RefreshStateAsync();
            await ShowSuccessAsync(L10n.T("连接配置已导入。", "Connection profile imported."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L10n.T("导入失败：", "Import failed: ") + ex.Message);
        }
        finally { Volatile.Write(ref _connectionMutationInProgress, 0); }
    }

    private async void ExportMobileProfile_Click(
        object? sender,
        RoutedEventArgs e) => await ExportProfileAsync(thirdParty: false);

    private async void ExportIosProfile_Click(object? sender, RoutedEventArgs e) =>
        await ExportProfileAsync(thirdParty: true);

    private async Task ExportProfileAsync(bool thirdParty)
    {
        try
        {
            _profile ??= await _settingsStore.LoadVpsProfileAsync(
                _settings.VpsProfilePath);
            if (_profile is null)
            {
                await ShowInfoAsync(L10n.T(
                    "请先部署 VPS，再导出手机配置。",
                    "Deploy the VPS before exporting a mobile profile."));
                return;
            }

            var json = thirdParty
                ? new ThirdPartyProfileExportService().Serialize(_profile)
                : new MobileProfileExportService().Serialize(_profile);
            var file = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = thirdParty ? L10n.T("导出 iOS 配置", "Export iOS profile") : L10n.T(
                        "导出手机配置（含连接凭据，请妥善保管）",
                        "Export mobile profile (contains connection credentials; keep it private)"),
                    SuggestedFileName = thirdParty ? "s2n-ios-profile.json" : "s2n-mobile-profile.json",
                    DefaultExtension = "json",
                    ShowOverwritePrompt = true,
                    FileTypeChoices =
                    [
                        new FilePickerFileType("JSON")
                        {
                            Patterns = ["*.json"],
                            MimeTypes = ["application/json"]
                        }
                    ]
                });
            if (file is null) return;

            await using (var output = await file.OpenWriteAsync())
            {
                if (output.CanSeek)
                {
                    output.SetLength(0);
                    output.Position = 0;
                }
                await output.WriteAsync(Encoding.UTF8.GetBytes(json));
                await output.FlushAsync();
            }

            await ShowSuccessAsync(thirdParty ? L10n.T(
                "iOS 配置已导出，请按 iOS 连接指南导入客户端。",
                "iOS profile exported. Follow the iOS guide to import it into your client.") : L10n.T(
                "手机配置已导出，请在 Android 版 S2N 中导入，并妥善保管该文件。",
                "Mobile profile exported. Import it into S2N for Android and keep the file private."));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L10n.T(
                "配置导出失败：", "Profile export failed: ") + ex.Message);
        }
    }

    private async void Refresh_Click(
        object? sender,
        RoutedEventArgs e) =>
        await RefreshStateAsync();

    private async void Exit_Click(
        object? sender,
        RoutedEventArgs e) =>
        await ExitAsync();

    private async void ThemeMode_SelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingPreferences
            || ThemeModeBox.SelectedItem is not ComboBoxItem item)
            return;

        var mode = item.Tag?.ToString() ?? "system";
        _settings.ThemeMode = mode;
        ApplyTheme(mode);
        await _settingsStore.SaveAsync(_settings);
    }

    private async void GlobalMode_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_loadingPreferences) return;

        try
        {
            _settings.GlobalMode = GlobalModeCheckBox.IsChecked == true;
            await _settingsStore.SaveAsync(_settings);

            var modeMessage = _settings.GlobalMode
                ? L10n.T(
                    "已启用全局模式；局域网和 VPS 地址仍保持直连。",
                    "Global mode enabled. LAN and VPS addresses remain direct.")
                : L10n.T(
                    "已启用规则模式。",
                    "Rule-based mode enabled.");

            if (_singBoxManager!.IsRunning(_settings))
            {
                if (!await StopInternalAsync(showResultDialog: false)) return;
                var connected = await ConnectInternalAsync(
                    showResultDialog: false);

                if (connected)
                {
                    await ShowSuccessAsync(
                        modeMessage + L10n.T(
                            "连接已重新建立。",
                            " The connection has been re-established."));
                }
                else
                {
                    await ShowWarningAsync(
                        modeMessage + L10n.T(
                            "连接未能重新建立，请手动连接。",
                            " The connection could not be re-established. Connect manually."));
                }
            }
            else
            {
                await ShowInfoAsync(modeMessage);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(
                L10n.T(
                    "路由模式切换失败：",
                    "Failed to change routing mode: ")
                + ex.Message);
        }
    }

    private async void AutoStart_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_loadingPreferences) return;

        var enabled = AutoStartCheckBox.IsChecked == true;
        AutoStartCheckBox.IsEnabled = false;
        try
        {
            await _startupRegistration.SetEnabledAsync(enabled);
            _settings.AutoStart = enabled;
            await _settingsStore.SaveAsync(_settings);
            await ShowInfoAsync(
                enabled
                    ? L10n.T(
                        "已启用开机启动。登录后将延迟约 60 秒启动 S2N。",
                        "Startup enabled. S2N will start about 60 seconds after sign-in.")
                    : L10n.T(
                        "已关闭开机启动。",
                        "Startup disabled."));
        }
        catch (Exception ex)
        {
            _loadingPreferences = true;
            AutoStartCheckBox.IsChecked = !enabled;
            _loadingPreferences = false;
            await ShowErrorAsync(
                L10n.T("开机启动设置失败：", "Failed to update startup: ")
                + ex.Message);
        }
        finally
        {
            AutoStartCheckBox.IsEnabled = true;
        }
    }

    private async void AutoConnect_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (_loadingPreferences) return;

        if (!_settings.HasSuccessfulConnection)
        {
            _loadingPreferences = true;
            AutoConnectCheckBox.IsChecked = false;
            _loadingPreferences = false;
            await ShowWarningAsync(
                L10n.T(
                    "请先手动连接成功一次，再启用自动连接。",
                    "Connect successfully once before enabling auto-connect."));
            return;
        }

        _settings.AutoConnect = AutoConnectCheckBox.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        await ShowInfoAsync(
            _settings.AutoConnect
                ? L10n.T(
                    "已启用自动连接。程序启动后会等待网络和必要组件就绪，再自动连接。",
                    "Auto-connect enabled. After launch, S2N waits for the network and required components before connecting.")
                : L10n.T(
                    "已关闭自动连接。",
                    "Auto-connect disabled."));
    }


    private Task ShowInfoAsync(string message) =>
        ShowMessageAsync(
            L10n.T("提示", "Information"),
            message,
            MessageDialogKind.Information);

    private Task ShowSuccessAsync(string message) =>
        ShowMessageAsync(
            L10n.T("成功", "Success"),
            message,
            MessageDialogKind.Success);

    private Task ShowWarningAsync(string message) =>
        ShowMessageAsync(
            L10n.T("注意", "Warning"),
            message,
            MessageDialogKind.Warning);

    private Task ShowErrorAsync(string message) =>
        ShowMessageAsync(
            L10n.T("错误", "Error"),
            message,
            MessageDialogKind.Error);

    private async Task ShowMessageAsync(
        string title,
        string message,
        MessageDialogKind kind)
    {
        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();

        var dialog = new MessageDialog(
            title,
            L10n.TranslateTechnicalMessage(message),
            kind);
        await dialog.ShowDialog(this);
    }

    private static void ApplyTheme(string mode)
    {
        if (Application.Current is null) return;

        Application.Current.RequestedThemeVariant = mode switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void SelectThemeMode(string mode)
    {
        ThemeModeBox.SelectedIndex = mode switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
    }
}
