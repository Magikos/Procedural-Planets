<#
.SYNOPSIS
Live re-enumeration of debug modes, capture sets, and console commands from source.
Use this to re-verify the catalogs in SKILL.md whenever code may have drifted.

.DESCRIPTION
Greps the real registration sites:
  - DebugModeConstants.cs           (water/biome/terrain int mode values)
  - RegisterMode(...) call sites    (registered mode display names + categories)
  - Register*CaptureSet(...) sites  (capture-set display names)
  - CloudDebugState.View            (cloud shader debug views)
  - [ConsoleCommand] / [CommandPrefix] attributes (console surface)

Read-only. Output is text; pipe to a file in your scratchpad if you want to diff runs.

.EXAMPLE
powershell -File Find-DebugModes.ps1
.EXAMPLE
powershell -File Find-DebugModes.ps1 -Section capture-sets
#>
[CmdletBinding()]
param(
    [string]$RepoRoot,
    [ValidateSet('all', 'mode-constants', 'mode-names', 'capture-sets', 'cloud-views', 'console')]
    [string]$Section = 'all'
)

$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
$scripts = Join-Path $RepoRoot 'Assets\Scripts'

function Show([string]$title) {
    Write-Output ''
    Write-Output ('=' * 78)
    Write-Output $title
    Write-Output ('=' * 78)
}

if ($Section -eq 'all' -or $Section -eq 'mode-constants') {
    Show 'DebugModeConstants (int values behind the _OceanDebugMode shader global)'
    $file = Join-Path $scripts 'Core\Services\DebugModeConstants.cs'
    foreach ($line in Get-Content $file) {
        if ($line -match 'public const int (\w+) = (\d+);') {
            Write-Output ("  {0,3}  {1}" -f $Matches[2], $Matches[1])
        }
    }
}

if ($Section -eq 'all' -or $Section -eq 'mode-names') {
    Show 'Registered mode names (what debug.mode <name> accepts)'
    $files = @(
        'Core\Services\WaterDebugRegistration.cs',
        'Core\Services\BiomeDebugModule.cs',
        'Core\Services\TerrainDebugModule.cs',
        'Core\Services\CloudDebugModule.cs'
    )
    foreach ($rel in $files) {
        $file = Join-Path $scripts $rel
        if (-not (Test-Path $file)) { Write-Output "  MISSING: $rel"; continue }
        Write-Output "  --- $rel ---"
        foreach ($line in Get-Content $file) {
            if ($line -match 'RegisterMode\(registry,\s*(?:DebugModeConstants\.)?(\w+),\s*"([^"]+)",\s*"([^"]+)"\)') {
                Write-Output ("    {0,-34} name={1,-26} category={2}" -f $Matches[1], $Matches[2], $Matches[3])
            }
        }
    }
}

if ($Section -eq 'all' -or $Section -eq 'capture-sets') {
    Show 'Capture sets (what debug.capture-set <name> accepts; F7 cycles these)'
    Get-ChildItem -Path $scripts -Recurse -Filter '*.cs' | ForEach-Object {
        $path = $_.FullName
        $lineNo = 0
        foreach ($line in Get-Content $path) {
            $lineNo++
            if ($line -match 'Register(Default|Timed)?CaptureSet\([^,)]+,\s*"([^"]+)"') {
                $kind = $Matches[1]
                if (-not $kind) { $kind = 'Std' }
                $rel = $path.Substring($RepoRoot.Length + 1)
                Write-Output ("  {0,-32} [{1,-7}] {2}:{3}" -f "'$($Matches[2])'", $kind, $rel, $lineNo)
            }
        }
    }
}

if ($Section -eq 'all' -or $Section -eq 'cloud-views') {
    Show 'CloudDebugState.View (cloud.debug-mode values; drives _CloudDebugMode global)'
    $file = Join-Path $scripts 'Planet\Clouds\CloudDebugState.cs'
    $inEnum = $false
    foreach ($line in Get-Content $file) {
        if ($line -match 'public enum View') { $inEnum = $true; continue }
        if ($inEnum -and $line -match '^\s*\}') { $inEnum = $false; continue }
        if ($inEnum -and $line -match '^\s*(\w+)\s*=\s*(\d+)') {
            Write-Output ("  {0,2}  {1}" -f $Matches[2], $Matches[1])
        }
    }
}

if ($Section -eq 'all' -or $Section -eq 'console') {
    Show 'Console command surface ([CommandPrefix] owners and [ConsoleCommand] counts)'
    $prefixes = @{}
    $total = 0
    # A single file can hold multiple [CommandPrefix] classes (e.g. SurfacePathDebugCommands.cs
    # declares both "path" and "scorch"), so attribute each [ConsoleCommand] to the most
    # recent [CommandPrefix] seen above it in the file.
    Get-ChildItem -Path $scripts -Recurse -Filter '*.cs' | ForEach-Object {
        $activePrefix = '(no prefix)'
        foreach ($line in Get-Content $_.FullName) {
            if ($line -match '\[CommandPrefix\("([^"]+)"\)\]') { $activePrefix = $Matches[1]; continue }
            if ($line -match '\[ConsoleCommand\("([^"]+)"') {
                if (-not $prefixes.ContainsKey($activePrefix)) { $prefixes[$activePrefix] = New-Object System.Collections.ArrayList }
                [void]$prefixes[$activePrefix].Add($Matches[1])
                $total++
            }
        }
    }
    foreach ($key in ($prefixes.Keys | Sort-Object)) {
        Write-Output ("  {0,-18} ({1,2} cmds): {2}" -f $key, $prefixes[$key].Count, (($prefixes[$key] | Sort-Object) -join ', '))
    }
    Write-Output "  TOTAL [ConsoleCommand] attributes: $total"
}
