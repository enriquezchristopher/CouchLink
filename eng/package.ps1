<#
.SYNOPSIS
    Builds the release package: CouchLink-v<version>-win-x64.zip
.DESCRIPTION
    Publishes the app, PadTest and VideoTest as self-contained win-x64 builds
    (no .NET install needed on the target PC), with FFmpeg 9 in ffmpeg\, and
    zips them with the license files. All three go into one folder so they
    share one copy of the .NET runtime and of FFmpeg.
    The version comes from eng/version.props.
.EXAMPLE
    ./eng/package.ps1 -OutDir artifacts
#>
param([string]$OutDir = 'artifacts')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'eng/version.props')
$version = ([string]$props.Project.PropertyGroup.Version).Trim()
$name = "CouchLink-v$version-win-x64"
$out = Join-Path $root $OutDir
$stage = Join-Path $out $name

if (-not (Test-Path (Join-Path $root 'third_party/ffmpeg/bin/avcodec-63.dll'))) {
    throw 'FFmpeg is missing: run ./eng/get-ffmpeg.ps1 first (host video needs it).'
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

function Publish([string]$project, [string]$destination) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true `
        -p:DebugType=embedded -o $destination
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

# One folder, one runtime. The files the three builds share are identical except WindowsBase.dll: the
# app's is the real WPF one and the console tools' is a smaller stand-in, so the app goes last to win.
Publish 'src/CouchLink.PadTest' $stage
Publish 'src/CouchLink.VideoTest' $stage
Publish 'src/CouchLink.App' $stage

foreach ($file in 'LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item (Join-Path $root $file) $stage
}

$zip = Join-Path $out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Package: $zip"
