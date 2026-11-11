#requires -Version 5.1
<#
.SYNOPSIS
    Creates a Railroader ADK Tools source-only ZIP for the Unity drag/drop updater.
.DESCRIPTION
    Reads ADKTools.Packaging.json from the project root. The managed directories
    listed there are the ONLY directories packaged. Embeds ADKTools.Release.json
    so the Unity updater can replace them without hardcoded paths.

    To add/remove/move a managed source folder, edit ADKTools.Packaging.json.
    No edits to the Unity updater or to this build script are required.
.EXAMPLE
    .\Build-ADKToolsSource.ps1 -Version Beta1.5.9
#>
[CmdletBinding()]
param(
    [string] $ProjectRoot = $PSScriptRoot,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $Version = 'dev',
    [string] $OutputDir = 'dist'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$project = (Resolve-Path -LiteralPath $ProjectRoot -ErrorAction Stop).ProviderPath.TrimEnd([char[]]@('\','/'))
$separator = [System.IO.Path]::DirectorySeparatorChar
$prefix = $project + $separator
$configPath = Join-Path $project 'ADKTools.Packaging.json'
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "Missing ADKTools.Packaging.json at project root: $configPath"
}
$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($config.schemaVersion -ne 1) { throw 'Unsupported ADKTools.Packaging.json schemaVersion (expected 1).' }
$managed = @($config.managedDirectories)
if ($managed.Count -lt 1 -or $managed.Count -gt 32) { throw 'Specify 1-32 managed directories.' }

function Assert-ManagedPath {
    param([string]$Relative)
    if ([string]::IsNullOrWhiteSpace($Relative) -or
        $Relative.Contains('\') -or $Relative.Contains(':') -or
        $Relative.StartsWith('/') -or $Relative.EndsWith('/') -or
        $Relative -notmatch '^Assets/') { throw "Invalid managed directory: $Relative" }
    $parts = $Relative.Split('/')
    if ($parts.Count -lt 4 -or @($parts | Where-Object { $_ -in @('','.','..') -or $_.EndsWith('.') -or $_.EndsWith(' ') }).Count -gt 0) {
        throw "Unsafe managed directory: $Relative"
    }
    if ($Relative -ieq 'Assets/Scripts/AssetPack/Editor' -or
        $Relative -ieq 'Assets/Scripts/AssetPack/Authoring' -or
        $Relative -ieq 'Assets/Editor/RailroaderADKToolsInstaller' -or
        $Relative.StartsWith('Assets/Editor/RailroaderADKToolsInstaller/', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to package a shared/installer directory: $Relative"
    }
}
$seenDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in $managed) {
    Assert-ManagedPath $relative
    if (-not $seenDirs.Add($relative)) { throw "Duplicate managed directory: $relative" }
}
for ($i = 0; $i -lt $managed.Count; $i++) {
    for ($j = $i + 1; $j -lt $managed.Count; $j++) {
        if ($managed[$i].StartsWith($managed[$j] + '/', [StringComparison]::OrdinalIgnoreCase) -or
            $managed[$j].StartsWith($managed[$i] + '/', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Nested managed directories: $($managed[$i]) / $($managed[$j])"
        }
    }
}

$sourceExtensions = @('.cs', '.asmdef', '.asmref', '.rsp', '.uxml', '.uss', '.shader', '.compute', '.hlsl', '.cginc')
$files = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)

function Add-File {
    param([string]$FullName)
    if (-not (Test-Path -LiteralPath $FullName -PathType Leaf)) { return }
    $absolute = (Resolve-Path -LiteralPath $FullName).ProviderPath
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File outside project root: $absolute"
    }
    $relative = $absolute.Substring($prefix.Length).Replace('\','/')
    $files[$relative] = $absolute
}

foreach ($relative in $managed) {
    $folder = Join-Path $project ($relative.Replace('/', [string]$separator))
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw "Missing managed folder: $relative"
    }
    $sources = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object {
        $sourceExtensions -contains $_.Extension.ToLowerInvariant()
    })
    if ($sources.Count -eq 0) { throw "No source files in: $relative" }
    foreach ($source in $sources) {
        Add-File $source.FullName
        $fileMeta = $source.FullName + '.meta'
        if (-not (Test-Path -LiteralPath $fileMeta -PathType Leaf)) {
            throw "Missing Unity .meta for: $($source.FullName)"
        }
        Add-File $fileMeta
        # Keep metadata for nested tool-owned directories, not their shared parents.
        $dir = $source.Directory.FullName
        while ($dir.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase)) {
            Add-File ($dir + '.meta')
            if ($dir.Equals($folder, [StringComparison]::OrdinalIgnoreCase)) { break }
            $dir = [IO.Directory]::GetParent($dir).FullName
        }
    }
}

if (-not [IO.Path]::IsPathRooted($OutputDir)) { $OutputDir = Join-Path $project $OutputDir }
[IO.Directory]::CreateDirectory($OutputDir) | Out-Null
$zipName = "RailroaderADKTools_v3.0_${Version}_SourceOnly.zip"
$zipPath = Join-Path $OutputDir $zipName
$tempPath = $zipPath + '.tmp'
$release = [ordered]@{ schemaVersion = 1; version = $Version; managedDirectories = @($managed) }
$releaseJson = $release | ConvertTo-Json -Depth 5

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force }
$archive = $null
try {
    $archive = [IO.Compression.ZipFile]::Open($tempPath, [IO.Compression.ZipArchiveMode]::Create)
    $manifest = $archive.CreateEntry('ADKTools.Release.json', [IO.Compression.CompressionLevel]::Optimal)
    $stream = $manifest.Open()
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($releaseJson)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
    foreach ($relative in @($files.Keys | Sort-Object)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $files[$relative], $relative, [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally { if ($null -ne $archive) { $archive.Dispose() } }

$archive = [IO.Compression.ZipFile]::OpenRead($tempPath)
try {
    if ($archive.Entries.Count -ne $files.Count + 1) { throw 'ZIP verification: wrong entry count.' }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        if (-not $names.Add($entry.FullName)) { throw "ZIP contains duplicate entry: $($entry.FullName)" }
        if ($entry.FullName -ne 'ADKTools.Release.json' -and -not $files.ContainsKey($entry.FullName)) {
            throw "ZIP contains unexpected entry: $($entry.FullName)"
        }
    }
    if (-not $names.Contains('ADKTools.Release.json')) { throw 'ZIP is missing the manifest.' }
} finally { $archive.Dispose() }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Move-Item -LiteralPath $tempPath -Destination $zipPath

$sourceCount = @($files.Keys | Where-Object { -not $_.EndsWith('.meta', [StringComparison]::OrdinalIgnoreCase) }).Count
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host "  Managed directories: $($managed.Count)"
Write-Host "  Source files: $sourceCount"
Write-Host "  Total asset files: $($files.Count), plus release manifest"
Write-Host '  Install from Unity: Railroader > ADK Tools > Install Update...' -ForegroundColor Cyan
