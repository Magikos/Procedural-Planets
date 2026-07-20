<#
.SYNOPSIS
Diffs two F10 capture metadata sidecars (.txt), highlighting changed values.

.DESCRIPTION
Sidecars are "Key: value" lines grouped under "--- Section ---" headers
(built by DebugCaptureMetadataBuilder + each module's AppendMetadata).
This script parses both files into Section/Key -> value maps and prints:
  ~ changed keys  (with token-level detail when the value is a k=v list)
  + keys only in B
  - keys only in A
Volatile keys (Image, Time) are skipped unless -All is passed.

.EXAMPLE
powershell -File Compare-CaptureSidecars.ps1 before.txt after.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$PathA,
    [Parameter(Mandatory = $true, Position = 1)][string]$PathB,
    [switch]$All
)

$ErrorActionPreference = 'Stop'
$volatileKeys = @('Image', 'Time', 'FPS', 'Pending')

function Parse-Sidecar([string]$path) {
    $map = [ordered]@{}
    $section = 'Header'
    $dupe = @{}
    foreach ($line in Get-Content $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('===')) { continue }
        if ($trimmed -match '^--- (.+) ---$') { $section = $Matches[1]; continue }
        $idx = $trimmed.IndexOf(':')
        if ($idx -lt 1) { continue }
        $key = "$section/$($trimmed.Substring(0, $idx))"
        $value = $trimmed.Substring($idx + 1).Trim()
        if ($map.Contains($key)) {
            if (-not $dupe.ContainsKey($key)) { $dupe[$key] = 1 }
            $dupe[$key]++
            $key = "$key#$($dupe[$key])"
        }
        $map[$key] = $value
    }
    return $map
}

# For "k1=v1, k2=v2" values, report only the tokens that changed.
function Get-TokenDiff([string]$a, [string]$b) {
    if ($a -notmatch '=' -or $b -notmatch '=') { return $null }
    $parse = {
        param($s)
        $t = [ordered]@{}
        foreach ($part in ($s -split ',\s*')) {
            $eq = $part.IndexOf('=')
            if ($eq -gt 0) { $t[$part.Substring(0, $eq).Trim()] = $part.Substring($eq + 1).Trim() }
        }
        $t
    }
    $ta = & $parse $a
    $tb = & $parse $b
    $diffs = @()
    foreach ($k in $ta.Keys) {
        if ($tb.Contains($k) -and $ta[$k] -ne $tb[$k]) { $diffs += "$k : $($ta[$k]) -> $($tb[$k])" }
    }
    foreach ($k in $tb.Keys) { if (-not $ta.Contains($k)) { $diffs += "$k : (new) $($tb[$k])" } }
    foreach ($k in $ta.Keys) { if (-not $tb.Contains($k)) { $diffs += "$k : (removed, was $($ta[$k]))" } }
    if ($diffs.Count -eq 0) { return $null }
    return $diffs
}

$a = Parse-Sidecar $PathA
$b = Parse-Sidecar $PathB

Write-Output "A: $PathA"
Write-Output "B: $PathB"
Write-Output ('-' * 78)

$changed = 0
foreach ($key in $a.Keys) {
    $bare = ($key -split '/')[-1] -replace '#\d+$', ''
    if (-not $All -and $volatileKeys -contains $bare) { continue }
    if ($b.Contains($key)) {
        if ($a[$key] -ne $b[$key]) {
            $changed++
            $tokens = Get-TokenDiff $a[$key] $b[$key]
            if ($tokens) {
                Write-Output "~ $key"
                foreach ($t in $tokens) { Write-Output "    $t" }
            }
            else {
                Write-Output "~ $key"
                Write-Output "    A: $($a[$key])"
                Write-Output "    B: $($b[$key])"
            }
        }
    }
    else {
        $changed++
        Write-Output "- $key   (only in A: $($a[$key]))"
    }
}
foreach ($key in $b.Keys) {
    $bare = ($key -split '/')[-1] -replace '#\d+$', ''
    if (-not $All -and $volatileKeys -contains $bare) { continue }
    if (-not $a.Contains($key)) {
        $changed++
        Write-Output "+ $key   (only in B: $($b[$key]))"
    }
}

Write-Output ('-' * 78)
if ($changed -eq 0) {
    Write-Output 'No differences (excluding volatile keys; pass -All to include Image/Time/FPS).'
}
else {
    Write-Output "$changed differing key(s)."
}
