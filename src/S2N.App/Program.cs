using Avalonia;
using S2N.Services;

namespace S2N;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--check-package"]) return PackageSelfCheck.Run();
        if (args is ["--check-ui"]) return PackageUiCheck.Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
