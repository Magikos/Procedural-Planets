param(
    [string]$PackageRoot,
    [switch]$CheckOnly,
    [switch]$Reverse
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$patchPath = Join-Path $PSScriptRoot 'capture-async.patch'
if (-not $PackageRoot) {
    $packages = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Library/PackageCache') -Directory -Filter 'com.coplaydev.unity-mcp@*')
    if ($packages.Count -ne 1) { throw 'Specify -PackageRoot for the active MCP package. Automatic selection requires exactly one installed version.' }
    $PackageRoot = $packages[0].FullName
}
$target = (Resolve-Path -LiteralPath $PackageRoot).Path
if (-not $target.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The package must be inside this project. This script does not patch other Unity projects.'
}
$package = Get-Content -LiteralPath (Join-Path $target 'package.json') -Raw | ConvertFrom-Json
if ($package.name -ne 'com.coplaydev.unity-mcp') { throw 'The selected directory is not the Unity MCP package.' }
$relativeTarget = [IO.Path]::GetRelativePath($repoRoot, $target).Replace('\', '/')
$direction = @()
if ($Reverse) { $direction = @('--reverse') }

Push-Location $repoRoot
try {
    $check = & git apply --check @direction "--directory=$relativeTarget" $patchPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        $opposite = @('--reverse')
        if ($Reverse) { $opposite = @() }
        $already = & git apply --check @opposite "--directory=$relativeTarget" $patchPath 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Output 'Already in the requested patch state. No files changed.'
            return
        }
        throw "Patch context differs. No files changed. Review the new upstream implementation before rebasing this patch.`n$check"
    }
    if ($CheckOnly) { Write-Output "Patch applies cleanly to $relativeTarget. No files changed."; return }

    $backupRoot = Join-Path $repoRoot ('local-only/mcp-patch-backups/' + [guid]::NewGuid().ToString('N'))
    $affected = @('Runtime/Helpers/ScreenshotUtility.cs', 'Editor/Tools/ManageScene.cs', 'Editor/Tools/Cameras/ManageCamera.cs')
    foreach ($file in $affected) {
        $backup = Join-Path $backupRoot $file
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $target $file) -Destination $backup
    }
    & git apply @direction "--directory=$relativeTarget" $patchPath
    if ($LASTEXITCODE -ne 0) { throw "Patch application failed. Backup: $backupRoot" }
    Write-Output "Patch applied. Backup: $backupRoot"
    Write-Output 'Refresh Unity when editor control is available, then run the validation steps in README.md.'
}
finally { Pop-Location }
