using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using S2N.Localization;

namespace S2N.Services;

public static class WindowsTunAdapterCleanup
{
    public static bool IsGeneratedName(string name) =>
        Regex.IsMatch(name, "\\AS2N-[0-9a-f]{16}\\z", RegexOptions.CultureInvariant);

    public static string ExpectedGuid(string name) =>
        // Compatibility with sing-tun's adapter identity algorithm; not a
        // cryptographic trust decision. Only a previously owned nonce is allowed.
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes("wintun" + name))).ToString("D");

    public static bool IsExpectedIdentity(string name, string guid) =>
        IsGeneratedName(name) && Guid.TryParse(guid, out var actual)
        && actual == Guid.Parse(ExpectedGuid(name));

    internal static string ActualGuid(string name)
    {
        var found = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return found is not null && Guid.TryParse(found.Id, out var guid) ? guid.ToString("D") : "";
    }

    internal static OwnedCoreProcess Capture(OwnedCoreProcess record)
    {
        if (string.IsNullOrWhiteSpace(record.InterfaceName)) return record;
        if (!IsGeneratedName(record.InterfaceName)) throw IdentityError();
        var actual = ActualGuid(record.InterfaceName);
        if (string.IsNullOrWhiteSpace(actual)) return record;
        if (!IsExpectedIdentity(record.InterfaceName, actual)
            || (!string.IsNullOrWhiteSpace(record.InterfaceGuid)
                && (!Guid.TryParse(record.InterfaceGuid, out var stored)
                    || stored != Guid.Parse(actual)))) throw IdentityError();
        return record with { InterfaceGuid = actual };
    }

    internal static async Task<OwnedCoreProcess> CaptureAsync(OwnedCoreProcess record,
        CancellationToken cancellationToken = default)
    {
        record = Capture(record);
        if (string.IsNullOrWhiteSpace(record.InterfaceName) || !string.IsNullOrWhiteSpace(record.PnpDeviceId)) return record;
        var identity = await QueryIdentityAsync(record.InterfaceName, ExpectedGuid(record.InterfaceName), cancellationToken);
        if (identity is null) return record;
        if (!IsExpectedIdentity(record.InterfaceName, identity.InterfaceGuid)
            || !MatchesPnpGuid(identity.InterfaceGuid, identity.PnpDeviceId)) throw IdentityError();
        return record with { InterfaceGuid = identity.InterfaceGuid, PnpDeviceId = identity.PnpDeviceId };
    }

    internal static async Task<WindowsAdapterIdentity> CaptureLegacyFixtureAsync(string name,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || name != "s2n-tun") throw IdentityError();
        return await QueryIdentityAsync(name, ExpectedGuid(name), cancellationToken)
            ?? throw new InvalidOperationException("The synthetic legacy adapter identity was not captured.");
    }

    internal static Task VerifyCandidateAbsentBeforeLaunchAsync(string name, CancellationToken cancellationToken) =>
        AssertCandidateAbsentAsync(name, cancellationToken);

    internal static async Task<OwnedCoreProcess> CapturePendingAsync(OwnedCoreProcess record,
        CancellationToken cancellationToken = default)
    {
        if (record.Ready || !record.AdapterAbsentBeforeLaunch || !IsGeneratedName(record.InterfaceName))
            throw IdentityError();
        // A normal adapter identity appearing during this last check still has
        // to match its alias and GUID; a conflicting row is never bypassed.
        var identity = await QueryIdentityAsync(record.InterfaceName, ExpectedGuid(record.InterfaceName), cancellationToken);
        if (identity is null)
        {
            var script = BuildPendingIdentityScript(record.InterfaceName);
            var result = await ProcessRunner.RunAsync(PowerShellPath(),
                ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
                cancellationToken: cancellationToken);
            if (result.ExitCode != 0) throw IdentityError();
            if (string.IsNullOrWhiteSpace(result.StdOut)) return record;
            identity = JsonSerializer.Deserialize<WindowsAdapterIdentity>(result.StdOut) ?? throw IdentityError();
        }
        if (!IsExpectedIdentity(record.InterfaceName, identity.InterfaceGuid)
            || !MatchesPnpGuid(identity.InterfaceGuid, identity.PnpDeviceId)) throw IdentityError();
        return record with { InterfaceGuid = identity.InterfaceGuid, PnpDeviceId = identity.PnpDeviceId,
            PendingAdapterIdentityRecovered = true };
    }

    internal static string BuildPendingIdentityScript(string name) =>
        """
$ErrorActionPreference = 'Stop'
$expectedInstance = __INSTANCE__
$devices = @(Get-PnpDevice -Class Net -ErrorAction Stop | Where-Object {
  $_.InstanceId -ieq $expectedInstance
})
if ($devices.Count -eq 0) { exit 0 }
if ($devices.Count -ne 1 -or $devices[0].Class -ne 'Net' -or
    $devices[0].InstanceId -notmatch '^SWD\\WINTUN\\') { throw 'Pending adapter identity did not match.' }
@{ InterfaceGuid = ([guid]$devices[0].InstanceId.Substring('SWD\WINTUN\'.Length)).ToString('D');
   PnpDeviceId = $devices[0].InstanceId } | ConvertTo-Json -Compress
""".Replace("__INSTANCE__", Quote("SWD\\WINTUN\\{" + ExpectedGuid(name).ToUpperInvariant() + "}"));

    private static async Task<WindowsAdapterIdentity?> QueryIdentityAsync(string name, string guid,
        CancellationToken cancellationToken)
    {
        var script = BuildIdentityScript(name, guid);
        var result = await ProcessRunner.RunAsync(PowerShellPath(),
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0) throw IdentityError();
        if (string.IsNullOrWhiteSpace(result.StdOut)) return null;
        return JsonSerializer.Deserialize<WindowsAdapterIdentity>(result.StdOut)
            ?? throw IdentityError();
    }

    internal static string BuildIdentityScript(string name, string guid) =>
        """
$ErrorActionPreference = 'Stop'
$expectedName = __NAME__
$expectedGuid = [guid]__GUID__
$ownedAdapters = @(Get-CimInstance -ClassName Win32_NetworkAdapter | Where-Object {
  $_.GUID -and [guid]$_.GUID -eq $expectedGuid
})
if ($ownedAdapters.Count -eq 0) { exit 0 }
if ($ownedAdapters.Count -ne 1 -or $ownedAdapters[0].NetConnectionID -cne $expectedName -or
    $ownedAdapters[0].PNPDeviceID -notmatch '^SWD\\WINTUN\\') { throw 'Adapter identity did not match.' }
@{ InterfaceGuid = ([guid]$ownedAdapters[0].GUID).ToString('D'); PnpDeviceId = $ownedAdapters[0].PNPDeviceID } | ConvertTo-Json -Compress
""".Replace("__NAME__", Quote(name)).Replace("__GUID__", Quote(guid));

    private static async Task AssertCandidateAbsentAsync(string name, CancellationToken cancellationToken)
    {
        if (!IsGeneratedName(name) || !string.IsNullOrWhiteSpace(ActualGuid(name))) throw IdentityError();
        // Wintun uses RequestedGUID as the SWD instance ID. This inferred ID is
        // used only to prove absence; removal always requires a captured actual ID.
        var expectedInstance = "SWD\\WINTUN\\{" + ExpectedGuid(name).ToUpperInvariant() + "}";
        var script = "$ErrorActionPreference='Stop';"
            + "$found=@(Get-PnpDevice -Class Net -ErrorAction Stop|Where-Object{$_.InstanceId -ieq "
            + Quote(expectedInstance) + "});if($found.Count -ne 0){exit 1};exit 0";
        var result = await ProcessRunner.RunAsync(PowerShellPath(),
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))],
            cancellationToken: cancellationToken);
        if (result.ExitCode != 0) throw IdentityError();
    }

    internal static Task RemoveAsync(OwnedCoreProcess record, string resultDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(record.InterfaceName)) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(record.InterfaceGuid))
            return AssertCandidateAbsentAsync(record.InterfaceName, cancellationToken);
        if (!IsExpectedIdentity(record.InterfaceName, record.InterfaceGuid)
            || !MatchesPnpGuid(record.InterfaceGuid, record.PnpDeviceId)) throw IdentityError();
        return RemoveExactAsync(record.InterfaceName, record.InterfaceGuid, record.PnpDeviceId,
            resultDirectory, cancellationToken, record.AdapterAbsentBeforeLaunch, record.PendingAdapterIdentityRecovered);
    }

    internal static Task RemoveLegacyFixtureAsync(string name, WindowsAdapterIdentity identity, string resultDirectory)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true"
            || name != "s2n-tun" || !Guid.TryParse(identity.InterfaceGuid, out var parsed)
            || parsed != Guid.Parse(ExpectedGuid(name))
            || !MatchesPnpGuid(identity.InterfaceGuid, identity.PnpDeviceId)) throw IdentityError();
        return RemoveExactAsync(name, identity.InterfaceGuid, identity.PnpDeviceId,
            resultDirectory, CancellationToken.None);
    }

    private static async Task RemoveExactAsync(string name, string guid, string pnpDeviceId, string resultDirectory,
        CancellationToken cancellationToken, bool prelaunchAbsent = false, bool recoveredPending = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, "adapter-cleanup-" + Guid.NewGuid().ToString("N") + ".json");
        var script = BuildRemovalScript(name, guid, pnpDeviceId, resultPath, prelaunchAbsent, recoveredPending);
        var start = new ProcessStartInfo
        {
            FileName = PowerShellPath(),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException(L10n.T(
                "无法启动本次 TUN 网卡的清理操作。", "Could not start cleanup of this connection's TUN adapter."));
        // Once the privileged command starts, wait for its outcome rather than
        // leaving it running when the UI cancellation token changes.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(L10n.T(
                "本次 TUN 网卡尚未清理，连接记录已保留，请再次点击停止。",
                "This connection's TUN adapter could not be removed. Its ownership record was kept; stop it again to retry."));
    }

    internal static string BuildRemovalScript(string name, string guid, string pnpDeviceId, string resultPath,
        bool prelaunchAbsent = false, bool recoveredPending = false) =>
        """
$ErrorActionPreference = 'Stop'
$result = [ordered]@{ Status = 'Pending'; InterfaceName = __NAME__; InterfaceGuid = __GUID__; PnpDeviceId = __PNPID__;
  PrelaunchAbsent = __ABSENT__; PendingIdentityRecovered = __PENDING__ }
$resultPath = __RESULT__
try {
  $expectedGuid = [guid]$result.InterfaceGuid
  $ownedAdapters = @(Get-CimInstance -ClassName Win32_NetworkAdapter | Where-Object {
    $_.GUID -and [guid]$_.GUID -eq $expectedGuid
  })
  if ($ownedAdapters.Count -gt 0) {
    if ($ownedAdapters.Count -ne 1) { throw 'Adapter identity was not unique.' }
    $adapter = $ownedAdapters[0]
    if ($adapter.NetConnectionID -cne $result.InterfaceName -or
        [guid]$adapter.GUID -ne $expectedGuid -or
        $adapter.PNPDeviceID -ine $result.PnpDeviceId -or
        $adapter.PNPDeviceID -notmatch '^SWD\\WINTUN\\') {
      throw 'Adapter identity did not match the owned Wintun device.'
    }
  }
  # The exact PnP identity was captured while the owned adapter was alive.
  # It remains authoritative if its Win32_NetworkAdapter row is now a ghost.
  $devices = @(Get-PnpDevice -Class Net -ErrorAction Stop | Where-Object {
    $_.InstanceId -ieq $result.PnpDeviceId
  })
  if ($devices.Count -eq 0) {
    $result.Status = 'AlreadyAbsent'
  } else {
    if ($devices.Count -ne 1) { throw 'The owned PnP identity was not unique.' }
    $device = $devices[0]
    if ($device.InstanceId -ine $result.PnpDeviceId -or $device.Class -ne 'Net' -or
        $result.PnpDeviceId -notmatch '^SWD\\WINTUN\\') { throw 'The owned PnP identity did not match.' }
    & (Join-Path ([Environment]::SystemDirectory) 'pnputil.exe') /remove-device $result.PnpDeviceId | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Removing the owned adapter failed.' }
    $remaining = @(Get-PnpDevice -Class Net -ErrorAction Stop | Where-Object {
      $_.InstanceId -ieq $result.PnpDeviceId
    })
    if ($remaining.Count -ne 0) {
      throw 'The owned PnP device is still registered.'
    }
    $result.Status = 'Removed'
  }
  [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress))
  exit 0
} catch {
  $result.Status = 'Failed'
  $result.Error = $_.Exception.Message
  [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Compress))
  exit 1
}
""".Replace("__NAME__", Quote(name)).Replace("__GUID__", Quote(guid))
        .Replace("__PNPID__", Quote(pnpDeviceId)).Replace("__RESULT__", Quote(resultPath))
        .Replace("__ABSENT__", prelaunchAbsent ? "$true" : "$false").Replace("__PENDING__", recoveredPending ? "$true" : "$false");

    internal static bool IsWintunDeviceId(string value) => value.StartsWith("SWD\\WINTUN\\", StringComparison.OrdinalIgnoreCase)
        && value.Length > "SWD\\WINTUN\\".Length;
    internal static bool MatchesPnpGuid(string guid, string deviceId) =>
        Guid.TryParse(guid, out var expected) && IsWintunDeviceId(deviceId)
        && string.Equals(deviceId, "SWD\\WINTUN\\" + expected.ToString("B"), StringComparison.OrdinalIgnoreCase);
    private static string PowerShellPath() => Path.Combine(Environment.SystemDirectory,
        "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    private static InvalidOperationException IdentityError() => new(L10n.T(
        "TUN 网卡归属无法核验，未执行移除。", "The TUN adapter's ownership could not be verified; it was not removed."));
}

internal sealed record WindowsAdapterIdentity(string InterfaceGuid, string PnpDeviceId);
