#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Compares two BenchmarkDotNet runs and fails when a benchmark allocates more than before.

.DESCRIPTION
    Reads the JSON reports (*-report*.json) under -Baseline and -Candidate, pairs benchmarks by
    their full name, and writes a Markdown table of mean time and allocated bytes per operation
    for both runs to the console and to -SummaryPath (the GitHub Actions job summary).

    The gate is allocations only (#409). A benchmark fails when its allocated bytes per operation
    rise more than -Threshold percent above the baseline; one that allocated nothing before fails
    on any allocation. Mean times are reported and never gated: on a shared runner they move by
    more than any useful threshold from run to run, while allocations are deterministic.

    Outcomes that are not regressions:
      - No baseline results at all (main before the benchmark project merged): the comparison is
        skipped with a notice and the candidate's numbers are listed on their own.
      - A benchmark only in the candidate (new) or only in the baseline (removed or renamed):
        listed, not gated.

    Outcomes that fail besides a regression:
      - The candidate has no results: a gate with nothing to compare would pass everything.
      - A candidate benchmark has no allocation figure where the baseline had one (its
        [MemoryDiagnoser] was dropped), which would switch the gate off for it silently.

.PARAMETER Baseline
    Folder holding the baseline run's reports (searched recursively), e.g. main's artifacts.

.PARAMETER Candidate
    Folder holding the candidate run's reports (searched recursively), e.g. the PR's artifacts.

.PARAMETER Threshold
    Allowed rise in allocated bytes per operation, in percent. Defaults to 10.

.PARAMETER BaselineLabel
    Column label for the baseline run. Defaults to "main".

.PARAMETER CandidateLabel
    Column label for the candidate run. Defaults to "PR".

.PARAMETER SummaryPath
    File the Markdown table is appended to. Defaults to $env:GITHUB_STEP_SUMMARY; when that is
    unset too (a local run), the table is only printed.

.OUTPUTS
    Exit code 0 when no benchmark regressed (or there was no baseline), 1 otherwise.

.EXAMPLE
    ./scripts/ci/benchmark-compare.ps1 -Baseline ./bench/main -Candidate ./bench/pr
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Baseline,

    [Parameter(Mandatory)]
    [string]$Candidate,

    [ValidateRange(0, 1000)]
    [double]$Threshold = 10,

    [string]$BaselineLabel = 'main',

    [string]$CandidateLabel = 'PR',

    [string]$SummaryPath = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
$inv = [System.Globalization.CultureInfo]::InvariantCulture

# Reads every benchmark in a run's JSON reports, keyed by full name (namespace, type, method and
# parameters), which is stable across runs and machines. Mean is in nanoseconds; Allocated is
# bytes per operation, or $null when the benchmark had no memory diagnoser or did not finish.
function Read-Run([string]$Path) {
    $results = @{}
    if (-not (Test-Path $Path)) { return $results }
    foreach ($file in Get-ChildItem -Path $Path -Recurse -File -Filter '*-report*.json') {
        $report = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json
        foreach ($b in $report.Benchmarks) {
            $name = "$($b.Type).$($b.Method)"
            if ($b.Parameters) { $name += "($($b.Parameters))" }
            $results[$b.FullName] = [pscustomobject]@{
                Name      = $name
                Mean      = if ($b.Statistics) { [double]$b.Statistics.Mean } else { $null }
                Allocated = if ($null -ne $b.Memory.BytesAllocatedPerOperation) {
                    [double]$b.Memory.BytesAllocatedPerOperation
                } else { $null }
            }
        }
    }
    return $results
}

function Format-Time($ns) {
    if ($null -eq $ns) { return 'n/a' }
    if ($ns -ge 1e9) { return [string]::Format($inv, '{0:0.00} s', $ns / 1e9) }
    if ($ns -ge 1e6) { return [string]::Format($inv, '{0:0.00} ms', $ns / 1e6) }
    if ($ns -ge 1e3) { return [string]::Format($inv, '{0:0.00} us', $ns / 1e3) }
    return [string]::Format($inv, '{0:0.00} ns', $ns)
}

# Binary units, as BenchmarkDotNet prints them (1 KB = 1024 B).
function Format-Bytes($bytes) {
    if ($null -eq $bytes) { return 'n/a' }
    if ($bytes -ge 1MB) { return [string]::Format($inv, '{0:0.00} MB', $bytes / 1MB) }
    if ($bytes -ge 1KB) { return [string]::Format($inv, '{0:0.00} KB', $bytes / 1KB) }
    return [string]::Format($inv, '{0:0} B', $bytes)
}

# Signed percentage change from $old to $new; $null when either is missing or $old is zero.
function Get-Change($old, $new) {
    if ($null -eq $old -or $null -eq $new -or $old -eq 0) { return $null }
    return ($new - $old) / $old * 100
}

function Format-Change($pct) {
    if ($null -eq $pct) { return 'n/a' }
    return [string]::Format($inv, '{0:+0.0;-0.0;0.0}%', $pct)
}

$base = Read-Run $Baseline
$cand = Read-Run $Candidate
$lines = [System.Collections.Generic.List[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
$limit = [string]::Format($inv, '{0:0.#}%', $Threshold)

$lines.Add("## Benchmarks: $CandidateLabel vs $BaselineLabel")
$lines.Add('')

if ($cand.Count -eq 0) {
    $lines.Add("The $CandidateLabel run produced no benchmark results.")
    $failures.Add("no benchmark results found under '$Candidate'")
}
elseif ($base.Count -eq 0) {
    # Not a failure: main has nothing to compare against until the benchmark project exists there.
    Write-Host "::notice::No baseline benchmark results under '$Baseline'; comparison skipped."
    $lines.Add("No $BaselineLabel results to compare against, so the allocation gate was skipped. $CandidateLabel results:")
    $lines.Add('')
    $lines.Add('| Benchmark | Mean | Allocated |')
    $lines.Add('|---|---:|---:|')
    foreach ($c in $cand.Values | Sort-Object Name) {
        $lines.Add("| $($c.Name) | $(Format-Time $c.Mean) | $(Format-Bytes $c.Allocated) |")
    }
}
else {
    $lines.Add("Fails when allocated bytes per operation rise more than $limit over $BaselineLabel. Times are for information only: shared runners are too noisy to gate on.")
    $lines.Add('')
    $lines.Add("| Benchmark | Mean ($BaselineLabel) | Mean ($CandidateLabel) | Time | Allocated ($BaselineLabel) | Allocated ($CandidateLabel) | Allocations | Result |")
    $lines.Add('|---|---:|---:|---:|---:|---:|---:|---|')

    foreach ($key in @($base.Keys) + @($cand.Keys) | Sort-Object -Unique) {
        $b = $base[$key]
        $c = $cand[$key]
        $name = if ($c) { $c.Name } else { $b.Name }

        if (-not $b) {
            $result = 'new (not gated)'
        }
        elseif (-not $c) {
            $result = "removed (not in $CandidateLabel)"
        }
        elseif ($null -eq $c.Allocated -and $null -ne $b.Allocated) {
            $result = '**FAIL**: no allocation data'
            $failures.Add("$name has no allocation data (was $(Format-Bytes $b.Allocated))")
        }
        elseif ($null -eq $b.Allocated) {
            $result = "no $BaselineLabel allocation data (not gated)"
        }
        elseif ($c.Allocated -gt $b.Allocated * (1 + $Threshold / 100)) {
            $result = "**FAIL**: over +$limit"
            $failures.Add("$name allocates $(Format-Bytes $c.Allocated) per op, $(Format-Change (Get-Change $b.Allocated $c.Allocated)) over $BaselineLabel's $(Format-Bytes $b.Allocated) (limit +$limit)")
        }
        else {
            $result = 'ok'
        }

        $alloc = if ($b -and $c -and $b.Allocated -eq 0 -and $c.Allocated -gt 0) { 'from 0 B' }
        else { Format-Change (Get-Change $b.Allocated $c.Allocated) }
        $cells = @(
            $name
            (Format-Time $b.Mean)
            (Format-Time $c.Mean)
            (Format-Change (Get-Change $b.Mean $c.Mean))
            (Format-Bytes $b.Allocated)
            (Format-Bytes $c.Allocated)
            $alloc
            $result
        )
        $lines.Add('| ' + ($cells -join ' | ') + ' |')
    }
}

$lines.Add('')
if ($failures.Count -gt 0) {
    $lines.Add("**Result: FAIL** ($($failures.Count)):")
    foreach ($f in $failures) {
        $lines.Add("- $f")
        Write-Host "::error::$f"
    }
}
else {
    $lines.Add('**Result: pass**')
}

$markdown = $lines -join "`n"
Write-Host $markdown
if ($SummaryPath) {
    Add-Content -Path $SummaryPath -Value $markdown -Encoding utf8
}

exit ($failures.Count -gt 0 ? 1 : 0)
