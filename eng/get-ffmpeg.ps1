<#
.SYNOPSIS
    Downloads the FFmpeg 9.0 GPL shared build that host video uses into third_party/ffmpeg.
.DESCRIPTION
    BtbN's win64 GPL shared build of the FFmpeg 9.0 branch (h264_amf, h264_nvenc, libx264).
    The app ships avcodec, avutil, swscale and swresample from bin/, plus LICENSE.txt;
    ffprobe/ffplay stay here for development checks. Run once after cloning, and in CI.
.EXAMPLE
    ./eng/get-ffmpeg.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/ffmpeg'
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip'

if ((Test-Path (Join-Path $dest 'bin/avcodec-63.dll')) -and -not $Force) {
    Write-Host "FFmpeg is already in $dest (use -Force to download again)."
    return
}

$zip = Join-Path ([IO.Path]::GetTempPath()) "couchlink-ffmpeg-$([guid]::NewGuid()).zip"
$unpacked = "$zip-files"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip
    Expand-Archive $zip $unpacked
    $top = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $top.FullName 'bin') $dest -Recurse
    Copy-Item (Join-Path $top.FullName 'LICENSE.txt') $dest
} finally {
    Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path (Join-Path $dest 'bin/avcodec-63.dll'))) {
    throw 'The download has no avcodec-63.dll, so it is not an FFmpeg 9.x build.'
}
Write-Host (& (Join-Path $dest 'bin/ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
