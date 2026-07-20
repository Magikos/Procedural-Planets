<#
.SYNOPSIS
Lists the newest F10 capture bundle(s) under local-only/debug-screenshots with mode names
and a sidecar summary per capture.

.DESCRIPTION
A "bundle" is one F10 capture run: the pipeline walks every mode in the active capture set
back-to-back, so files whose filename timestamps are close together belong to one run.
This script clusters F10-*.txt sidecars by timestamp gap (default 120 s) and prints the
newest bundle(s): capture set, camera position, sun elevation, and per-mode key lines.

Read-only. Never writes into the capture directory.

.EXAMPLE
powershell -File Get-LatestCaptureBundle.ps1
.EXAMPLE
powershell -File Get-LatestCaptureBundle.ps1 -Bundles 3 -Detail
#>
[CmdletBinding()]
param(
    [string]$CaptureDir,
    [int]$Bundles = 1,
    [int]$GapSeconds = 120,
    [switch]$Detail
)

$ErrorActionPreference = 'Stop'

if (-not $CaptureDir) {
    # scripts/ -> pp-diagnostics-and-tooling/ -> .agent-skills/ -> repo root
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $CaptureDir = Join-Path $repoRoot 'local-only\debug-screenshots'
}

if (-not (Test-Path $CaptureDir)) {
    Write-Output "Capture directory not found: $CaptureDir"
    Write-Output "No F10 captures have been taken yet (the game creates it on first F10)."
    exit 0
}

# Filename shape (DebugCapturePipeline.SaveScreenshot):
#   F10-{modeId}-{modeName}-{yyyyMMdd-HHmmss-fff}.txt
#   F10-{scriptPrefix}-{modeId}-{modeName}-{yyyyMMdd-HHmmss-fff}.txt  (console-script runs)
$pattern = '^F10-(?:(?<prefix>.+?)-)?(?<modeid>[a-z]+\.\d+)-(?<modename>.+)-(?<ts>\d{8}-\d{6}-\d{3})\.txt$'

$entries = @()
foreach ($file in Get-ChildItem -Path $CaptureDir -Filter 'F10-*.txt') {
    if ($file.Name -match $pattern) {
        $entries += [pscustomobject]@{
            File     = $file
            Prefix   = $Matches['prefix']
            ModeId   = $Matches['modeid']
            ModeName = $Matches['modename']
            Time     = [datetime]::ParseExact($Matches['ts'], 'yyyyMMdd-HHmmss-fff', $null)
        }
    }
}

if ($entries.Count -eq 0) {
    Write-Output "No F10-*.txt sidecars matched in $CaptureDir"
    exit 0
}

$entries = $entries | Sort-Object Time

# Cluster into bundles by timestamp gap.
$bundleList = New-Object System.Collections.ArrayList
$current = New-Object System.Collections.ArrayList
$prevTime = $null
foreach ($e in $entries) {
    if ($null -ne $prevTime -and ($e.Time - $prevTime).TotalSeconds -gt $GapSeconds) {
        [void]$bundleList.Add($current)
        $current = New-Object System.Collections.ArrayList
    }
    [void]$current.Add($e)
    $prevTime = $e.Time
}
[void]$bundleList.Add($current)

function Get-SidecarValue([string[]]$lines, [string]$prefix) {
    foreach ($line in $lines) {
        if ($line.StartsWith($prefix)) { return $line.Substring($prefix.Length).Trim() }
    }
    return $null
}

$take = [Math]::Min($Bundles, $bundleList.Count)
for ($i = $bundleList.Count - $take; $i -lt $bundleList.Count; $i++) {
    $bundle = $bundleList[$i]
    $first = $bundle[0]
    $lines = Get-Content $first.File.FullName
    $set = Get-SidecarValue $lines 'CaptureSet:'
    $pos = Get-SidecarValue $lines 'Position:'
    $sun = Get-SidecarValue $lines 'SunElevationDeg:'
    $fps = Get-SidecarValue $lines 'FPS:'

    Write-Output ('=' * 78)
    Write-Output ("BUNDLE  {0:yyyy-MM-dd HH:mm:ss}  ({1} captures)" -f $first.Time, $bundle.Count)
    Write-Output "  CaptureSet: $set"
    Write-Output "  Camera:     $pos   SunElevationDeg: $sun   FPS: $fps"
    if ($first.Prefix) { Write-Output "  ScriptPrefix: $($first.Prefix)" }
    Write-Output ''
    foreach ($e in $bundle) {
        $png = [System.IO.Path]::ChangeExtension($e.File.FullName, '.png')
        $hasPng = if (Test-Path $png) { 'png+txt' } else { 'txt only' }
        Write-Output ("  {0,-12} {1,-28} {2:HH:mm:ss.fff}  {3}" -f $e.ModeId, $e.ModeName, $e.Time, $hasPng)
        if ($Detail) {
            $ml = Get-Content $e.File.FullName
            foreach ($key in @('Whole frame:', 'Rolling:', 'CameraWeather:', 'Draw: emitted=')) {
                foreach ($line in $ml) {
                    if ($line.StartsWith($key)) { Write-Output ("               {0}" -f $line.Trim()); break }
                }
            }
        }
    }
    Write-Output "  Sidecar of first capture: $($first.File.FullName)"
}
