<#
.SYNOPSIS
    Downloads the FFmpeg 9.0 GPL shared build that host video uses into third_party/ffmpeg.
.DESCRIPTION
    BtbN's win64 GPL shared build of the FFmpeg 9.0 branch (h264_amf, h264_nvenc, libx264),
    pinned to one dated build and checked against its SHA-256, so every release ships the same
    FFmpeg. BtbN keeps month-end builds; pin one of those when updating, and update
    THIRD-PARTY-NOTICES.md to match. The app ships avcodec, avutil, swscale and swresample from
    bin/, plus LICENSE.txt; ffprobe/ffplay stay here for development checks. Run once after
    cloning, and in CI.
.EXAMPLE
    ./eng/get-ffmpeg.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/ffmpeg'
$build = 'autobuild-2026-09-30-13-08'
$asset = 'ffmpeg-n9.0.2-17-g2a571b6068-win64-gpl-shared-9.0.zip'
$sha256 = '3da6c7b60bb9ccd73ec5b5e815ba804a0879eb362ba0e3beebce50174c022696'
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$build/$asset"
$marker = Join-Path $dest 'BUILD.txt'

if ((Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $asset) -and -not $Force) {
    Write-Host "FFmpeg $asset is already in $dest (use -Force to download again)."
    return
}

$zip = Join-Path ([IO.Path]::GetTempPath()) "couchlink-ffmpeg-$([guid]::NewGuid()).zip"
$unpacked = "$zip-files"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "The download's SHA-256 is $actual, not the pinned $sha256."
    }
    Expand-Archive $zip $unpacked
    $top = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $top.FullName 'bin') $dest -Recurse
    Copy-Item (Join-Path $top.FullName 'LICENSE.txt') $dest
    Set-Content $marker $asset
} finally {
    Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path (Join-Path $dest 'bin/avcodec-63.dll'))) {
    throw 'The download has no avcodec-63.dll, so it is not an FFmpeg 9.x build.'
}
Write-Host (& (Join-Path $dest 'bin/ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
