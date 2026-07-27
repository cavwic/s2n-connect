$ErrorActionPreference = "Stop"
dotnet restore
dotnet build -c Release
dotnet publish .\src\S2N.App\S2N.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\win-x64
Write-Host "Output: artifacts\win-x64"
