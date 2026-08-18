<#
.SYNOPSIS
  ProceduralPlanets deterministic audit pass (Tier 1). No LLM, no network, read-only.

.DESCRIPTION
  One CLI owns the logic; hooks and skills are thin wrappers that shell out to it.
  Every function reads tools/audit/scope.json so lint, clone detection, capability
  indexing and fingerprinting can never inspect different repositories.

  Design: docs/design/2026-08-18-periodic-code-audit.md

.EXAMPLE
  pwsh tools/audit/audit.ps1 -Action inventory
  pwsh tools/audit/audit.ps1 -Action lint -Format text
  pwsh tools/audit/audit.ps1 -Action all -Format json > audit.json
  pwsh tools/audit/audit.ps1 -Action staleness
#>
[CmdletBinding()]
param(
    [ValidateSet('inventory', 'lint', 'markers', 'capabilities', 'clones', 'fingerprint', 'staleness', 'all')]
    [string]$Action = 'all',

    [ValidateSet('json', 'text')]
    [string]$Format = 'text',

    # Clone detector: consecutive normalised lines per shingle. Lower finds more, noisier.
    [int]$CloneWindow = 8
)

$ErrorActionPreference = 'Stop'
$SchemaVersion = '1.0.0'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ScopePath = Join-Path $PSScriptRoot 'scope.json'
$LedgerPath = Join-Path $RepoRoot 'docs/audit/current.md'

# ---------------------------------------------------------------- scope

function Get-Scope {
    if (-not (Test-Path $ScopePath)) { throw "scope manifest missing: $ScopePath" }
    return Get-Content $ScopePath -Raw | ConvertFrom-Json
}

function Get-ScopeFiles {
    $scope = Get-Scope
    $results = New-Object System.Collections.Generic.List[object]

    foreach ($pattern in $scope.include) {
        # Split "dir/**/*.ext" into a search root and a leaf filter.
        $normalised = $pattern -replace '/', '\'
        $starIndex = $normalised.IndexOf('*')
        if ($starIndex -lt 0) {
            $direct = Join-Path $RepoRoot $normalised
            if (Test-Path $direct) { $results.Add((Get-Item $direct)) }
            continue
        }
        $rootPart = $normalised.Substring(0, $starIndex).TrimEnd('\')
        $leaf = Split-Path $normalised -Leaf
        $searchRoot = Join-Path $RepoRoot $rootPart
        if (-not (Test-Path $searchRoot)) { continue }
        $recurse = $pattern -like '*`**`**'
        if ($recurse) {
            Get-ChildItem $searchRoot -Filter $leaf -File -Recurse | ForEach-Object { $results.Add($_) }
        }
        else {
            Get-ChildItem $searchRoot -Filter $leaf -File | ForEach-Object { $results.Add($_) }
        }
    }

    $excluded = @()
    foreach ($e in $scope.exclude) { $excluded += ($e -replace '/', '\') }

    $filtered = $results | Where-Object {
        $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart('\')
        $keep = $true
        foreach ($e in $excluded) { if ($rel -like "$e*") { $keep = $false; break } }
        $keep
    }

    # Stable order: relative path, ordinal.
    return $filtered | Sort-Object { $_.FullName.Substring($RepoRoot.Length).TrimStart('\') } -Unique
}

function Get-RelPath([string]$full) {
    return ($full.Substring($RepoRoot.Length).TrimStart('\')) -replace '\\', '/'
}

# ---------------------------------------------------------------- inventory

function Get-Inventory {
    $files = Get-ScopeFiles
    $byExt = @{}
    $totalLines = 0
    foreach ($f in $files) {
        $ext = $f.Extension.ToLowerInvariant()
        $lines = (Get-Content $f.FullName -ErrorAction SilentlyContinue | Measure-Object -Line).Lines
        $totalLines += $lines
        if (-not $byExt.ContainsKey($ext)) { $byExt[$ext] = @{ files = 0; lines = 0 } }
        $byExt[$ext].files++
        $byExt[$ext].lines += $lines
    }
    $breakdown = foreach ($k in ($byExt.Keys | Sort-Object)) {
        [pscustomobject]@{ extension = $k; files = $byExt[$k].files; lines = $byExt[$k].lines }
    }
    return [pscustomobject]@{
        scopeId    = (Get-Scope).scopeId
        totalFiles = @($files).Count
        totalLines = $totalLines
        byExtension = @($breakdown)
    }
}

# ---------------------------------------------------------------- lint

# Each rule: Id, Severity, Applies (extension filter), Pattern, Message, plus optional
# Exempt scriptblock for sanctioned sites. Adding a rule bumps the schema minor version.
function Get-LintRules {
    return @(
        [pscustomobject]@{ Id = 'PP-R001'; Severity = 'high'; Ext = '.cs'
            Pattern = 'RuntimeInitializeOnLoadMethod'
            Message = 'RuntimeInitializeOnLoadMethod outside LoadingManager.CreateInstance; ordering belongs in the init graph'
            ExemptFile = 'LoadingManager.cs' }
        [pscustomobject]@{ Id = 'PP-R002'; Severity = 'high'; Ext = '.cs'
            Pattern = 'DefaultExecutionOrder'
            Message = 'DefaultExecutionOrder is banned; ordering belongs in the init phase system'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R003'; Severity = 'high'; Ext = '.cs'
            Pattern = 'StartCoroutine|StopCoroutine'
            Message = 'Coroutine use; the project is Awaitable-only'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R004'; Severity = 'high'; Ext = '.cs'
            Pattern = 'async\s+void\s+'
            Message = 'async void; use Awaitable'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R005'; Severity = 'high'; Ext = '.cs'
            Pattern = 'Task\.Run\s*\('
            Message = 'Task.Run; use Awaitable.BackgroundThreadAsync'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R006'; Severity = 'med'; Ext = '.cs'
            Pattern = 'Shader\.(Set|Get)Global\w*\s*\(\s*"'
            Message = 'Shader global addressed by string literal; the name belongs in ShaderGlobalIds'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R008'; Severity = 'low'; Ext = '.cs'
            Pattern = '//\s*(was\s|added for\b|see PR\b|changed from\b|previously\b)'
            Message = 'Change-history comment; that belongs in the commit message'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R009'; Severity = 'med'; Ext = '.cs'
            Pattern = '#if\s+false'
            Message = 'Dead #if false block'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R011'; Severity = 'low'; Ext = $null
            Pattern = '(?<!\w)(TODO|FIXME|HACK|XXX)(?!\w)'
            Message = 'Unsanctioned debt marker; this project uses ponytail: and planned: only'
            ExemptFile = $null }
        [pscustomobject]@{ Id = 'PP-R012'; Severity = 'high'; Ext = $null
            Pattern = "(?i)don't touch caustics|(?i)do not touch caustics|(?i)caustics are untouchable"
            Message = 'Retired caustics prohibition; CLAUDE.md lifted it 2026-08-11'
            ExemptFile = $null }
    )
}

function Invoke-Lint {
    $files = Get-ScopeFiles
    $rules = Get-LintRules
    $hits = New-Object System.Collections.Generic.List[object]

    foreach ($f in $files) {
        $ext = $f.Extension.ToLowerInvariant()
        # Whole-file pre-check: most files match no rule, so this skips per-line scanning entirely.
        # Matching is case-SENSITIVE (-cmatch); C# is, and `.xxx` HLSL swizzles otherwise trip the
        # XXX debt-marker rule. Rules needing case-insensitivity carry an inline (?i).
        $text = [IO.File]::ReadAllText($f.FullName)
        $lines = $null
        foreach ($rule in $rules) {
            if ($rule.Ext -and $rule.Ext -ne $ext) { continue }
            if ($rule.ExemptFile -and $f.Name -eq $rule.ExemptFile) { continue }
            if ($text -cnotmatch $rule.Pattern) { continue }
            if ($null -eq $lines) { $lines = Get-Content $f.FullName -ErrorAction SilentlyContinue }
            if ($null -eq $lines) { continue }
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -cmatch $rule.Pattern) {
                    $hits.Add([pscustomobject]@{
                        ruleId   = $rule.Id
                        severity = $rule.Severity
                        path     = Get-RelPath $f.FullName
                        line     = $i + 1
                        text     = $lines[$i].Trim()
                        message  = $rule.Message
                    })
                }
            }
        }
    }

    # PP-R007 is a trend, not a per-line defect: reported separately.
    $oversize = foreach ($f in ($files | Where-Object { $_.Extension -eq '.cs' })) {
        $n = (Get-Content $f.FullName | Measure-Object -Line).Lines
        if ($n -gt 400) { [pscustomobject]@{ path = Get-RelPath $f.FullName; lines = $n } }
    }

    return [pscustomobject]@{
        violations = @($hits | Sort-Object ruleId, path, line)
        oversizeFiles = @($oversize | Sort-Object -Property lines -Descending)
    }
}

# ---------------------------------------------------------------- markers

function Get-Markers {
    $files = Get-ScopeFiles
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($f in $files) {
        $lines = Get-Content $f.FullName -ErrorAction SilentlyContinue
        if ($null -eq $lines) { continue }
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '(ponytail|planned)\s*:') {
                $kind = $matches[1]
                $reviewBy = $null
                if ($lines[$i] -match 'RE-REVIEW BY\s+(\d{4}-\d{2}-\d{2})') { $reviewBy = $matches[1] }
                $out.Add([pscustomobject]@{
                    kind     = $kind
                    path     = Get-RelPath $f.FullName
                    line     = $i + 1
                    text     = $lines[$i].Trim()
                    reviewBy = $reviewBy
                })
            }
        }
    }
    return @($out | Sort-Object kind, path, line)
}

# ---------------------------------------------------------------- capabilities

function Get-Capabilities {
    $files = Get-ScopeFiles | Where-Object { $_.Extension -eq '.cs' }
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($f in $files) {
        $text = [IO.File]::ReadAllText($f.FullName)
        foreach ($m in [regex]::Matches($text, '(?m)^\s*(?:public|internal)\s+static\s+(?:partial\s+)?class\s+(\w+)')) {
            $className = $m.Groups[1].Value
            $methods = New-Object System.Collections.Generic.List[string]
            foreach ($mm in [regex]::Matches($text, '(?m)^\s*(?:public|internal)\s+static\s+(?!class)([\w<>\[\],\.\s]+?)\s+(\w+)\s*\(')) {
                $methods.Add(($mm.Groups[2].Value + '(): ' + $mm.Groups[1].Value.Trim()))
            }
            $out.Add([pscustomobject]@{
                type    = $className
                path    = Get-RelPath $f.FullName
                methods = @($methods | Sort-Object -Unique)
            })
        }
    }
    return @($out | Sort-Object type, path)
}

# ---------------------------------------------------------------- clones

function Get-NormalisedLines([string]$path) {
    $raw = Get-Content $path -ErrorAction SilentlyContinue
    if ($null -eq $raw) { return @() }
    $out = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $raw.Count; $i++) {
        $l = $raw[$i]
        $l = $l -replace '//.*$', ''
        $l = $l -replace '\s+', ' '
        $l = $l.Trim()
        # Skip trivia that would otherwise make every file look alike.
        if ($l.Length -lt 6) { continue }
        if ($l -match '^[\{\}\(\);,]+$') { continue }
        if ($l -match '^(using|namespace)\b') { continue }
        $out.Add([pscustomobject]@{ text = $l; line = $i + 1 })
    }
    return $out
}

function Get-Clones([int]$window) {
    $files = Get-ScopeFiles | Where-Object { $_.Extension -in @('.cs', '.compute', '.hlsl', '.shader') }
    $index = @{}
    foreach ($f in $files) {
        $norm = Get-NormalisedLines $f.FullName
        if ($norm.Count -lt $window) { continue }
        for ($i = 0; $i -le $norm.Count - $window; $i++) {
            $slice = ($norm[$i..($i + $window - 1)] | ForEach-Object { $_.text }) -join "`n"
            $hash = [BitConverter]::ToString(
                [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($slice))
            ).Replace('-', '').Substring(0, 16)
            if (-not $index.ContainsKey($hash)) { $index[$hash] = New-Object System.Collections.Generic.List[object] }
            $index[$hash].Add([pscustomobject]@{ path = Get-RelPath $f.FullName; line = $norm[$i].line })
        }
    }

    # Sliding windows overlap, so one shared region produces many hashes with the same file set.
    # Collapse by file set and keep the earliest line per file, or a single duplicated region
    # reports as a dozen findings.
    $groups = @{}
    foreach ($h in $index.Keys) {
        $sites = $index[$h]
        $distinct = @($sites | Select-Object -ExpandProperty path -Unique | Sort-Object)
        if ($distinct.Count -lt 2) { continue }   # cross-file only; intra-file repetition is often legitimate
        $key = $distinct -join '|'
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = @{ files = $distinct; windows = 0; firstLine = @{} }
        }
        $groups[$key].windows++
        foreach ($s in $sites) {
            if (-not $groups[$key].firstLine.ContainsKey($s.path) -or $s.line -lt $groups[$key].firstLine[$s.path]) {
                $groups[$key].firstLine[$s.path] = $s.line
            }
        }
    }

    $clones = New-Object System.Collections.Generic.List[object]
    foreach ($key in $groups.Keys) {
        $g = $groups[$key]
        $sites = foreach ($p in $g.files) { [pscustomobject]@{ path = $p; line = $g.firstLine[$p] } }
        $clones.Add([pscustomobject]@{
            fileCount   = @($g.files).Count
            windowCount = $g.windows   # rough size of the shared region, in overlapping windows
            sites       = @($sites | Sort-Object path)
        })
    }
    return @($clones | Sort-Object -Property @{Expression='windowCount';Descending=$true}, @{Expression='fileCount';Descending=$true} | Select-Object -First 30)
}

# ---------------------------------------------------------------- fingerprint

function Get-TreeFingerprint {
    $files = Get-ScopeFiles
    $sha = [Security.Cryptography.SHA256]::Create()
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in $files) {
        $bytes = [IO.File]::ReadAllBytes($f.FullName)
        $h = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
        [void]$sb.AppendLine((Get-RelPath $f.FullName) + ':' + $h)
    }
    $all = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sb.ToString()))
    return [BitConverter]::ToString($all).Replace('-', '').Substring(0, 32)
}

function Get-GitHead {
    try { return (git -C $RepoRoot rev-parse HEAD 2>$null).Trim() } catch { return 'unknown' }
}

# ---------------------------------------------------------------- staleness

function Get-Staleness {
    $head = Get-GitHead
    $fp = Get-TreeFingerprint
    $scopeId = (Get-Scope).scopeId

    if (-not (Test-Path $LedgerPath)) {
        return [pscustomobject]@{
            state = 'no-ledger'; head = $head; treeFingerprint = $fp; scopeId = $scopeId
            message = 'No docs/audit/current.md. Run a full audit to seed the ledger.'
        }
    }

    $ledger = Get-Content $LedgerPath -Raw
    $auditedHead = $null; $auditedFp = $null; $auditedScope = $null
    if ($ledger -match 'auditedHead:\s*(\S+)') { $auditedHead = $matches[1] }
    if ($ledger -match 'treeFingerprint:\s*(\S+)') { $auditedFp = $matches[1] }
    if ($ledger -match 'scopeId:\s*(\S+)') { $auditedScope = $matches[1] }

    $state = 'current'
    $notes = New-Object System.Collections.Generic.List[string]

    if ($auditedScope -ne $scopeId) { $state = 'scope-changed'; $notes.Add("scope manifest changed: $auditedScope -> $scopeId") }

    if ($auditedHead -and $auditedHead -ne $head) {
        $isAncestor = $false
        try {
            git -C $RepoRoot merge-base --is-ancestor $auditedHead $head 2>$null
            $isAncestor = ($LASTEXITCODE -eq 0)
        } catch { $isAncestor = $false }
        if ($isAncestor) {
            $behind = (git -C $RepoRoot rev-list --count "$auditedHead..$head").Trim()
            if ($state -eq 'current') { $state = 'stale' }
            $notes.Add("$behind commit(s) since the audited head")
        }
        else {
            $state = 'diverged'
            $notes.Add('HEAD is not a descendant of the audited head')
        }
    }

    if ($auditedFp -and $auditedFp -ne $fp) {
        if ($state -eq 'current') { $state = 'dirty' }
        $notes.Add('working tree differs from the audited tree (uncommitted, deleted or untracked files in scope)')
    }

    return [pscustomobject]@{
        state = $state; head = $head; auditedHead = $auditedHead
        treeFingerprint = $fp; auditedFingerprint = $auditedFp
        scopeId = $scopeId; notes = @($notes)
    }
}

# ---------------------------------------------------------------- output

function Write-Text($result) {
    switch ($Action) {
        'inventory' {
            "scope: $($result.scopeId)"
            "files: $($result.totalFiles)   lines: $($result.totalLines)"
            $result.byExtension | ForEach-Object { "  {0,-10} {1,5} files {2,8} lines" -f $_.extension, $_.files, $_.lines }
        }
        'lint' {
            "violations: $(@($result.violations).Count)"
            $result.violations | Group-Object ruleId | Sort-Object Name | ForEach-Object {
                "  {0}  {1,3}  {2}" -f $_.Name, $_.Count, $_.Group[0].message
            }
            ""
            "files over 400 lines: $(@($result.oversizeFiles).Count)"
            $result.oversizeFiles | Select-Object -First 10 | ForEach-Object { "  {0,5}  {1}" -f $_.lines, $_.path }
        }
        'markers' {
            "markers: $(@($result).Count)"
            $result | Group-Object kind | ForEach-Object { "  {0,-9} {1}" -f $_.Name, $_.Count }
            $due = @($result | Where-Object { $_.reviewBy })
            if ($due.Count -gt 0) {
                ""
                "dated re-reviews:"
                $due | ForEach-Object { "  {0}  {1}:{2}" -f $_.reviewBy, $_.path, $_.line }
            }
        }
        'capabilities' { "capability types: $(@($result).Count)"; $result | Select-Object -First 20 | ForEach-Object { "  {0,-34} {1}" -f $_.type, $_.path } }
        'clones' {
            "cross-file duplicate regions: $(@($result).Count)  (ranked by shared size)"
            $result | Select-Object -First 12 | ForEach-Object {
                "  ~{0,3} windows across {1} files:" -f $_.windowCount, $_.fileCount
                $_.sites | ForEach-Object { "        {0}:{1}" -f $_.path, $_.line }
            }
        }
        'fingerprint'  { "treeFingerprint: $result" }
        'staleness'    { "state: $($result.state)"; $result.notes | ForEach-Object { "  - $_" } }
        default        { $result | ConvertTo-Json -Depth 6 }
    }
}

$result = switch ($Action) {
    'inventory'    { Get-Inventory }
    'lint'         { Invoke-Lint }
    'markers'      { Get-Markers }
    'capabilities' { Get-Capabilities }
    'clones'       { Get-Clones -window $CloneWindow }
    'fingerprint'  { Get-TreeFingerprint }
    'staleness'    { Get-Staleness }
    'all' {
        [pscustomobject]@{
            schemaVersion   = $SchemaVersion
            generatedFor    = Get-GitHead
            scopeId         = (Get-Scope).scopeId
            treeFingerprint = Get-TreeFingerprint
            inventory       = Get-Inventory
            lint            = Invoke-Lint
            markers         = Get-Markers
            capabilities    = Get-Capabilities
            clones          = Get-Clones -window $CloneWindow
        }
    }
}

if ($Format -eq 'json') { $result | ConvertTo-Json -Depth 8 }
else { Write-Text $result }
