<#
.SYNOPSIS
    Downloads the Umbraco Management API OpenAPI document from a running instance.

.DESCRIPTION
    Fetches the Management API swagger/OpenAPI JSON from a live Umbraco instance
    and saves it to spec/management.json for committing. The document is public
    API metadata (no credentials) so it is safe to commit. After refreshing the
    spec, re-run scripts/regen-client.ps1 to regenerate the typed client.

    Umbraco 14 serves the document at /umbraco/swagger/management/swagger.json.
    Some builds also expose /umbraco/openapi/management.json — the script tries
    the swagger path first and falls back.

.PARAMETER BaseUrl
    Base URL of the Umbraco instance, e.g. https://localhost:45000.
    Also accepts the -Host alias.

.PARAMETER SkipCertificateCheck
    Skip TLS validation (needed for the self-signed dev certificate on localhost).

.EXAMPLE
    ./scripts/fetch-spec.ps1 -Host https://localhost:45000 -SkipCertificateCheck
#>
[CmdletBinding()]
param(
    # Base URL of the running Umbraco instance. Named -BaseUrl because $Host is a
    # read-only PowerShell automatic variable (assigning it throws on pwsh 7);
    # the -Host alias keeps the documented invocation working.
    [Parameter(Mandatory)]
    [Alias('Host')]
    [string]$BaseUrl,

    # Skip TLS validation for self-signed dev certificates.
    [switch]$SkipCertificateCheck
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot/.."
$specDir  = Join-Path $repoRoot 'spec'
$specPath = Join-Path $specDir 'management.json'

if (-not (Test-Path $specDir)) {
    New-Item -ItemType Directory -Path $specDir | Out-Null
}

# Candidate document locations, most-current first.
$candidates = @(
    "$($BaseUrl.TrimEnd('/'))/umbraco/swagger/management/swagger.json",
    "$($BaseUrl.TrimEnd('/'))/umbraco/openapi/management.json"
)

$invokeArgs = @{}
if ($SkipCertificateCheck) { $invokeArgs['SkipCertificateCheck'] = $true }

foreach ($url in $candidates) {
    try {
        Write-Host "Trying $url ..."
        Invoke-WebRequest -Uri $url -OutFile $specPath @invokeArgs
        Write-Host "Saved spec to $specPath" -ForegroundColor Green
        return
    }
    catch {
        Write-Warning "Failed: $($_.Exception.Message)"
    }
}

throw "Could not fetch the OpenAPI document from any known path on $BaseUrl."
