<#
.SYNOPSIS
    Full build script for rnet-kit: RemoteNET submodule then the rnet-kit solution.
.DESCRIPTION
    1. Locates VS toolchain via vswhere.
    2. Builds the detours.net native library (cmake + msbuild via vcvarsall).
    3. Builds RemoteNET.sln (Release|Mixed config).
    4. Creates the dist/ directory junctions required for the Merged build.
    5. Builds Merged.sln (Release|Mixed) via msbuild.
.NOTES
    Build logs are written to build_*.log files in the repo root.
    The script is idempotent: cmake and junction creation are skipped when already done.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Step([string]$Msg) {
    Write-Host "`n>>> $Msg" -ForegroundColor Cyan
}

function Invoke-CmdChain([string]$Chain, [string]$LogFile) {
    # Runs a cmd /c chain and streams output to $LogFile.
    # On failure, prints the last 40 lines of the log and stops the script.
    cmd /c $Chain > $LogFile 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED (exit $LASTEXITCODE). Last 40 lines of ${LogFile}:" -ForegroundColor Red
        Get-Content $LogFile -ErrorAction SilentlyContinue | Select-Object -Last 40
        Write-Error "Build step failed."
        exit $LASTEXITCODE
    }
}

function New-JunctionIfMissing([string]$LinkPath, [string]$TargetPath) {
    if (Test-Path $LinkPath) { return }
    $parent = Split-Path $LinkPath
    if ($parent -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    New-Item -ItemType Junction -Path $LinkPath -Target $TargetPath | Out-Null
    $rel = $LinkPath.Replace($repoRoot, '').TrimStart('\')
    Write-Host "    ++ Junction created: $rel" -ForegroundColor DarkCyan
}

# ---------------------------------------------------------------------------
# Locate vcvarsall.bat
# ---------------------------------------------------------------------------

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    Write-Error "vswhere.exe not found. Install Visual Studio with C++ workload."
    exit 1
}

# Use the latest VS installation that has the C++ x64 native tools.
# Falls back to any installation if no C++-specific one is found.
$vsInstallPath = & $vswhere -latest -products '*' `
    -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 `
    -property installationPath 2>$null |
    Select-Object -First 1

if (-not $vsInstallPath) {
    Write-Host "No VS with C++ x64 tools found; trying any VS installation..." -ForegroundColor Yellow
    $vsInstallPath = & $vswhere -latest -products '*' -property installationPath 2>$null |
        Select-Object -First 1
}

if (-not $vsInstallPath) {
    Write-Error "No Visual Studio installation found via vswhere."
    exit 1
}

$vcvarsall = Join-Path $vsInstallPath "VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $vcvarsall)) {
    Write-Error "vcvarsall.bat not found at '$vcvarsall'. Ensure the C++ workload is installed."
    exit 1
}

Write-Host "VS toolchain : $vcvarsall" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# Step 1 — Build detours native library (cmake + msbuild)
# ---------------------------------------------------------------------------

$srcDir          = Join-Path $repoRoot "RemoteNET\src"
$detoursBuildDir = Join-Path $srcDir "detours_build"
$detoursProjFile = Join-Path $detoursBuildDir "ALL_BUILD.vcxproj"
$detoursLog      = Join-Path $repoRoot "build_detours.log"

if (-not (Test-Path $detoursProjFile)) {
    Write-Step "Running cmake for detours.net..."
    New-Item -ItemType Directory -Force -Path $detoursBuildDir | Out-Null
    Invoke-CmdChain (
        "`"$vcvarsall`" x64 && " +
        "cd /d `"$detoursBuildDir`" && " +
        "cmake ..\detours.net"
    ) $detoursLog
    Write-Host "    cmake OK" -ForegroundColor Green
}
else {
    Write-Host "cmake already run, skipping." -ForegroundColor DarkGray
}

Write-Step "Building detours native library..."
Invoke-CmdChain (
    "`"$vcvarsall`" x64 && " +
    "cd /d `"$detoursBuildDir`" && " +
    "msbuild ALL_BUILD.vcxproj /t:restore,build /p:Configuration=Release /m /nologo /verbosity:minimal"
) $detoursLog
Write-Host "    detours OK" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 2 — Build RemoteNET.sln (Release|Mixed)
# ---------------------------------------------------------------------------
#
# The solution contains C++ and C# projects, so its platform is "Mixed" not "Any CPU".
# vcvarsall sets PLATFORM=x64 in the environment; we clear it immediately (set PLATFORM=)
# so MSBuild doesn't compose an invalid "Release|x64" configuration name.

$remoteNetSln = Join-Path $srcDir "RemoteNET.sln"
$remoteNetLog = Join-Path $repoRoot "build_remotenet.log"

Write-Step "Building RemoteNET.sln (Release|Mixed)..."
Invoke-CmdChain (
    "`"$vcvarsall`" x64 && " +
    "set PLATFORM= && " +
    "msbuild `"$remoteNetSln`" /t:restore,build /p:Configuration=Release /p:Platform=Mixed /m /nologo /verbosity:minimal"
) $remoteNetLog
Write-Host "    RemoteNET.sln OK" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 4 — Create dist directory junctions
# ---------------------------------------------------------------------------
#
# RemoteNET.csproj uses $(SolutionDir)\dist\$(Configuration)\... for its Copy/Zip targets.
# When built as a dependency from rnet-kit.sln, a "dotnet clean" in ScubaDiver.API's
# PreBuild target resets the configuration to Debug. The native artefacts only exist
# under RemoteNET\src\dist\Release, so we create junctions to bridge the gap.

Write-Step "Creating directory junctions..."

$srcDist = Join-Path $srcDir "dist\Release"

New-JunctionIfMissing (Join-Path $repoRoot "dist\Debug\ScubaDivers")  (Join-Path $srcDist "ScubaDivers")
New-JunctionIfMissing (Join-Path $repoRoot "dist\Debug\Win32")         (Join-Path $srcDist "Win32")
New-JunctionIfMissing (Join-Path $repoRoot "dist\Debug\x64")           (Join-Path $srcDist "x64")
New-JunctionIfMissing (Join-Path $repoRoot "dist\Release\Win32")       (Join-Path $srcDist "Win32")
New-JunctionIfMissing (Join-Path $repoRoot "dist\Release\x64")         (Join-Path $srcDist "x64")

# InjectableDummy is built as Debug when triggered from rnet-kit.sln; point its
# Debug output directory at the Release output built by RemoteNET.sln above.
$injBase    = Join-Path $srcDir "InjectableDummy\bin"
$injRelease = Join-Path $injBase "Release\net6.0-windows"
$injDebug   = Join-Path $injBase "Debug\net6.0-windows"

if (Test-Path $injRelease) {
    New-JunctionIfMissing $injDebug $injRelease
}
else {
    Write-Warning "InjectableDummy Release output not found at '$injRelease'. Skipping junction."
}

Write-Host "    Junctions OK" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 4 — Build Merged.sln
# ---------------------------------------------------------------------------
#
# Merged.sln contains C++ and C# projects, so we use msbuild via vcvarsall
# with Platform=Mixed, same as the RemoteNET.sln build above.

$rnetKitSln = Join-Path $repoRoot "Merged.sln"
$rnetKitLog = Join-Path $repoRoot "build_rnetkit.log"

Write-Step "Building Merged.sln (Release|Mixed)..."
Invoke-CmdChain (
    "`"$vcvarsall`" x64 && " +
    "set PLATFORM= && " +
    "msbuild `"$rnetKitSln`" /t:restore,build /p:Configuration=Release /p:Platform=Mixed /m /nologo /verbosity:minimal"
) $rnetKitLog
Write-Host "    Merged.sln OK" -ForegroundColor Green

# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "======================================" -ForegroundColor Green
Write-Host "  All builds completed successfully." -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green
