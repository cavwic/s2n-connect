using System.Text;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using S2N.Localization;
using S2N.Models;

namespace S2N.Services;

public sealed class VpsDeploymentService
{
    private readonly SshService _sshService;
    private readonly SettingsStore _settingsStore;

    public VpsDeploymentService(
        SshService sshService,
        SettingsStore settingsStore)
    {
        _sshService = sshService;
        _settingsStore = settingsStore;
    }

    public async Task<VpsProfile> DeployAsync(
        AppSettings settings,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        var deployment = CaptureDeploymentSettings(settings);
        if (string.IsNullOrWhiteSpace(deployment.SshKeyPath))
            throw new InvalidOperationException(
                L10n.T("SSH 私钥尚未验证。", "SSH key has not been validated."));

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(CreateDeploymentScript(
                deployment.PreferredTransport, deployment.VpsHost)));
        var command = $"printf '%s' '{encoded}' | base64 -d | bash";

        string output;
        try
        {
            output = await _sshService.ExecuteAsync(
                deployment,
                deployment.SshKeyPath,
                passphrase,
                command,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            var message = DescribeDeploymentError(exception.Message);
            if (message is null) throw;
            throw new InvalidOperationException(message);
        }

        const string marker = "S2N_PROFILE_JSON=";
        var line = output.Split('\n')
            .LastOrDefault(x => x.StartsWith(marker, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                L10n.T("VPS 部署未返回配置文件。", "The VPS deployment did not return a profile."));

        var profile = ParseDeploymentProfile(line[marker.Length..].Trim(),
            deployment.PreferredTransport, deployment.VpsHost);

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _settingsStore.SaveVpsProfileAsync(
            profile,
            deployment.VpsProfilePath,
            cancellationToken);
        return profile;
    }

    internal static AppSettings CaptureDeploymentSettings(AppSettings settings) => new()
    {
        VpsHost = settings.VpsHost,
        VpsPort = settings.VpsPort,
        VpsUser = settings.VpsUser,
        SshKeyPath = settings.SshKeyPath,
        SshProxyPort = settings.SshProxyPort,
        PreferredTransport = settings.PreferredTransport,
        VpsProfilePath = settings.VpsProfilePath,
        KnownHostFingerprints = new Dictionary<string, string>(
            settings.KnownHostFingerprints ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
    };

    public static VpsProfile ParseDeploymentProfile(string json, string transport, string serverHost)
    {
        try
        {
            if (json.Length > 256 * 1024) throw new InvalidDataException();
            var profile = JsonSerializer.Deserialize<VpsProfile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException();
            if (profile.ServerPort is < 1 or > 65535
                || Uri.CheckHostName(profile.ServerIp) == UriHostNameType.Unknown
                || !string.Equals(profile.Transport, transport, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();
            if (string.Equals(transport, "hysteria2", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(profile.ServerIp, serverHost, StringComparison.OrdinalIgnoreCase)
                    || profile.ServerPort != 443 || profile.ServerName != "s2n.internal"
                    || string.IsNullOrWhiteSpace(profile.Password) || profile.Password.Length > 1024
                    || string.IsNullOrWhiteSpace(profile.CertificatePem))
                    throw new InvalidDataException();
                using var certificate = X509Certificate2.CreateFromPem(profile.CertificatePem);
                if (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow
                    || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                    throw new InvalidDataException();
            }
            else if (string.IsNullOrWhiteSpace(profile.Uuid)
                     || string.IsNullOrWhiteSpace(profile.PublicKey)
                     || string.IsNullOrWhiteSpace(profile.ServerName))
                throw new InvalidDataException();
            return VpsProfileValidator.Normalize(profile);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException
                   or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            // A profile includes authentication material. Never include the JSON in errors.
            throw new InvalidDataException(L10n.T(
                "VPS 返回的连接配置无效；原配置保持不变。",
                "The VPS returned an invalid connection profile. Your previous profile was kept."));
        }
    }

    public static string? DescribeDeploymentError(string error)
    {
        if (error.Contains("S2N_UDP_CONFLICT", StringComparison.Ordinal))
            return L10n.T("UDP 443 已被其他服务占用。请导入已有连接配置，或选择其他 VPS。",
                "UDP port 443 is already used by another service. Import its connection profile or use another VPS.");
        if (error.Contains("S2N_OWNERSHIP_CONFLICT", StringComparison.Ordinal))
            return L10n.T("检测到已有或修改过的服务器配置，已停止部署以保留原服务。请导入已有配置。",
                "Existing or modified server files were found. Deployment stopped to preserve them. Import the existing profile.");
        if (error.Contains("S2N_PROFILE_MISMATCH", StringComparison.Ordinal))
            return L10n.T("已有 UDP 配置使用不同的 VPS 地址。请使用原地址或导入已有配置。",
                "The existing UDP profile uses a different VPS address. Use its original address or import the profile.");
        if (error.Contains("S2N_HASH_MISMATCH", StringComparison.Ordinal))
            return L10n.T("服务器程序校验失败，未安装。请稍后重试。",
                "The server download failed its integrity check and was not installed. Try again later.");
        if (error.Contains("S2N_DOWNLOAD_FAILED", StringComparison.Ordinal))
            return L10n.T("VPS 无法下载服务器程序，请检查 VPS 的网络连接。",
                "The VPS could not download the server program. Check its internet connection.");
        if (error.Contains("S2N_TLS_FAILURE", StringComparison.Ordinal))
            return L10n.T("服务器证书验证失败，原连接凭据保持不变。",
                "Server certificate validation failed. Existing connection credentials were kept.");
        if (error.Contains("S2N_START_FAILED", StringComparison.Ordinal))
            return L10n.T("UDP 服务未能启动。请检查 VPS 状态及 UDP 443 的占用情况。",
                "The UDP service could not start. Check the VPS and whether UDP port 443 is in use.");
        if (error.Contains("S2N_ARCH_UNSUPPORTED", StringComparison.Ordinal))
            return L10n.T("UDP 部署支持 x64 和 ARM64 VPS。", "UDP deployment supports x64 and ARM64 VPS instances.");
        if (error.Contains("S2N_OS_UNSUPPORTED", StringComparison.Ordinal))
            return L10n.T("UDP 部署需要 Ubuntu 或 Debian VPS。", "UDP deployment requires an Ubuntu or Debian VPS.");
        if (error.Contains("S2N_ROOT_REQUIRED", StringComparison.Ordinal))
            return L10n.T("部署需要 root SSH 权限。", "Deployment requires root SSH access.");
        if (error.Contains("S2N_PROFILE_INVALID", StringComparison.Ordinal)
            || error.Contains("S2N_DEPLOY_FAILED", StringComparison.Ordinal))
            return L10n.T("UDP 部署未完成，原有 SSH 与 Xray 服务保持不变。",
                "UDP deployment did not complete. Existing SSH and Xray services were preserved.");
        return null;
    }

    public static string CreateDeploymentScript(string transport, string serverHost)
    {
        if (string.Equals(transport, "vless", StringComparison.OrdinalIgnoreCase))
            return BuildLegacyRemoteScript().Replace("\r\n", "\n");
        if (!string.Equals(transport, "hysteria2", StringComparison.OrdinalIgnoreCase)
            || Uri.CheckHostName(serverHost) == UriHostNameType.Unknown)
            throw new InvalidOperationException(L10n.T("VPS 地址或连接方式无效。", "Invalid VPS address or transport."));
        using var stream = typeof(VpsDeploymentService).Assembly.GetManifestResourceStream(
            "S2N.Server.deploy_hysteria.py")
            ?? throw new InvalidOperationException(L10n.T("缺少服务器部署资源。", "The server deployment resource is missing."));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var resource = reader.ReadToEnd().Replace("\r\n", "\n");
        var request = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { server = serverHost })));
        return ("""
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo S2N_ROOT_REQUIRED >&2; exit 20; fi
. /etc/os-release
case "${ID:-}" in ubuntu|debian) ;; *) echo S2N_OS_UNSUPPORTED >&2; exit 21 ;; esac
if ! command -v python3 >/dev/null 2>&1 || ! command -v openssl >/dev/null 2>&1 || ! command -v ss >/dev/null 2>&1 || [ ! -f /etc/ssl/certs/ca-certificates.crt ]; then
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y python3 openssl ca-certificates iproute2
fi
""" + "\npython3 - '" + request + "' <<'S2N_HYSTERIA_PY'\n" + resource + "\nS2N_HYSTERIA_PY\n").Replace("\r\n", "\n");
    }

    private static string BuildLegacyRemoteScript() => """
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "S2N requires root SSH privileges." >&2
  exit 20
fi

. /etc/os-release
case "${ID:-}" in
  ubuntu|debian) ;;
  *)
    echo "Unsupported VPS OS: ${ID:-unknown}. S2N 2.0 supports Ubuntu/Debian VPS targets." >&2
    exit 21
    ;;
esac

CONFIG_DIR=/usr/local/etc/xray
CONFIG_FILE="$CONFIG_DIR/config.json"
PROFILE_FILE=/root/s2n-profile.json
# Old S2N installations have both files. Never reconstruct a missing half by
# overwriting an unrelated Xray configuration or rotating an existing account.
if [ -L "$CONFIG_DIR" ] || [ -L "$CONFIG_FILE" ] || [ -L "$PROFILE_FILE" ]; then
  echo S2N_OWNERSHIP_CONFLICT >&2
  exit 22
fi
if { [ -e "$PROFILE_FILE" ] || [ -e "$CONFIG_FILE" ]; } && ! { [ -f "$PROFILE_FILE" ] && [ -f "$CONFIG_FILE" ]; }; then
  echo S2N_OWNERSHIP_CONFLICT >&2
  exit 22
fi
if [ ! -f "$PROFILE_FILE" ] && ss -H -lnt '( sport = :443 )' | grep -q .; then
  echo S2N_OWNERSHIP_CONFLICT >&2
  exit 22
fi

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y curl ca-certificates openssl

if ! command -v xray >/dev/null 2>&1; then
  bash -c "$(curl -fsSL https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
fi

mkdir -p "$CONFIG_DIR"

if [ -f "$PROFILE_FILE" ] && [ -f "$CONFIG_FILE" ]; then
  xray run -test -c "$CONFIG_FILE" >/dev/null
else
  if ss -lntp '( sport = :443 )' | grep -q LISTEN; then
    if ! ss -lntp '( sport = :443 )' | grep -q xray; then
      echo "TCP 443 is already used by another service." >&2
      exit 22
    fi
  fi

  UUID="$(xray uuid)"
  KEY_OUTPUT="$(xray x25519)"
  PRIVATE_KEY="$(printf '%s\n' "$KEY_OUTPUT" | sed -n 's/^PrivateKey:[[:space:]]*//p' | head -n1)"
  PUBLIC_KEY="$(printf '%s\n' "$KEY_OUTPUT" | sed -n 's/^Password (PublicKey):[[:space:]]*//p' | head -n1)"
  if [ -z "$PUBLIC_KEY" ]; then
    PUBLIC_KEY="$(printf '%s\n' "$KEY_OUTPUT" | sed -n 's/^Password:[[:space:]]*//p' | head -n1)"
  fi
  SHORT_ID="$(openssl rand -hex 4)"
  SERVER_IP="$(curl -4fsS --max-time 10 https://api.ipify.org || hostname -I | awk '{print $1}')"
  TARGET=www.bing.com

  cat > "$CONFIG_FILE" <<EOF
{
  "log": { "loglevel": "warning" },
  "inbounds": [
    {
      "tag": "s2n-vless-reality",
      "listen": "0.0.0.0",
      "port": 443,
      "protocol": "vless",
      "settings": {
        "clients": [
          {
            "id": "$UUID",
            "email": "s2n-client",
            "flow": "xtls-rprx-vision"
          }
        ],
        "decryption": "none"
      },
      "streamSettings": {
        "method": "raw",
        "security": "reality",
        "realitySettings": {
          "show": false,
          "target": "$TARGET:443",
          "xver": 0,
          "serverNames": [ "$TARGET" ],
          "privateKey": "$PRIVATE_KEY",
          "shortIds": [ "$SHORT_ID" ]
        }
      }
    }
  ],
  "outbounds": [
    { "tag": "direct", "protocol": "freedom" },
    { "tag": "block", "protocol": "blackhole" }
  ]
}
EOF

  chown nobody:nogroup "$CONFIG_FILE"
  chmod 600 "$CONFIG_FILE"
  xray run -test -c "$CONFIG_FILE"
  systemctl enable xray
  systemctl restart xray

  cat > "$PROFILE_FILE" <<EOF
{
  "serverIp": "$SERVER_IP",
  "serverPort": 443,
  "uuid": "$UUID",
  "publicKey": "$PUBLIC_KEY",
  "shortId": "$SHORT_ID",
  "serverName": "$TARGET"
}
EOF
  chmod 600 "$PROFILE_FILE"
fi

# Reuse the original account and restart only if this existing service was stopped.
systemctl enable xray
if ! systemctl is-active --quiet xray; then systemctl start xray; fi
systemctl is-active --quiet xray
ss -lntp '( sport = :443 )' | grep -q xray

printf 'S2N_PROFILE_JSON='
tr -d '\n' < "$PROFILE_FILE"
printf '\n'
""";
}
