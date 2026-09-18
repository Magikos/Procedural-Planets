[CmdletBinding()]
param([string]$Repository)

$ErrorActionPreference = 'Stop'
if (-not $Repository) { $Repository = Join-Path $PSScriptRoot '../..' }
$Repository = (Resolve-Path -LiteralPath $Repository).Path
$git = (Get-Command git.exe -ErrorAction Stop).Source
$gitDirectory = & $git -C $Repository rev-parse --absolute-git-dir
if ($LASTEXITCODE -ne 0) { throw 'Repository must be an existing Git working tree.' }
$destination = Join-Path $gitDirectory 'checkpoint-runner'
[void][IO.Directory]::CreateDirectory($destination)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Checkpoint.ps1') -Destination $destination -Force
$runner = Join-Path $destination 'Checkpoint.ps1'
$powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$command = '"{0}" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{1}" -Repository "{2}" -GitPath "{3}"' -f $powershell, $runner, $Repository, $git
# WScript has no console. WindowStyle alone can briefly display PowerShell's console at startup.
$launcher = Join-Path $destination 'Launch.vbs'
$escaped = $command.Replace('"', '""')
@"
Option Explicit
Dim shell, result, files, log
Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
Set log = files.CreateTextFile("$($destination.Replace('"', '""'))\launcher-status.txt", True)
log.WriteLine "Started: " & Now
log.Close
On Error Resume Next
result = shell.Run("$escaped", 0, True)
Set log = files.OpenTextFile("$($destination.Replace('"', '""'))\launcher-status.txt", 8)
If Err.Number <> 0 Then
    log.WriteLine "Launcher error: " & Err.Number & " " & Err.Description
    log.Close
    WScript.Quit 1
End If
log.WriteLine "Finished: " & Now & " Exit code: " & result
log.Close
WScript.Quit result
"@ | Set-Content -LiteralPath $launcher -Encoding Unicode
$action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32/wscript.exe') -Argument "//B //NoLogo `"$launcher`"" -WorkingDirectory $destination
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(15) -RepetitionInterval (New-TimeSpan -Minutes 15)
$settings = New-ScheduledTaskSettingsSet -RunOnlyIfIdle -RestartOnIdle -MultipleInstances IgnoreNew -Priority 10 -ExecutionTimeLimit (New-TimeSpan -Minutes 10)
$settings.IdleSettings.StopOnIdleEnd = $true
$settings.WakeToRun = $false
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$task = New-ScheduledTask -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description 'Local Git checkpoints while Windows is idle. Hidden, background priority, no network or AI. Stops when idle ends. Log: repository .git/checkpoint-state/checkpoint.log'
Register-ScheduledTask -TaskName 'ProceduralPlanets Checkpoint' -InputObject $task -Force | Out-Null
Write-Output "Installed: ProceduralPlanets Checkpoint ($destination)"
