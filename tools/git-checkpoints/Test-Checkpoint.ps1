$ErrorActionPreference = 'Stop'
$git = (Get-Command git.exe).Source
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$runner = Join-Path $PSScriptRoot 'Checkpoint.ps1'
$testRepository = Join-Path ([IO.Path]::GetTempPath()) ('checkpoint-test-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRepository)

function Git([string[]]$Arguments) {
    $result = & $git -C $testRepository @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Test git command failed: $($Arguments -join ' ')" }
    return $result
}
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Snapshot {
    & $powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $runner -Repository $testRepository -GitPath $git
    Check ($LASTEXITCODE -eq 0) 'Checkpoint process failed'
}

Push-Location $testRepository
try {
    [void](Git @('init', '-q'))
    [void](Git @('config', 'user.name', 'Checkpoint Test'))
    [void](Git @('config', 'user.email', 'checkpoint-test@example.invalid'))
    Set-Content '.gitignore' 'ignored/'
    Set-Content 'code.txt' 'original'
    Set-Content 'delete.txt' 'delete me'
    [void](Git @('add', '-A'))
    [void](Git @('commit', '-qm', 'Initial test state'))
    Set-Content 'code.txt' 'staged version'
    [void](Git @('add', 'code.txt'))
    Set-Content 'code.txt' 'working version'
    Set-Content 'new file.txt' 'new content'
    Remove-Item -LiteralPath 'delete.txt'
    [void][IO.Directory]::CreateDirectory((Join-Path $testRepository 'ignored'))
    Set-Content 'ignored/cache.txt' 'cache'
    $head = Git @('rev-parse', 'HEAD')
    $branch = Git @('symbolic-ref', 'HEAD')
    $indexHash = (Get-FileHash '.git/index').Hash
    $status = (Git @('status', '--porcelain=v1')) -join "`n"
    Snapshot
    $ref = 'refs/checkpoints/' + $env:COMPUTERNAME.ToLowerInvariant()
    $first = Git @('rev-parse', $ref)
    Check ((Git @('show', "${ref}:code.txt")) -eq 'working version') 'Working content was not captured'
    Check ((Git @('show', "${ref}:new file.txt")) -eq 'new content') 'New file was not captured'
    $paths = @(Git @('ls-tree', '-r', '--name-only', $ref))
    Check (-not ($paths -contains 'delete.txt')) 'Deletion was not captured'
    Check (-not ($paths -contains 'ignored/cache.txt')) 'Ignored content entered the snapshot'
    $stagedParent = Git @('rev-parse', "$ref`^2")
    Check ((Git @('show', "${stagedParent}:code.txt")) -eq 'staged version') 'Staged-only version was lost'
    Check ((Git @('rev-parse', 'HEAD')) -eq $head) 'HEAD changed'
    Check ((Git @('symbolic-ref', 'HEAD')) -eq $branch) 'Active branch changed'
    Check ((Get-FileHash '.git/index').Hash -eq $indexHash) 'Real index changed'
    Check (((Git @('status', '--porcelain=v1')) -join "`n") -eq $status) 'Working status changed'
    Snapshot
    Check ((Git @('rev-parse', $ref)) -eq $first) 'Unchanged run created a duplicate checkpoint'
    Set-Content 'code.txt' 'next version'
    Snapshot
    Check ((Git @('rev-parse', "$ref`^1")) -eq $first) 'Earlier checkpoint history was lost'
    $second = Git @('rev-parse', $ref)
    Set-Content '.git/index.lock' 'busy'
    Set-Content 'code.txt' 'after lock'
    Snapshot
    Check ((Git @('rev-parse', $ref)) -eq $second) 'Busy repository was not skipped'
    Remove-Item -LiteralPath '.git/index.lock'
    Snapshot
    Check ((Git @('show', "${ref}:code.txt")) -eq 'after lock') 'Retry after Git lock failed'
    Set-Content 'ignored/forced.txt' 'tracked despite ignore'
    [void](Git @('add', '-f', 'ignored/forced.txt'))
    Snapshot
    Check ((Git @('show', "${ref}:ignored/forced.txt")) -eq 'tracked despite ignore') 'Cache missed a newly tracked ignored file'
    $archive = Join-Path $testRepository 'recovered.zip'
    [void](Git @('archive', '--format=zip', "--output=$archive", $first))
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $testRepository 'recovered')
    Check ((Get-Content 'recovered/code.txt') -eq 'working version') 'Archive recovery failed'
    Write-Output 'PASS: working/staged/new/deleted/ignored files, unchanged run, history, Git lock retry, branch/index preservation, and archive recovery'
    Write-Output "Test repository retained: $testRepository"
} finally {
    Pop-Location
}
