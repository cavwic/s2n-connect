using System.Runtime.InteropServices;
using S2N.Localization;

namespace S2N.Services;

public static class PlatformInfo
{
    public static string OsToken =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        throw new PlatformNotSupportedException(
            L10n.T("不支持当前操作系统。", "Unsupported operating system."));

    public static string ArchitectureToken => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "386",
        Architecture.Arm => "arm",
        _ => throw new PlatformNotSupportedException(
            L10n.T(
                $"不支持的处理器架构：{RuntimeInformation.OSArchitecture}",
                $"Unsupported architecture: {RuntimeInformation.OSArchitecture}"))
    };

    public static string ExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "sing-box.exe"
            : "sing-box";

    public static bool IsWindows =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMacOS =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsLinux =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
