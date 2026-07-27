using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using S2N.Views;

namespace S2N;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            var menu = new NativeMenu();
            var show = new NativeMenuItem("显示");
            var connect = new NativeMenuItem("连接");
            var stop = new NativeMenuItem("停止");
            var exit = new NativeMenuItem("退出");

            show.Click += (_, _) => ShowMainWindow();
            connect.Click += async (_, _) =>
            {
                ShowMainWindow();
                if (_mainWindow is not null) await _mainWindow.ConnectFromTrayAsync();
            };
            stop.Click += async (_, _) =>
            {
                if (_mainWindow is not null) await _mainWindow.StopFromTrayAsync();
            };
            exit.Click += async (_, _) =>
            {
                if (_mainWindow is not null) await _mainWindow.ExitAsync();
            };

            menu.Add(show);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(connect);
            menu.Add(stop);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exit);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://S2N/Assets/s2n.ico"))),
                ToolTipText = "S2N",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ShowMainWindow();

            _mainWindow.Closing += (_, e) =>
            {
                if (!_mainWindow.IsExiting)
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}
