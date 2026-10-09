<#
.SYNOPSIS
    Scans a release's assets with ClamAV and adds the result to the release notes.
.DESCRIPTION
    Downloads every asset of the release, runs clamscan over them and appends a collapsed
    "ClamAV virus scan results" block to the release body. A block from an earlier run is
    replaced, so the script can be run again. Fails after updating the notes if clamscan finds
    anything or errors. Needs clamscan on PATH with current signatures (run freshclam first)
    and gh with a token that can edit the release.
.EXAMPLE
    ./eng/add-clamav-results.ps1 -Tag v1.10.2
#>
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
$scanDir = Join-Path ([IO.Path]::GetTempPath()) "clamav-$Tag"
Remove-Item $scanDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $scanDir | Out-Null

gh release download $Tag --dir $scanDir
if ($LASTEXITCODE -ne 0) { throw "gh release download failed ($LASTEXITCODE)" }

$engine = (clamscan --version | Select-Object -First 1).Trim()
$output = clamscan --recursive --stdout $scanDir
$exit = $LASTEXITCODE
$summaryStart = [array]::IndexOf($output, '----------- SCAN SUMMARY -----------')
$summary = if ($summaryStart -ge 0) { $output[$summaryStart..($output.Count - 1)] } else { $output }

$heading = switch ($exit) {
    0 { 'No viruses detected' }
    1 { 'VIRUSES DETECTED' }
    default { 'Scan failed' }
}
$date = [DateTime]::UtcNow.ToString('ddd, dd MMM yyyy HH:mm:ss') + ' GMT'
$fence = '```'
$block = @(
    '<!-- clamav-start -->'
    '<details>'
    "<summary><b>🛡 ClamAV virus scan results: $heading</b></summary>"
    ''
    $fence
    "Version: $engine"
    "Scan Date: $date"
    ''
    $summary
    $fence
    ''
    '</details>'
    '<!-- clamav-end -->'
) -join "`n"

$body = gh release view $Tag --json body --jq .body | Out-String
if ($LASTEXITCODE -ne 0) { throw "gh release view failed ($LASTEXITCODE)" }
$body = [regex]::Replace($body, '(?s)\s*<!-- clamav-start -->.*?<!-- clamav-end -->', '').TrimEnd()

$notes = Join-Path $scanDir 'notes.md'
Set-Content $notes "$body`n`n$block`n" -Encoding utf8NoBOM
gh release edit $Tag --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw "gh release edit failed ($LASTEXITCODE)" }

if ($exit -ne 0) { throw "clamscan exited with $exit ($heading)" }
