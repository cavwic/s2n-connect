param([string]$OutputPath = 'artifacts/win-x64')

$ErrorActionPreference = "Stop"
$s2nRepositoryRoot = Split-Path -Parent $PSScriptRoot
$s2nArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $s2nRepositoryRoot 'artifacts'))
$s2nOutput = [System.IO.Path]::GetFullPath($(if ([System.IO.Path]::IsPathRooted($OutputPath)) {
  $OutputPath
} else { Join-Path $s2nRepositoryRoot $OutputPath }))
if (!$s2nOutput.StartsWith($s2nArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
    [System.StringComparison]::OrdinalIgnoreCase)) { throw 'OutputPath must be a child of artifacts.' }
$s2nAncestor = $s2nOutput
while (!(Test-Path -LiteralPath $s2nAncestor)) { $s2nAncestor = Split-Path -Parent $s2nAncestor }
$s2nItem = Get-Item -LiteralPath $s2nAncestor -Force
while ($null -ne $s2nItem) {
  if ($s2nItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'OutputPath cannot use symbolic links.' }
  $s2nItem = if ($s2nItem.PSIsContainer) { $s2nItem.Parent } else { $s2nItem.Directory }
}
if ((Test-Path -LiteralPath $s2nOutput) -and
    (!(Test-Path -LiteralPath $s2nOutput -PathType Container) -or
      @(Get-ChildItem -LiteralPath $s2nOutput -Force).Count -gt 0)) {
  throw 'Choose an empty -OutputPath under artifacts; publishing over old files can retain unused DLLs.'
}
Push-Location -LiteralPath $s2nRepositoryRoot
try {
  dotnet restore
  if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
  dotnet build -c Release
  if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
  dotnet publish .\src\S2N.App\S2N.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $s2nOutput
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
  & (Join-Path $PSScriptRoot 'prepare-desktop-release.ps1') `
    -OutputPath $s2nOutput `
    -OutputRoot (Join-Path $s2nRepositoryRoot 'artifacts')
  Write-Host "Output: $s2nOutput"
}
finally {
  Pop-Location
}
