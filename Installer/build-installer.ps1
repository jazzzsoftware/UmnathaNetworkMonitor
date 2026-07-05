<#
.SYNOPSIS
    Publishes Umnatha Network Monitor (self-contained, x64) and builds the Inno Setup installer.

.DESCRIPTION
    The published output bundles the .NET 10 runtime and the Windows App SDK runtime
    (WindowsAppSDKSelfContained=true), so the resulting setup.exe needs no prerequisites
    on the target machine.

.PARAMETER Version
    Overrides the version. When omitted, the version is read from <Version> in
    NetworkMonitor.csproj — the single source of truth shared with the About box.

.PARAMETER SkipPublish
    Reuse an existing publish folder instead of re-running dotnet publish.

.EXAMPLE
    .\build-installer.ps1 -Version 1.2.0
#>

param(
    [string]$Version = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot    = Split-Path -Parent $scriptDir
$projectPath = Join-Path $repoRoot "NetworkMonitor\NetworkMonitor.csproj"
$issPath     = Join-Path $scriptDir "NetworkMonitor.iss"
$publishDir  = Join-Path $repoRoot "NetworkMonitor\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\publish"

# Single source of truth: read <Version> from the csproj unless overridden.
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content $projectPath
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "No <Version> found in $projectPath and no -Version was supplied."
    }
}
Write-Host "Version: $Version" -ForegroundColor Cyan

if (-not $SkipPublish) {
    Write-Host "Publishing self-contained x64 Release build..." -ForegroundColor Cyan
    dotnet publish $projectPath -c Release -r win-x64 -p:Platform=x64 --self-contained --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path (Join-Path $publishDir "NetworkMonitor.exe"))) {
    throw "Publish output not found at: $publishDir"
}

# Locate the Inno Setup compiler.
$iscc = (Get-Command "ISCC.exe" -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
}
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 from https://jrsoftware.org/isdl.php"
}

Write-Host "Compiling installer (version $Version)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "/DMyPublishDir=$publishDir" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$outFile = Join-Path $scriptDir ("Output\Umnatha Network Monitor v$Version.exe")
Write-Host "Installer built: $outFile" -ForegroundColor Green
