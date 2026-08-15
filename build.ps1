<#
.SYNOPSIS
Builds and packages HzHitSoundRenderer on Windows.

.EXAMPLE
.\build.ps1

.EXAMPLE
.\build.ps1 -GameManagedDir "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"

.EXAMPLE
.\build.ps1 -DeployDir "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\Mods\HzHitSoundRenderer"
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$GameManagedDir = $env:ADOFAI_GAME_MANAGED_DIR,

    [string]$DotNetPath,

    [string]$DeployDir,

    [switch]$SkipPackage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-ManagedDirectory {
    param([string]$RequestedPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates += $RequestedPath
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ADOFAI_DIR)) {
        $candidates += Join-Path $env:ADOFAI_DIR `
            "A Dance of Fire and Ice_Data\Managed"
    }

    $candidates += @(
        "C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed",
        "C:\Program Files\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"
    )

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath (Join-Path $candidate "Assembly-CSharp.dll") -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw @"
ADOFAI's Managed directory was not found.
Pass -GameManagedDir or set ADOFAI_GAME_MANAGED_DIR.
Example:
  .\build.ps1 -GameManagedDir "D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed"
"@
}

function Resolve-DotNet {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) {
            throw "dotnet was not found at '$RequestedPath'."
        }
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $command = Get-Command "dotnet" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw @"
dotnet was not found.
Install the .NET SDK, or pass -DotNetPath explicitly.
"@
}

function Assert-GameReferences {
    param([string]$ManagedDirectory)

    $requiredFiles = @(
        "mscorlib.dll",
        "netstandard.dll",
        "Assembly-CSharp.dll",
        "RDTools.dll",
        "UnityEngine.dll",
        "UnityEngine.AudioModule.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.IMGUIModule.dll",
        "UnityModManager\UnityModManager.dll",
        "UnityModManager\0Harmony.dll"
    )

    $missing = @()
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $ManagedDirectory $relativePath) -PathType Leaf)) {
            $missing += $relativePath
        }
    }

    if ($missing.Count -gt 0) {
        throw "Required game references are missing:`n  $($missing -join "`n  ")"
    }
}

function Get-ModVersion {
    param([string]$ProjectPath)

    $source = [System.IO.File]::ReadAllText($ProjectPath)
    $match = [regex]::Match(
        $source,
        '<Version>\s*(?<version>\d+\.\d+\.\d+(?:[-+][^<]+)?)\s*</Version>'
    )
    if (-not $match.Success) {
        throw "Could not read the version from '$ProjectPath'."
    }
    return $match.Groups["version"].Value
}

function Write-ModInfo {
    param(
        [string]$DestinationPath,
        [string]$Version
    )

    $modInfo = [ordered]@{
        Id = "HzHitSoundRenderer"
        DisplayName = "Hz Hit Sound Renderer"
        Author = "kineticnapier"
        Version = $Version
        AssemblyName = "HzHitSoundRenderer.dll"
        EntryMethod = "HzHitSoundRenderer.Main.Load"
        HomePage = "https://github.com/kineticnapier/HzHitSoundRenderer"
    }

    $json = $modInfo | ConvertTo-Json
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText(
        $DestinationPath,
        $json + [Environment]::NewLine,
        $utf8WithoutBom
    )
}

function Import-ZipAssemblies {
    # ZipArchiveMode and CompressionLevel are not guaranteed to be loaded by
    # Windows PowerShell 5.1 when only FileSystem is requested.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

function New-UMMPackage {
    param(
        [string]$DllPath,
        [string]$InfoPath,
        [string]$ZipPath
    )

    Import-ZipAssemblies
    $archive = [System.IO.Compression.ZipFile]::Open(
        $ZipPath,
        [System.IO.Compression.ZipArchiveMode]::Create
    )
    try {
        $packageFiles = @(
            [ordered]@{
                Source = $DllPath
                Entry = "HzHitSoundRenderer/HzHitSoundRenderer.dll"
            },
            [ordered]@{
                Source = $InfoPath
                Entry = "HzHitSoundRenderer/Info.json"
            }
        )

        foreach ($packageFile in $packageFiles) {
            $entry = $archive.CreateEntry(
                $packageFile.Entry,
                [System.IO.Compression.CompressionLevel]::Optimal
            )
            $sourceStream = [System.IO.File]::OpenRead($packageFile.Source)
            try {
                $destinationStream = $entry.Open()
                try {
                    $sourceStream.CopyTo($destinationStream)
                }
                finally {
                    $destinationStream.Dispose()
                }
            }
            finally {
                $sourceStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-PackageContents {
    param([string]$ZipPath)

    Import-ZipAssemblies
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $required = @(
            "HzHitSoundRenderer/HzHitSoundRenderer.dll",
            "HzHitSoundRenderer/Info.json"
        )
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        $backslashEntries = @($entries | Where-Object { $_.Contains("\") })
        $missing = @($required | Where-Object { $entries -notcontains $_ })
        $unexpected = @($entries | Where-Object { $required -notcontains $_ })
        $duplicates = @(
            $entries |
                Group-Object |
                Where-Object { $_.Count -gt 1 } |
                ForEach-Object { $_.Name }
        )

        if ($backslashEntries.Count -gt 0 -or
            $missing.Count -gt 0 -or
            $unexpected.Count -gt 0 -or
            $duplicates.Count -gt 0) {
            throw @"
Invalid package contents.
Backslash entry names (UMM requires '/'):
  $($backslashEntries -join "`n  ")
Missing:
  $($missing -join "`n  ")
Unexpected:
  $($unexpected -join "`n  ")
Duplicates:
  $($duplicates -join "`n  ")
"@
        }
    }
    finally {
        $archive.Dispose()
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptRoot "HzHitSoundRenderer.csproj"
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "HzHitSoundRenderer.csproj was not found at '$projectPath'."
}

$managedDirectory = Resolve-ManagedDirectory -RequestedPath $GameManagedDir
$resolvedDotNet = Resolve-DotNet -RequestedPath $DotNetPath
$version = Get-ModVersion -ProjectPath $projectPath

Assert-GameReferences -ManagedDirectory $managedDirectory

Write-Host "Building HzHitSoundRenderer v$version ($Configuration)"
Write-Host "Project : $projectPath"
Write-Host "Managed : $managedDirectory"
Write-Host "dotnet  : $resolvedDotNet"

$dotnetArguments = @(
    "build",
    $projectPath,
    "--configuration", $Configuration,
    "--nologo",
    "--verbosity", "minimal",
    "/p:GameManagedDir=$managedDirectory"
)

& $resolvedDotNet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$outputDirectory = Join-Path $scriptRoot "bin\$Configuration\net48"
$dllPath = Join-Path $outputDirectory "HzHitSoundRenderer.dll"
$pdbPath = Join-Path $outputDirectory "HzHitSoundRenderer.pdb"
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Build succeeded, but '$dllPath' was not created."
}

if (-not $SkipPackage) {
    $artifactsDirectory = Join-Path $scriptRoot "artifacts"
    $packageDirectory = Join-Path $artifactsDirectory "HzHitSoundRenderer"
    $zipPath = Join-Path $artifactsDirectory "HzHitSoundRenderer-v$version.zip"

    New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $packageDirectory) {
        Remove-Item -LiteralPath $packageDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    $packageDll = Join-Path $packageDirectory "HzHitSoundRenderer.dll"
    $packageInfo = Join-Path $packageDirectory "Info.json"
    Copy-Item -LiteralPath $dllPath -Destination $packageDll
    Write-ModInfo -DestinationPath $packageInfo -Version $version

    New-UMMPackage -DllPath $packageDll -InfoPath $packageInfo -ZipPath $zipPath
    Assert-PackageContents -ZipPath $zipPath
    Write-Host "Package : $zipPath"
}

if (-not [string]::IsNullOrWhiteSpace($DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
    Copy-Item -LiteralPath $dllPath -Destination $DeployDir -Force
    if (Test-Path -LiteralPath $pdbPath -PathType Leaf) {
        Copy-Item -LiteralPath $pdbPath -Destination $DeployDir -Force
    }
    Write-ModInfo -DestinationPath (Join-Path $DeployDir "Info.json") `
        -Version $version
    Write-Host "Deployed: $DeployDir"
}

Write-Host "Build completed successfully."
