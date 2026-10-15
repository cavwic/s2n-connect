param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$s2nRepoRoot = Split-Path -Parent $PSScriptRoot
$s2nDocsSource = Join-Path $s2nRepoRoot 'docs'
$s2nLicenseSource = Join-Path $s2nDocsSource 'licenses'
$s2nManifestPath = Join-Path $s2nLicenseSource 'manifest.json'

if (!(Test-Path -LiteralPath $s2nManifestPath -PathType Leaf)) {
    throw 'Release packaging requires docs/licenses/manifest.json.'
}
$s2nManifest = Get-Content -LiteralPath $s2nManifestPath -Raw | ConvertFrom-Json
foreach ($s2nNotice in $s2nManifest.notices) {
    $s2nNoticePath = Join-Path $s2nLicenseSource $s2nNotice.file
    $s2nNoticeHash = (Get-FileHash -LiteralPath $s2nNoticePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($s2nNoticeHash -ne $s2nNotice.sha256) {
        throw "License text checksum mismatch: $($s2nNotice.file)"
    }
}

$s2nOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath))
}
$null = New-Item -ItemType Directory -Path $s2nOutput -Force
foreach ($s2nRootFile in @('LICENSE', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $s2nRepoRoot $s2nRootFile) -Destination $s2nOutput -Force
}
$s2nDocsOutput = Join-Path $s2nOutput 'docs'
$null = New-Item -ItemType Directory -Path $s2nDocsOutput -Force
Get-ChildItem -LiteralPath $s2nDocsSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $s2nDocsOutput -Recurse -Force
}

foreach ($s2nRequiredFile in @('docs/ios.md', 'docs/README.en.md', 'docs/SIGNING.md', 'docs/THIRD_PARTY_NOTICES.md', 'docs/brand/s2n-banner.png', 'docs/licenses/manifest.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $s2nOutput $s2nRequiredFile) -PathType Leaf)) {
        throw "Required release documentation is missing: $s2nRequiredFile"
    }
}
Write-Host "Release documentation copied with its directory structure to $s2nOutput"
