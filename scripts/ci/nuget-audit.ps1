#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when a restored NuGet package has a known vulnerability at or above a threshold.

.DESCRIPTION
    Runs `dotnet list package --vulnerable --include-transitive --format json`, then fails
    the build if any package (direct or transitive) has a vulnerability whose severity is at
    or above -Threshold. Advisories listed in the allow-list file are treated as accepted
    risk and ignored, so an accepted finding does not re-fail later runs. Every reported
    advisory is printed regardless of severity, so nothing is hidden.

    Written in PowerShell (not jq) so it runs identically on the Linux CI runner and on a
    Windows/macOS dev box. Run `dotnet restore` first.

.PARAMETER Threshold
    Minimum severity that fails the build: Low, Moderate, High (default) or Critical.

.PARAMETER AllowlistPath
    Path to the advisory allow-list. Each non-comment line is an advisory URL or a package
    id to ignore; '#' starts a comment. Defaults to security/nuget-audit-allowlist.txt.

.OUTPUTS
    Exit code 0 when clean (or only allow-listed / below-threshold findings), 1 otherwise.
#>
[CmdletBinding()]
param(
    [ValidateSet('Low', 'Moderate', 'High', 'Critical')]
    [string]$Threshold = 'High',

    [string]$AllowlistPath = "$PSScriptRoot/../../security/nuget-audit-allowlist.txt"
)

$ErrorActionPreference = 'Stop'

# Ordinal severity so we can compare against the threshold.
$rank = @{ Low = 1; Moderate = 2; High = 3; Critical = 4 }
$minRank = $rank[$Threshold]

# `dotnet list ... --format json` can emit a leading human line before the JSON on some
# SDKs; trim to the first '{' so ConvertFrom-Json always gets clean input.
$raw = (dotnet list package --vulnerable --include-transitive --format json 2>&1) | Out-String
$start = $raw.IndexOf('{')
if ($start -lt 0) {
    Write-Host "No JSON emitted by 'dotnet list package'; nothing to audit."
    exit 0
}
$report = $raw.Substring($start) | ConvertFrom-Json

# Load the allow-list (advisory URLs or package ids), stripping comments and blanks.
$allow = @()
if (Test-Path $AllowlistPath) {
    $allow = Get-Content $AllowlistPath |
        ForEach-Object { ($_ -replace '#.*$', '').Trim() } |
        Where-Object { $_ }
}

$all = [System.Collections.Generic.List[object]]::new()
$violations = [System.Collections.Generic.List[object]]::new()

foreach ($project in $report.projects) {
    foreach ($framework in $project.frameworks) {
        # Top-level and transitive packages have the same shape; audit both.
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -eq $package) { continue }
            foreach ($vuln in $package.vulnerabilities) {
                $row = [pscustomobject]@{
                    Package  = $package.id
                    Version  = $package.resolvedVersion
                    Severity = $vuln.severity
                    Advisory = $vuln.advisoryurl
                }
                $all.Add($row)

                $ignored = ($allow -contains $vuln.advisoryurl) -or ($allow -contains $package.id)
                $sev = $rank[[string]$vuln.severity]
                if (-not $ignored -and $sev -ge $minRank) {
                    $violations.Add($row)
                }
            }
        }
    }
}

if ($all.Count -gt 0) {
    Write-Host "All reported advisories:"
    ($all | Format-Table -AutoSize | Out-String) | Write-Host
}
else {
    Write-Host "No vulnerable packages reported."
}

if ($violations.Count -gt 0) {
    Write-Host "::error::$($violations.Count) vulnerable package(s) at or above $Threshold and not allow-listed."
    ($violations | Format-Table -AutoSize | Out-String) | Write-Host
    exit 1
}

Write-Host "PASS: no vulnerabilities at or above $Threshold (allow-listed advisories excluded)."
exit 0
