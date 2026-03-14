<#
.SYNOPSIS
    All-in-one build script for rnet-kit, matching the GitHub Actions CI pipeline.
.DESCRIPTION
    Builds the entire repo: cmake detours, restore, build DetoursNet (ALL_BUILD),
    build RemoteNET, then build all Kit projects.
.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release).
.PARAMETER SkipSubmoduleInit
    Skip 'git submodule update --init --recursive'.
.PARAMETER Publish
    Also publish rnet-kit and RemoteNetSpy as self-contained win-x64 after building.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipSubmoduleInit,
    [switch]$Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$SolutionName = 'Merged.sln'
$NuGetConfigSource = Join-Path $RepoRoot 'rnet-repl\RemoteNetRepl\nuget.config'
$NuGetConfigDest = Join-Path $RepoRoot 'nuget.config'
$DetoursSrcDir = Join-Path $RepoRoot 'RemoteNET\src'
$DetoursBuildDir = Join-Path $DetoursSrcDir 'detours_build'
$DetoursCMake = Join-Path $DetoursSrcDir 'detours.net'

$KitProjects = @(
    'RemoteNetSpy\RemoteNetSpy.csproj',
    'rnet-kit\rnet-kit.csproj'
)

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) {
    Write-Host "`n>>> $msg" -ForegroundColor Cyan
}

function Invoke-Cmd {
    param([string]$Description, [scriptblock]$Command)
    Write-Step $Description
    & $Command
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "FAILED: $Description (exit code $LASTEXITCODE)"
    }
}

function Find-MSBuild {
    # Try vswhere first (VS 2017+)
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($installPath) {
            $msbuild = Join-Path $installPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
            if (Test-Path $msbuild) { return $msbuild }
            $msbuild = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $msbuild) { return $msbuild }
        }
    }
    # Fallback: try PATH
    $inPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    throw "MSBuild not found. Install Visual Studio or ensure msbuild.exe is on PATH."
}

function Find-CMake {
    $inPath = Get-Command cmake.exe -ErrorAction SilentlyContinue
    if ($inPath) { return $inPath.Source }

    # Check common VS locations
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -property installationPath 2>$null
        if ($installPath) {
            $cmake = Join-Path $installPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
            if (Test-Path $cmake) { return $cmake }
        }
    }
    throw "cmake not found. Install CMake or ensure cmake.exe is on PATH."
}

# ── Resolve tools ────────────────────────────────────────────────────────────

$msbuild = Find-MSBuild
$cmake   = Find-CMake
Write-Host "MSBuild : $msbuild"
Write-Host "CMake   : $cmake"
Write-Host "Config  : $Configuration"

Push-Location $RepoRoot
try {
    # ── 1. Submodules ────────────────────────────────────────────────────────
    if (-not $SkipSubmoduleInit) {
        Invoke-Cmd 'Initializing submodules' {
            git submodule update --init --recursive
        }
    }

    # ── 2. Copy NuGet config ─────────────────────────────────────────────────
    Write-Step 'Copying custom NuGet config to repo root'
    Copy-Item -Path $NuGetConfigSource -Destination $NuGetConfigDest -Force

    # ── 3. CMake for Detours ─────────────────────────────────────────────────
    Invoke-Cmd 'Running cmake for detours.net' {
        if (-not (Test-Path $DetoursBuildDir)) {
            New-Item -ItemType Directory -Path $DetoursBuildDir | Out-Null
        }
        Push-Location $DetoursBuildDir
        try {
            & $cmake $DetoursCMake
            if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw "cmake failed" }
        } finally { Pop-Location }
    }

    # ── 4. Restore Solution ──────────────────────────────────────────────────
    Invoke-Cmd 'Restoring Merged.sln' {
        & $msbuild $SolutionName /t:Restore "/p:Configuration=$Configuration" "/p:RestoreConfigFile=$NuGetConfigDest" /m /nologo /v:minimal
    }

    # ── 5. Build DetoursNet (ALL_BUILD) ──────────────────────────────────────
    Invoke-Cmd 'Building DetoursNet (ALL_BUILD)' {
        & $msbuild $SolutionName "-target:RemoteNET_Dir\DetoursNet\ALL_BUILD:rebuild" "/p:Configuration=$Configuration" '/property:Platform=Mixed' "/p:RestoreConfigFile=$NuGetConfigDest" /m /nologo /v:minimal
    }

    # ── 6. Clean stale resources & Build RemoteNET ─────────────────────────
    Write-Step 'Cleaning stale resources'
    $resDir = Join-Path $RepoRoot 'RemoteNET\src\RemoteNET\Resources'
    if (Test-Path $resDir) {
        Get-ChildItem $resDir -Include '*.exe','*.dll','*.pdb','*.json','*.zip' -File | Remove-Item -Force
    }

    Invoke-Cmd 'Building RemoteNET' {
        & $msbuild $SolutionName "-target:RemoteNET_Dir\RemoteNET:rebuild" "/p:Configuration=$Configuration" '/property:Platform=Mixed' "/p:RestoreConfigFile=$NuGetConfigDest" /m /nologo /v:minimal
    }

    # ── 7. Build Kit projects ────────────────────────────────────────────────
    foreach ($proj in $KitProjects) {
        $projPath = Join-Path $RepoRoot $proj
        $projName = Split-Path $proj -Leaf

        Invoke-Cmd "Restoring $projName" {
            dotnet restore $projPath --configfile $NuGetConfigDest
        }

        Invoke-Cmd "Building $projName" {
            dotnet build $projPath --configuration $Configuration --no-restore
        }
    }

    # ── 8. Publish (optional) ────────────────────────────────────────────────
    if ($Publish) {
        Invoke-Cmd 'Publishing rnet-kit (win-x64, self-contained)' {
            dotnet publish (Join-Path $RepoRoot 'rnet-kit\rnet-kit.csproj') `
                -c $Configuration -r win-x64 --self-contained true `
                /p:ErrorOnDuplicatePublishOutputFiles=false
        }
        Invoke-Cmd 'Publishing RemoteNetSpy (win-x64, self-contained)' {
            dotnet publish (Join-Path $RepoRoot 'RemoteNetSpy\RemoteNetSpy.csproj') `
                -c $Configuration -r win-x64 --self-contained true `
                /p:ErrorOnDuplicatePublishOutputFiles=false
        }
    }

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host " BUILD SUCCEEDED ($Configuration)" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
}
catch {
    Write-Host "`n========================================" -ForegroundColor Red
    Write-Host " BUILD FAILED" -ForegroundColor Red
    Write-Host " $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
