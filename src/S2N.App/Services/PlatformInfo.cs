using System.Runtime.InteropServices;

namespace S2N.Services;

public static class PlatformInfo
{
    public static string OsToken =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
        throw new PlatformNotSupportedException("Unsupported operating system.");

    public static string ArchitectureToken => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "386",
        Architecture.Arm => "arm",
        _ => throw new PlatformNotSupportedException(
            $"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
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
