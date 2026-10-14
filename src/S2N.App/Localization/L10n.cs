using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace S2N.Localization;

public static class L10n
{
    public static bool IsChinese =>
        string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "zh",
            StringComparison.OrdinalIgnoreCase);

    public static string T(string chinese, string english) =>
        IsChinese ? chinese : english;

    public static string TranslateTechnicalMessage(string message)
    {
        if (!IsChinese || string.IsNullOrWhiteSpace(message))
            return message;

        return message
            .Replace(
                "S2N requires root SSH privileges.",
                "S2N 需要 root SSH 权限。",
                StringComparison.Ordinal)
            .Replace(
                "TCP 443 is already used by another service.",
                "TCP 443 已被其他服务占用。",
                StringComparison.Ordinal)
            .Replace(
                "Unsupported VPS OS:",
                "不支持的 VPS 操作系统：",
                StringComparison.Ordinal)
            .Replace(
                "S2N 2.0 supports Ubuntu/Debian VPS targets.",
                "S2N 2.0 仅支持 Ubuntu/Debian VPS。",
                StringComparison.Ordinal);
    }

    public static void Apply(Window window)
    {
        if (IsChinese)
            return;

        window.Title = TranslateStatic(window.Title);

        foreach (var item in window.GetLogicalDescendants())
        {
            switch (item)
            {
                case TextBlock textBlock
                    when !string.IsNullOrEmpty(textBlock.Text):
                    textBlock.Text = TranslateStatic(textBlock.Text);
                    break;

                case ContentControl contentControl
                    when contentControl.Content is string content:
                    contentControl.Content = TranslateStatic(content);
                    break;

                case TextBox textBox
                    when !string.IsNullOrEmpty(textBox.PlaceholderText):
                    textBox.PlaceholderText =
                        TranslateStatic(textBox.PlaceholderText);
                    break;
            }
        }
    }

    private static string TranslateStatic(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? "";

        return EnglishByChinese.TryGetValue(value, out var english)
            ? english
            : value;
    }

    private static readonly IReadOnlyDictionary<string, string>
        EnglishByChinese =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["自建 VPS 与 sing-box 跨平台管理工具"] =
                    "Cross-platform VPS and sing-box manager",
                ["本地 IP 及位置"] = "Local IP and location",
                ["获取中"] = "Loading",
                ["出口 IP 及位置"] = "Exit IP and location",
                ["未连接"] = "Disconnected",
                ["连接状态"] = "Connection status",
                ["VPS 部署状态"] = "VPS deployment status",
                ["未检测"] = "Not checked",
                ["sing-box 部署状态"] = "sing-box deployment status",
                ["部署"] = "Deployment",
                ["VPS 地址"] = "VPS address",
                ["例如 203.0.113.10"] = "For example, 203.0.113.10",
                ["测试 VPS"] = "Test VPS",
                ["SSH 用户 / 端口"] = "SSH user / port",
                ["SSH 私钥"] = "SSH private key",
                ["连接成功后才保存路径"] =
                    "The path is saved only after a successful connection",
                ["选择私钥"] = "Select key",
                ["私钥口令"] = "Key passphrase",
                ["仅本次使用，不保存"] =
                    "Used for this session only; not saved",
                ["sing-box 路径"] = "sing-box path",
                ["用户选择安装目录"] =
                    "Select an installation directory",
                ["选择并安装"] = "Select and install",
                ["部署操作"] = "Deployment actions",
                ["一键部署"] = "One-click deploy",
                ["仅部署 VPS"] = "Deploy VPS only",
                ["偏好设置"] = "Preferences",
                ["主题"] = "Theme",
                ["跟随系统"] = "Follow system",
                ["浅色"] = "Light",
                ["深色"] = "Dark",
                ["全局模式"] = "Global mode",
                ["开机启动"] = "Start at sign-in",
                ["自动连接"] = "Auto-connect",
                ["全局模式仅代理公网流量，局域网与 VPS 地址保持直连。自动连接需先手动连接成功一次。"] =
                    "Global mode routes public Internet traffic through the VPS while LAN and VPS addresses remain direct. Auto-connect requires one successful manual connection first.",
                ["使用"] = "Connection",
                ["连接"] = "Connect",
                ["停止"] = "Stop",
                ["规则管理"] = "Rules",
                ["刷新状态"] = "Refresh status",
                ["退出"] = "Exit",
                ["指定程序（每行一个）"] =
                    "Specified processes (one per line)",
                ["命中后，该程序的全部公网流量走 VPS"] =
                    "All public traffic from a matched process is routed through the VPS",
                ["指定域名后缀（每行一个）"] =
                    "Domain suffixes (one per line)",
                ["任何程序访问命中域名时均走 VPS，例如 google.com"] =
                    "Traffic to matching domains is routed through the VPS for any process, for example google.com",
                ["取消"] = "Cancel",
                ["保存"] = "Save",
                ["确认 SSH 服务器指纹"] =
                    "Confirm SSH server fingerprint",
                ["首次连接该 VPS。请通过供应商控制台或可信渠道核对以下 SSH Host Key 指纹："] =
                    "This is the first connection to this VPS. Verify the following SSH host-key fingerprint through the provider console or another trusted channel:",
                ["确认后，S2N 会按 VPS 地址和端口保存该指纹；后续若发生变化，连接会被拒绝。"] =
                    "After confirmation, S2N saves the fingerprint for this VPS address and port. Future connections are rejected if it changes.",
                ["信任并继续"] = "Trust and continue"
            };
}
