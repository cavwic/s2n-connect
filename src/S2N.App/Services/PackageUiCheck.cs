using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using S2N.Views;

namespace S2N.Services;

internal static class PackageUiCheck
{
    // Keep platform setup and every window constructor on the original STA thread.
    // No desktop lifetime, Show, Opened, dispatcher loop, or background work is started.
    internal static int Run() => PackageSelfCheck.RunIsolated("ui", checks =>
    {
        checks.Begin("avalonia-platform");
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        checks.Expect(Application.Current is App);
        checks.Complete();
        var previousCulture = CultureInfo.CurrentUICulture;
        var previousTheme = Application.Current!.RequestedThemeVariant;
        try
        {
            foreach (var chinese in new[] { true, false })
            {
                checks.Begin(chinese ? "windows-zh-light" : "windows-en-dark");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(chinese ? "zh-CN" : "en-US");
                Application.Current.RequestedThemeVariant = chinese ? ThemeVariant.Light : ThemeVariant.Dark;
                var main = new MainWindow();
                var export = main.FindControl<Button>("ExportMobileProfileButton");
                checks.Expect(export is not null && Equals(export.Content, chinese ? "导出手机配置" : "Export mobile profile"));
                var menu = export!.Flyout as MenuFlyout;
                checks.Expect(menu is not null && menu.Items.Count == 2);
                var android = menu!.Items[0] as MenuItem;
                var ios = menu.Items[1] as MenuItem;
                checks.Expect(android is not null && Equals(android.Header, chinese ? "Android（S2N）" : "Android (S2N)"));
                checks.Expect(ios is not null && Equals(ios.Header, chinese ? "iOS（Hiddify 等）" : "iOS (Hiddify and similar clients)"));
                checks.Expect(main.FindControl<TextBox>("VpsPortBox") is not null);
                var rules = new RulesWindow(["package-check.exe"], ["example.com"]);
                checks.Expect(rules.FindControl<TextBox>("ProcessesBox")?.Text == "package-check.exe"
                    && rules.FindControl<TextBox>("DomainsBox")?.Text == "example.com");
                var fingerprint = new FingerprintWindow("synthetic-package-check-fingerprint");
                checks.Expect(fingerprint.FindControl<TextBox>("FingerprintBox")?.Text == "synthetic-package-check-fingerprint");
                foreach (var kind in Enum.GetValues<MessageDialogKind>())
                {
                    var dialog = new MessageDialog("Package check", "Synthetic offline fixture", kind);
                    checks.Expect(Equals(dialog.FindControl<Button>("CloseButton")?.Content, chinese ? "确定" : "OK"));
                    CheckLayout(checks, dialog);
                    dialog.Close();
                }
                foreach (var window in new Window[] { main, rules, fingerprint })
                {
                    CheckLayout(checks, window);
                    window.Close();
                }
                checks.Complete();
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    });

    private static void CheckLayout(PackageSelfCheck.CheckContext checks, Window window)
    {
        window.Measure(new Size(1200, 900));
        checks.Expect(!window.IsVisible && window.Content is Control && !string.IsNullOrWhiteSpace(window.Title));
    }
}
