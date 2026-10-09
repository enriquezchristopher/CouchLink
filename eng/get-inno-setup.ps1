<#
.SYNOPSIS
    Installs the Inno Setup 6 compiler that builds CouchLink's setup into third_party/innosetup.
.DESCRIPTION
    Downloads the pinned Inno Setup release from its GitHub release page, checks its SHA-256, and
    installs it silently into third_party/innosetup for the current user (no administrator rights).
    GitHub's Windows runners no longer include Inno Setup. Run once before eng/package.ps1, and in CI.
.EXAMPLE
    ./eng/get-inno-setup.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/innosetup'
$version = '6.7.3'
$sha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$url ="https://github.com/jrsoftware/issrc/releases/download/is-$($version.Replace('.', '_'))/innosetup-$version.exe"
$marker = Join-Path $dest 'BUILD.txt'
$compiler = Join-Path $dest 'ISCC.exe'

if ((Test-Path $compiler) -and (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $version) -and -not $Force) {
    Write-Host "Inno Setup $version is already in $dest (use -Force to install again)."
    return
}

$installer = Join-Path ([IO.Path]::GetTempPath()) "couchlink-innosetup-$([guid]::NewGuid()).exe"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $installer
    $actual = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "The download's SHA-256 is $actual, not the pinned $sha256."
    }
    New-Item -ItemType Directory -Force $dest | Out-Null
    $p = Start-Process $installer -Wait -PassThru -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', "/DIR=`"$dest`""
    if ($p.ExitCode -ne 0) { throw "The Inno Setup installer exited with code $($p.ExitCode)." }
} finally {
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $compiler)) {
    throw "Inno Setup installed, but $compiler is missing."
}
Set-Content $marker $version
Write-Host "Inno Setup $version ready: $compiler"
