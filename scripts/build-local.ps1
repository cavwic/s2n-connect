$ErrorActionPreference = "Stop"
$s2nRepositoryRoot = Split-Path -Parent $PSScriptRoot
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
    -o .\artifacts\win-x64
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
  Write-Host "Output: artifacts\win-x64"
}
finally {
  Pop-Location
}
