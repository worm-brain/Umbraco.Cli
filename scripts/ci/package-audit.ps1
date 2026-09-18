#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Inspects the packed .nupkg so no secret material or unexpected file ships to NuGet.org.

.DESCRIPTION
    A .nupkg is a zip. This extracts it, prints the full file list (so a reviewer can eyeball
    what ships), then fails if it contains a denylisted file type (keys, certs, dotenv, config
    files) or a high-signal secret pattern inside a shipped text file. Run `dotnet pack` first.

    Deliberately dependency-free (no external scanner) so the check is deterministic and
    runs identically in CI and locally.

.PARAMETER PackageDir
    Directory holding the packed .nupkg. Defaults to ./nupkg.
#>
[CmdletBinding()]
param(
    [string]$PackageDir = './nupkg'
)

$ErrorActionPreference = 'Stop'

$nupkg = Get-ChildItem -Path $PackageDir -Filter *.nupkg -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $nupkg) {
    Write-Host "::error::no .nupkg found in $PackageDir"
    exit 1
}

$dest = Join-Path ([System.IO.Path]::GetTempPath()) ("pkgaudit-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $dest | Out-Null

# Expand-Archive requires a .zip extension, so copy the nupkg to a .zip name first.
$zipCopy = Join-Path $dest ($nupkg.BaseName + '.zip')
Copy-Item -Path $nupkg.FullName -Destination $zipCopy
Expand-Archive -Path $zipCopy -DestinationPath $dest -Force
Remove-Item $zipCopy

Write-Host "Package contents ($($nupkg.Name)):"
Get-ChildItem -Recurse -File $dest |
    ForEach-Object { $_.FullName.Substring($dest.Length + 1) } |
    Sort-Object |
    ForEach-Object { Write-Host "  $_" }

# 1) Denylisted file types that should never ship in the tool package.
$denyGlobs = '*.pfx', '*.snk', '*.p12', '*.pem', '*.key', '.env', 'config.json', 'appsettings*.json'
$bad = foreach ($glob in $denyGlobs) {
    Get-ChildItem -Recurse -File -Path $dest -Filter $glob -ErrorAction SilentlyContinue
}
if ($bad) {
    Write-Host "::error::denylisted/secret-bearing file(s) present in package:"
    $bad | ForEach-Object { Write-Host "  $($_.FullName.Substring($dest.Length + 1))" }
    exit 1
}

# 2) High-signal secret patterns inside shipped text files.
$patterns = 'client_secret', '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----', 'xoxb-[0-9A-Za-z-]+', 'AKIA[0-9A-Z]{16}'
$textExt = '.json', '.xml', '.txt', '.md', '.nuspec', '.props', '.targets', '.config', '.ps1'
$hits = Get-ChildItem -Recurse -File -Path $dest |
    Where-Object { $textExt -contains $_.Extension.ToLower() } |
    Select-String -Pattern $patterns -List -ErrorAction SilentlyContinue
if ($hits) {
    Write-Host "::error::secret-like pattern found in packaged file(s):"
    $hits | ForEach-Object { Write-Host "  $($_.Path):$($_.LineNumber)" }
    exit 1
}

Write-Host "PASS: package contains no denylisted files or secret-like patterns."
exit 0
