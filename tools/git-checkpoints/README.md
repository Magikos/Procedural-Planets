# Local Git checkpoints

Windows Task Scheduler runs `ProceduralPlanets Checkpoint` every 15 minutes,
subject to Windows idle detection. It stops the task when idle time ends and
retries during later idle time. The task runs only while Bryan is logged on.
It does not wake the computer or start on battery power.

The task uses a windowless WScript launcher and hidden child processes.
It requests low scheduling priority and Windows background processing mode.
It makes no network requests or AI calls. It does not build, test, or run
Git maintenance. Idle detection is a Windows heuristic, not a game detector.
It cannot guarantee zero disk activity when a game starts.

## Storage and scope

- Checkpoints live at `refs/checkpoints/<lowercase computer name>` in this repository.
- The active branch, real Git index, and working files remain unchanged.
- Snapshots include tracked files, deletions, and non-ignored new files.
- Git ignore rules still apply. Unsaved editor buffers and ignored files are not captured.
- When staging contains a separate version, the checkpoint retains it through an additional parent commit.
- Each later checkpoint retains the previous checkpoint as its first parent.
- No checkpoints are deleted automatically. Changing large binary assets increases local Git storage.
- Git merge, rebase, and detected index activity delay the run.
- A snapshot records files as Git reads them. It is not an atomic filesystem snapshot during concurrent writes.

These are unverified recovery snapshots. They need not compile or run.
Local checkpoints do not protect against disk failure or loss of this repository.
Nothing is pushed remotely.

## Installation and updates

Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/git-checkpoints/Install.ps1
```

The installer copies the runner into `.git/checkpoint-runner`.
The scheduled task uses that copy so branch switches do not remove its script.
Rerun installation after editing `Checkpoint.ps1`.
Installation replaces only the named task and its installed runner files.
The launcher uses Windows Script Host, which must remain enabled.
The installer uses the Git directory because packaged applications can redirect
`LOCALAPPDATA` into folders that Task Scheduler cannot see.

## Status

```powershell
Get-ScheduledTask -TaskName 'ProceduralPlanets Checkpoint'
Get-ScheduledTaskInfo -TaskName 'ProceduralPlanets Checkpoint'
Get-Content .git/checkpoint-state/checkpoint.log -Tail 10
git log --first-parent --oneline refs/checkpoints/bryan-pc
```

The log records `SAVED`, `UNCHANGED`, `SKIPPED`, or `ERROR` with elapsed time.
The installed runner directory also contains `launcher-status.txt` and startup errors in `startup.log`.
`last-checkpoint.txt` in the same directory contains the last successful commit.
The log rotates after 1 MiB. Errors return a nonzero task result without a popup.
A stopped task can leave the last log entry unchanged; check its next successful run.
If Git reports a reference lock error, inspect it before removing any Git lock.

## Recover without overwriting current work

Choose a commit from the checkpoint history, then export it to a new ZIP:

```powershell
git archive --format=zip --output="$env:TEMP/planet-recovery.zip" CHECKPOINT_HASH
```

Extract that ZIP into a separate folder and copy back only the files you need.
To inspect the staged version, get its tree hash from the checkpoint message:

```powershell
git show --no-patch --format=%B CHECKPOINT_HASH
git show STAGED_TREE_HASH:Assets/path/to/file.cs
```

## Pause or remove scheduling

```powershell
Disable-ScheduledTask -TaskName 'ProceduralPlanets Checkpoint'
Enable-ScheduledTask -TaskName 'ProceduralPlanets Checkpoint'
Unregister-ScheduledTask -TaskName 'ProceduralPlanets Checkpoint' -Confirm:$false
```

Removing the task leaves existing checkpoints intact.

## Validation

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/git-checkpoints/Test-Checkpoint.ps1
```

The check uses a temporary repository. It verifies file recovery, staged content,
deletions, ignored files, unchanged runs, history, busy Git retries, and branch/index preservation.
It retains that temporary repository for inspection.

Windows behavior: [idle conditions](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-idle-conditions)
and [background processing mode](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setpriorityclass).
