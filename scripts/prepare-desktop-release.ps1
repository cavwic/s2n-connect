param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts')
)

$ErrorActionPreference = 'Stop'
$s2nRepositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$s2nArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $s2nRepositoryRoot 'artifacts'))
$s2nPathComparison = if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
    [System.StringComparison]::OrdinalIgnoreCase
} else {
    [System.StringComparison]::Ordinal
}

function Get-S2nFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A nonempty output path is required.' }
    $absolute = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
    }
    if ($absolute.Length -gt [System.IO.Path]::GetPathRoot($absolute).Length) {
        $absolute = $absolute.TrimEnd([char[]]@('/', '\'))
    }
    return $absolute
}

function Test-S2nChildPath([string]$Path, [string]$Parent) {
    $prefix = $Parent.TrimEnd([char[]]@('/', '\')) + [System.IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, $s2nPathComparison)
}

function Assert-S2nNoReparseAncestors([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    while ($null -ne $item) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Release preparation refuses symbolic links and reparse points.'
        }
        $item = if ($item.PSIsContainer) { $item.Parent } else { $item.Directory }
    }
}

function Get-S2nSafeTree([string]$Path) {
    Assert-S2nNoReparseAncestors $Path
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Release preparation refuses symbolic links and reparse points.'
            }
            $item
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}

function Assert-S2nPublishedApplication([string]$Path) {
    foreach ($name in @('S2N.dll', 'S2N.runtimeconfig.json', 'S2N.deps.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $Path $name) -PathType Leaf)) {
            throw "Release preparation requires a published application: missing $name."
        }
    }
}

function Get-S2nFileBytes($Items) {
    [long]$total = 0
    foreach ($item in $Items) {
        if (!$item.PSIsContainer) { $total += $item.Length }
    }
    return $total
}

$s2nExplicitRoot = Get-S2nFullPath $OutputRoot
$s2nOutput = Get-S2nFullPath $OutputPath
# Only generated output beneath this checkout's artifacts directory is eligible.
# Neither the output root itself nor any repository/source directory can be cleaned.
if (!$s2nExplicitRoot.Equals($s2nArtifactsRoot, $s2nPathComparison) -and
    !(Test-S2nChildPath $s2nExplicitRoot $s2nArtifactsRoot)) {
    throw 'The explicit output root must be the repository artifacts directory or one of its children.'
}
if (!(Test-S2nChildPath $s2nOutput $s2nExplicitRoot)) {
    throw 'The publish directory must be a strict child of the explicit output root.'
}
if (!(Test-Path -LiteralPath $s2nExplicitRoot -PathType Container) -or
    !(Test-Path -LiteralPath $s2nOutput -PathType Container)) {
    throw 'The output root and published application directory must already exist.'
}
Assert-S2nNoReparseAncestors $s2nExplicitRoot
$null = @(Get-S2nSafeTree $s2nOutput)
Assert-S2nPublishedApplication $s2nOutput

# Reject linked documentation before the existing copier can traverse it.
$null = @(Get-S2nSafeTree (Join-Path $s2nRepositoryRoot 'docs'))
foreach ($name in @('LICENSE', 'README.md', 'scripts/copy-release-docs.ps1')) {
    Assert-S2nNoReparseAncestors (Join-Path $s2nRepositoryRoot $name)
}
& (Join-Path $PSScriptRoot 'copy-release-docs.ps1') -OutputPath $s2nOutput

$s2nItems = @(Get-S2nSafeTree $s2nOutput)
$s2nBefore = Get-S2nFileBytes $s2nItems
$s2nDocsOutput = Join-Path $s2nOutput 'docs'
$s2nSymbolExtensions = @('.pdb', '.dbg', '.debug', '.mdb')
$s2nSymbolDirectories = @($s2nItems | Where-Object {
    $_.PSIsContainer -and $_.Name.EndsWith('.dSYM', [System.StringComparison]::OrdinalIgnoreCase) -and
    !(Test-S2nChildPath $_.FullName $s2nDocsOutput)
} | Sort-Object { $_.FullName.Length })
$s2nOuterSymbolDirectories = [System.Collections.Generic.List[string]]::new()
foreach ($directory in $s2nSymbolDirectories) {
    if (!($s2nOuterSymbolDirectories | Where-Object { Test-S2nChildPath $directory.FullName $_ })) {
        $s2nOuterSymbolDirectories.Add($directory.FullName)
    }
}
$s2nSymbolFiles = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($item in $s2nItems) {
    if ($item.PSIsContainer -or $s2nSymbolExtensions -notcontains $item.Extension.ToLowerInvariant() -or
        (Test-S2nChildPath $item.FullName $s2nDocsOutput)) { continue }
    $insideSymbolDirectory = $false
    foreach ($directory in $s2nOuterSymbolDirectories) {
        if (Test-S2nChildPath $item.FullName $directory) {
            $insideSymbolDirectory = $true
            break
        }
    }
    if (!$insideSymbolDirectory) { $s2nSymbolFiles.Add($item) }
}
[long]$s2nRemovedFiles = $s2nSymbolFiles.Count
foreach ($directory in $s2nOuterSymbolDirectories) {
    $s2nRemovedFiles += @($s2nItems | Where-Object {
        !$_.PSIsContainer -and (Test-S2nChildPath $_.FullName $directory)
    }).Count
}

foreach ($file in $s2nSymbolFiles) {
    if (!(Test-S2nChildPath $file.FullName $s2nOutput)) { throw 'Symbol file escaped the publish directory.' }
    Assert-S2nNoReparseAncestors $file.FullName
    Remove-Item -LiteralPath $file.FullName -Force
}
foreach ($directory in $s2nOuterSymbolDirectories) {
    if (!(Test-S2nChildPath $directory $s2nOutput)) { throw 'Symbol directory escaped the publish directory.' }
    $null = @(Get-S2nSafeTree $directory)
    Remove-Item -LiteralPath $directory -Recurse -Force
}
Assert-S2nPublishedApplication $s2nOutput
foreach ($name in @('LICENSE', 'docs/licenses/manifest.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $s2nOutput $name) -PathType Leaf)) {
        throw "Release preparation lost required licensing: $name."
    }
}
$s2nAfter = Get-S2nFileBytes @(Get-S2nSafeTree $s2nOutput)
Write-Host "Prepared desktop release: $s2nBefore -> $s2nAfter bytes; removed $s2nRemovedFiles symbol files and $($s2nOuterSymbolDirectories.Count) .dSYM containers ($($s2nBefore - $s2nAfter) bytes)."
