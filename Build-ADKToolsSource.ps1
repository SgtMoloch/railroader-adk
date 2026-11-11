#requires -Version 5.1
<#
.SYNOPSIS
    Package Railroader ADK Tools source files for an existing Unity ADK project.
.DESCRIPTION
    Produces a drop-in ZIP rooted at Assets/Scripts/AssetPack/... containing only
    the toolkit's source files, assembly definitions, and matching Unity .meta files.
    Does not include models, textures, prefabs, game assets, docs, or build output.

    Place this script in the root of the railroader-adk checkout (beside Assets),
    or pass -ProjectRoot to point to that checkout.
.EXAMPLE
    .\Build-ADKToolsSource.ps1 -Version Beta1.5.8
.EXAMPLE
    .\Build-ADKToolsSource.ps1 -ProjectRoot 'C:\Projects\railroader-adk' -Version Beta1.5.9
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string] $ProjectRoot = $PSScriptRoot,

    [Parameter()]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $Version = 'dev',

    [Parameter()]
    [string] $OutputDir = 'dist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$project = (Resolve-Path -LiteralPath $ProjectRoot -ErrorAction Stop).ProviderPath
$project = $project.TrimEnd([char[]]@('\', '/'))
$rootPrefix = $project + [System.IO.Path]::DirectorySeparatorChar

# Deliberately restricted to OUR tool directories: do not ship/overwrite the
# stock ADK's scripts or any of CLB's other asset-pack files.
$sourceDirectories = @(
    'Assets/Scripts/AssetPack/Editor/AssetTools',
    'Assets/Scripts/AssetPack/Authoring/Colorizer'
)

# Extend these if the toolkit starts using other code-based Unity assets.
$sourceExtensions = @('.cs', '.asmdef', '.asmref', '.rsp', '.uxml', '.uss', '.shader', '.compute', '.hlsl', '.cginc')

# Path-keyed map prevents duplicate ZIP entries on case-insensitive Windows.
$packageFiles = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)

function Add-PackageFile {
    param([Parameter(Mandatory)][string] $FullName)

    if (-not (Test-Path -LiteralPath $FullName -PathType Leaf)) { return }
    $absolute = (Resolve-Path -LiteralPath $FullName).ProviderPath
    if (-not $absolute.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to package a file outside the project: $absolute"
    }

    $relative = $absolute.Substring($rootPrefix.Length).Replace('\', '/')
    $packageFiles[$relative] = $absolute
}

foreach ($relativeDir in $sourceDirectories) {
    $folder = Join-Path -Path $project -ChildPath ($relativeDir.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw "Missing toolkit source directory: $relativeDir`nRun from the repository root, or specify -ProjectRoot."
    }

    $sources = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object {
        $sourceExtensions -contains $_.Extension.ToLowerInvariant()
    })
    if ($sources.Count -eq 0) { throw "No source files found in $relativeDir" }

    foreach ($source in $sources) {
        Add-PackageFile $source.FullName

        $fileMeta = $source.FullName + '.meta'
        if (-not (Test-Path -LiteralPath $fileMeta -PathType Leaf)) {
            throw "Missing Unity .meta file: $fileMeta`nOpen the project in Unity to generate it, then commit it before packaging."
        }
        Add-PackageFile $fileMeta

        # Include .meta for each toolkit-owned folder, keeping Unity GUIDs stable.
        $directory = $source.Directory.FullName
        while ($directory.StartsWith($folder, [System.StringComparison]::OrdinalIgnoreCase)) {
            $folderMeta = $directory + '.meta'
            if (Test-Path -LiteralPath $folderMeta -PathType Leaf) {
                Add-PackageFile $folderMeta
            }
            if ($directory.Equals($folder, [System.StringComparison]::OrdinalIgnoreCase)) { break }
            $directory = [System.IO.Directory]::GetParent($directory).FullName
        }
    }
}

# This parent folder is owned by the toolkit in the current ADK layout.
$authoringMeta = Join-Path $project 'Assets/Scripts/AssetPack/Authoring.meta'
Add-PackageFile $authoringMeta

if ($packageFiles.Count -eq 0) { throw 'No files found to package.' }

if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $project $OutputDir
}
[System.IO.Directory]::CreateDirectory($OutputDir) | Out-Null

$zipName = "RailroaderADKTools_v3.0_${Version}_SourceOnly.zip"
$zipPath = Join-Path $OutputDir $zipName
$tempZip = Join-Path $OutputDir ($zipName + '.tmp')

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $tempZip) { Remove-Item -LiteralPath $tempZip -Force }
$archive = $null
try {
    $archive = [System.IO.Compression.ZipFile]::Open($tempZip, [System.IO.Compression.ZipArchiveMode]::Create)
    foreach ($relative in @($packageFiles.Keys | Sort-Object)) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $packageFiles[$relative],
            $relative,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    if ($null -ne $archive) { $archive.Dispose() }
}

# Check contents before replacing a previous package.
$verified = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
try {
    if ($verified.Entries.Count -ne $packageFiles.Count) {
        throw "Archive verification failed: expected $($packageFiles.Count) files, found $($verified.Entries.Count)."
    }
    foreach ($entry in $verified.Entries) {
        if (-not $packageFiles.ContainsKey($entry.FullName)) {
            throw "Unexpected archive path: $($entry.FullName)"
        }
    }
}
finally {
    $verified.Dispose()
}

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Move-Item -LiteralPath $tempZip -Destination $zipPath

$codeCount = @($packageFiles.Keys | Where-Object { -not $_.EndsWith('.meta', [System.StringComparison]::OrdinalIgnoreCase) }).Count
Write-Host "Created: $zipPath" -ForegroundColor Green
Write-Host "  Source files : $codeCount"
Write-Host "  Total files  : $($packageFiles.Count) (including Unity .meta)"
Write-Host '  Extract ZIP into the ROOT of an existing Railroader ADK project (beside Assets).' -ForegroundColor Cyan
Write-Host '  This updates ADK Tools scripts only; it does not install the base ADK.'
