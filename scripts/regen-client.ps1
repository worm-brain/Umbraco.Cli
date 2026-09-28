<#
.SYNOPSIS
    Regenerates the Kiota HTTP client for the Umbraco Management API.

.DESCRIPTION
    Runs `kiota generate` against the committed OpenAPI document
    (spec/management.json) and writes the strongly-typed request builders and
    models into src/Umbraco.Cli.Client/Generated. The generated code is
    committed to the repo so that CI and contributors do not need a running
    Umbraco instance to build — this script only needs to be re-run when the
    spec is refreshed (see scripts/fetch-spec.ps1) or the target Umbraco major
    changes. See GitHub issue #50.

    When the target Umbraco major changes, also update MinMajor/MaxMajor in
    src/Umbraco.Cli.Client/VersionSupport.cs, the tested range that
    `auth doctor` checks the connected instance against (#153).

.PARAMETER SpecPath
    Path to the OpenAPI document. Defaults to the committed spec/management.json.

.EXAMPLE
    ./scripts/regen-client.ps1
    Regenerates the client from the committed spec.

.NOTES
    Requires the Kiota global tool:
        dotnet tool install --global Microsoft.OpenApi.Kiota
#>
[CmdletBinding()]
param(
    # Path to the OpenAPI document to generate from.
    [string]$SpecPath = "$PSScriptRoot/../spec/management.json"
)

$ErrorActionPreference = 'Stop'

# Resolve paths relative to the repo root (the script's parent directory).
$repoRoot = Resolve-Path "$PSScriptRoot/.."
$output   = Join-Path $repoRoot 'src/Umbraco.Cli.Client/Generated'

if (-not (Test-Path $SpecPath)) {
    throw "OpenAPI spec not found at '$SpecPath'. Run scripts/fetch-spec.ps1 first."
}

# --clean-output wipes the previous generation so removed endpoints do not linger.
# --exclude-backward-compatible drops the legacy/duplicate members Kiota emits for
# source compatibility, keeping the generated surface smaller.
kiota generate `
    --language csharp `
    --openapi $SpecPath `
    --output $output `
    --namespace-name Umbraco.Cli.Client.Generated `
    --class-name UmbracoApiClient `
    --clean-output `
    --exclude-backward-compatible

if ($LASTEXITCODE -ne 0) {
    throw "kiota generate failed with exit code $LASTEXITCODE."
}

Write-Host "Client regenerated into $output" -ForegroundColor Green
