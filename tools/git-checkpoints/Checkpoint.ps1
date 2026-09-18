[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(Mandatory = $true)][string]$GitPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$env:GIT_OPTIONAL_LOCKS = '0'
$timer = [Diagnostics.Stopwatch]::StartNew()
$lock = $null
$index = $null
$logPath = Join-Path $PSScriptRoot 'startup.log'

function Invoke-Git([string[]]$GitArguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $GitPath
    $info.WorkingDirectory = $Repository
    $allArguments = @('-c', 'gc.auto=0', '-c', 'maintenance.auto=false', '-c', 'credential.interactive=false') + $GitArguments
    # ProcessStartInfo on Windows PowerShell requires Windows command-line quoting.
    $info.Arguments = ($allArguments | ForEach-Object {
        '"' + (($_ -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
    }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $stdout.Result.TrimEnd()
        $errors = $stderr.Result.TrimEnd()
        if ($process.ExitCode -ne 0) {
            throw "git $($GitArguments -join ' ') failed ($($process.ExitCode)): $errors"
        }
        return $output
    } finally {
        $process.Dispose()
    }
}

function Write-Status([string]$Message) {
    $line = '{0} {1} ({2:N1}s)' -f [DateTime]::UtcNow.ToString('o'), $Message, $timer.Elapsed.TotalSeconds
    if ($logPath) { Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8 }
    Write-Output $line
}

function Get-IndexHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)) }
    finally { $stream.Dispose(); $hash.Dispose() }
}

try {
    $Repository = (Resolve-Path -LiteralPath $Repository).Path
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass = [Diagnostics.ProcessPriorityClass]::Idle
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CheckpointPriority {
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr process, uint priority);
}
'@
    if (-not [CheckpointPriority]::SetPriorityClass([Diagnostics.Process]::GetCurrentProcess().Handle, 0x00100000)) {
        throw "Cannot enable Windows background processing mode. Win32 error: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }

    $gitDirectory = Invoke-Git @('rev-parse', '--absolute-git-dir')
    $state = Join-Path $gitDirectory 'checkpoint-state'
    [void][IO.Directory]::CreateDirectory($state)
    # The OS releases this handle if Task Scheduler stops the process on idle end.
    try {
        $lock = [IO.File]::Open((Join-Path $state 'runner.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    } catch [IO.IOException] {
        Write-Output 'Another checkpoint is running.'
        exit 0
    }
    $logPath = Join-Path $state 'checkpoint.log'
    if ((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 1MB) {
        Move-Item -LiteralPath $logPath -Destination "$logPath.1" -Force
    }

    foreach ($busyPath in @('index.lock', 'HEAD.lock', 'MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply')) {
        if (Test-Path -LiteralPath (Join-Path $gitDirectory $busyPath)) {
            Write-Status "SKIPPED: Git operation in progress ($busyPath)"
            exit 0
        }
    }
    $head = Invoke-Git @('rev-parse', 'HEAD')
    $branch = Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')
    $reference = 'refs/checkpoints/' + $env:COMPUTERNAME.ToLowerInvariant()
    $previous = Invoke-Git @('for-each-ref', '--format=%(objectname)', $reference)
    $sourceIndex = Join-Path $gitDirectory 'index'
    $index = Join-Path $state 'snapshot.index'
    # Only these private index files can remain after an interrupted run.
    foreach ($privateFile in @($index, "$index.lock")) {
        if (Test-Path -LiteralPath $privateFile) { Remove-Item -LiteralPath $privateFile -Force }
    }
    Copy-Item -LiteralPath $sourceIndex -Destination $index
    $indexHash = Get-IndexHash $index
    $env:GIT_INDEX_FILE = $index
    $stagedTree = Invoke-Git @('write-tree')
    $cache = Join-Path $state 'last-worktree.index'
    $cacheSource = Join-Path $state 'cached-source-index.txt'
    # Reuse Git's file-stat cache when the real index has not changed.
    if ((Test-Path -LiteralPath $cache) -and (Test-Path -LiteralPath $cacheSource) -and
        (Get-Content -LiteralPath $cacheSource -Raw).Trim() -eq $indexHash) {
        Copy-Item -LiteralPath $cache -Destination $index -Force
    }
    [void](Invoke-Git @('add', '-A', '--', '.'))
    $tree = Invoke-Git @('write-tree')
    if ((Invoke-Git @('rev-parse', 'HEAD')) -ne $head -or
        (Get-IndexHash $sourceIndex) -ne $indexHash -or
        (Test-Path -LiteralPath (Join-Path $gitDirectory 'index.lock'))) {
        Write-Status 'SKIPPED: Git state changed during snapshot; retry on the next idle run'
        exit 0
    }
    $lastStagedPath = Join-Path $state 'last-staged-tree.txt'
    $lastStaged = if (Test-Path -LiteralPath $lastStagedPath) { (Get-Content -LiteralPath $lastStagedPath -Raw).Trim() } else { '' }
    if ($previous -and (Invoke-Git @('rev-parse', "$previous`^{tree}")) -eq $tree -and $lastStaged -eq $stagedTree) {
        Write-Status "UNCHANGED: $previous"
        exit 0
    }
    $parents = @('-p', $head)
    if ($previous) { $parents = @('-p', $previous, '-p', $head) }
    if ($stagedTree -ne $tree -and $stagedTree -ne (Invoke-Git @('rev-parse', "$head`^{tree}"))) {
        $stagedCommit = Invoke-Git (@('commit-tree', $stagedTree, '-p', $head, '-m', 'Preserve staged content for checkpoint') )
        $parents += @('-p', $stagedCommit)
    }
    $message = "Unverified checkpoint $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')) UTC"
    $commit = Invoke-Git (@('commit-tree', $tree) + $parents + @('-m', $message, '-m', "Source branch: $branch`nSource HEAD: $head`nStaged tree: $stagedTree`nSaved files only. No build or runtime validation."))
    $expected = if ($previous) { $previous } else { '0' * $head.Length }
    [void](Invoke-Git @('update-ref', '-m', $message, $reference, $commit, $expected))
    Set-Content -LiteralPath $lastStagedPath -Value $stagedTree -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $state 'last-checkpoint.txt') -Value $commit -Encoding ASCII
    Copy-Item -LiteralPath $index -Destination "$cache.tmp" -Force
    if (Test-Path -LiteralPath $cache) { [IO.File]::Replace("$cache.tmp", $cache, "$cache.previous") }
    else { [IO.File]::Move("$cache.tmp", $cache) }
    Set-Content -LiteralPath $cacheSource -Value $indexHash -Encoding ASCII
    Write-Status "SAVED: $commit on $reference"
} catch {
    Write-Status "ERROR: $($_.Exception.Message)"
    exit 1
} finally {
    if ($index) {
        foreach ($privateFile in @($index, "$index.lock")) {
            if (Test-Path -LiteralPath $privateFile) { Remove-Item -LiteralPath $privateFile -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($lock) { $lock.Dispose() }
}
