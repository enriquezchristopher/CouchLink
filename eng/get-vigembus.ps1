<#
.SYNOPSIS
    Downloads the ViGEmBus driver installer that CouchLink's setup bundles into third_party/vigembus.
.DESCRIPTION
    The official ViGEmBus 1.22.0 installer from Nefarius' GitHub releases, checked against its pinned
    SHA-256. Setup runs it on a PC that doesn't have the driver yet. ViGEmBus is BSD-3-Clause; the
    project is archived upstream, so this release stays the one we ship. Run once after cloning, and
    in CI.
.EXAMPLE
    ./eng/get-vigembus.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/vigembus'
$name = 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
$sha256 = '89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a'
$url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/$name"
$file = Join-Path $dest $name

function Test-Pinned { (Test-Path $file) -and ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sha256) }

if ((Test-Pinned) -and -not $Force) {
    Write-Host "$name is already in $dest (use -Force to download again)."
    return
}

New-Item -ItemType Directory -Force $dest | Out-Null
$partial = "$file.download"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $partial
    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "The download's SHA-256 is $actual, not the pinned $sha256."
    }
    Move-Item $partial $file -Force
} finally {
    Remove-Item $partial -Force -ErrorAction SilentlyContinue
}
Write-Host "ViGEmBus installer ready: $file"
