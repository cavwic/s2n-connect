namespace S2N.Models;

public sealed class AppSettings
{
    public string VpsHost { get; set; } = "";
    public int VpsPort { get; set; } = 22;
    public string VpsUser { get; set; } = "root";
    public string SshKeyPath { get; set; } = "";
    public string SingBoxInstallDirectory { get; set; } = "";
    public string SingBoxExecutablePath { get; set; } = "";
    public string ManagedConfigPath { get; set; } = "";
    public string VpsProfilePath { get; set; } = "";
    public bool AutoStart { get; set; }
    public bool AutoConnect { get; set; }

    public List<string> ProcessNames { get; set; } =
    [
        "chrome.exe",
        "ChatGPT.exe",
        "codex.exe"
    ];

    public List<string> DomainSuffixes { get; set; } =
    [
        "google.com",
        "googleapis.com",
        "gstatic.com",
        "youtube.com",
        "openai.com",
        "chatgpt.com",
        "oaistatic.com",
        "oaiusercontent.com"
    ];

    public Dictionary<string, string> KnownHostFingerprints { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VpsProfile
{
    public string ServerIp { get; set; } = "";
    public int ServerPort { get; set; } = 443;
    public string Uuid { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";
    public string ServerName { get; set; } = "www.bing.com";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
