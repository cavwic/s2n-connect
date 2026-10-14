using System.Text;
using System.Text.Json;
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
        if (string.IsNullOrWhiteSpace(settings.SshKeyPath))
            throw new InvalidOperationException(
                L10n.T("SSH 私钥尚未验证。", "SSH key has not been validated."));

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(BuildRemoteScript()));
        var command = $"printf '%s' '{encoded}' | base64 -d | bash";

        var output = await _sshService.ExecuteAsync(
            settings,
            settings.SshKeyPath,
            passphrase,
            command,
            cancellationToken);

        const string marker = "S2N_PROFILE_JSON=";
        var line = output.Split('\n')
            .LastOrDefault(x => x.StartsWith(marker, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                L10n.T("VPS 部署未返回配置文件。", "The VPS deployment did not return a profile."));

        var profile = JsonSerializer.Deserialize<VpsProfile>(
            line[marker.Length..].Trim(),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                L10n.T("VPS 返回了无效的配置文件。", "Invalid VPS profile returned."));

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _settingsStore.SaveVpsProfileAsync(
            profile,
            settings.VpsProfilePath,
            cancellationToken);
        return profile;
    }

    private static string BuildRemoteScript() => """
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

apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y curl ca-certificates openssl

if ! command -v xray >/dev/null 2>&1; then
  bash -c "$(curl -fsSL https://github.com/XTLS/Xray-install/raw/main/install-release.sh)" @ install
fi

CONFIG_DIR=/usr/local/etc/xray
CONFIG_FILE="$CONFIG_DIR/config.json"
PROFILE_FILE=/root/s2n-profile.json
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

systemctl is-active --quiet xray
ss -lntp '( sport = :443 )' | grep -q xray

printf 'S2N_PROFILE_JSON='
tr -d '\n' < "$PROFILE_FILE"
printf '\n'
""";
}
