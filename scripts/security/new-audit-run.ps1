#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Scaffolds a Tier-2 security-audit run folder under .claude/security-audits (git-ignored).

.DESCRIPTION
    Creates a timestamped, per-run folder so audit output is kept OUT of git (this is a public
    repo - findings must not leak into tracked files or public issues) and so successive runs
    can be compared. Each run folder is seeded with template files (meta.json, threat-model.md,
    report.md, hypotheses.md) and a raw/ subfolder for per-lens output. See docs/security-audit.md.

    The run id is a UTC timestamp plus the short commit sha, e.g. 2026-09-18-1423-f391858, so
    folders sort chronologically and each is pinned to the code it audited.

.PARAMETER List
    List existing run folders (newest first) instead of creating a new one.

.PARAMETER Reviewer
    Name recorded in meta.json as the person running the audit. Defaults to the git user name.

.OUTPUTS
    The absolute path of the created run folder (or the listing).
#>
[CmdletBinding()]
param(
    [switch]$List,
    [string]$Reviewer
)

$ErrorActionPreference = 'Stop'

# Repo root is two levels up from scripts/security.
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$auditRoot = Join-Path $repoRoot '.claude/security-audits'

if ($List) {
    if (-not (Test-Path $auditRoot)) {
        Write-Host "No audit runs yet ($auditRoot does not exist)."
        return
    }
    Get-ChildItem -Directory $auditRoot |
        Sort-Object Name -Descending |
        ForEach-Object { Write-Host $_.FullName }
    return
}

# Short commit sha (best effort - the run is still valid without it).
$sha = 'nogit'
try {
    $rev = (git -C $repoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $rev) { $sha = $rev.Trim() }
}
catch {
    # Not a git checkout, or git unavailable; keep the 'nogit' marker.
}

$runId = (Get-Date -AsUTC -Format 'yyyy-MM-dd-HHmm') + "-$sha"
$runDir = Join-Path $auditRoot $runId
if (Test-Path $runDir) {
    throw "Run folder already exists: $runDir (wait a minute or reuse it)."
}

New-Item -ItemType Directory -Path (Join-Path $runDir 'raw') -Force | Out-Null

if (-not $Reviewer) {
    try { $Reviewer = (git -C $repoRoot config user.name 2>$null); if (-not $Reviewer) { $Reviewer = $env:USERNAME } }
    catch { $Reviewer = $env:USERNAME }
}

# meta.json - machine-readable run metadata.
$meta = [ordered]@{
    runId      = $runId
    createdUtc = (Get-Date -AsUTC -Format 'o')
    commit     = $sha
    reviewer   = $Reviewer
    status     = 'in-progress'
} | ConvertTo-Json
Set-Content -Path (Join-Path $runDir 'meta.json') -Value $meta -Encoding utf8

# Template documents - see docs/security-audit.md for the full schema.
$threatModel = @"
# Threat model - $runId

Fill in per docs/security-audit.md, Step 1.

- Entry points:
- Assets:
- Trust boundaries:
- Deployment environments:
- Adversaries:
"@
Set-Content -Path (Join-Path $runDir 'threat-model.md') -Value $threatModel -Encoding utf8

$report = @"
# Security audit report - $runId

Commit: $sha  |  Reviewer: $Reviewer  |  Status: in-progress

Findings use the stable-id schema in docs/security-audit.md. Keep unsanitised detail here (this
folder is git-ignored); publish only what is safe (Step 5).

## Findings

_None recorded yet._

## Accepted / false positives

_None._
"@
Set-Content -Path (Join-Path $runDir 'report.md') -Value $report -Encoding utf8

$hypotheses = @"
# Hypotheses - $runId

Unproven candidates. These never block a release and are never filed as findings until a
proof of concept promotes them (docs/security-audit.md, Step 4).
"@
Set-Content -Path (Join-Path $runDir 'hypotheses.md') -Value $hypotheses -Encoding utf8

Write-Host "Created audit run folder:"
Write-Host "  $runDir"
$previous = Get-ChildItem -Directory $auditRoot |
    Where-Object { $_.Name -ne $runId } |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($previous) {
    Write-Host "Previous run (for comparison):"
    Write-Host "  $($previous.FullName)"
    Write-Host "  git diff --no-index `"$($previous.FullName)/report.md`" `"$runDir/report.md`""
}
