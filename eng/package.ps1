<#
.SYNOPSIS
    Builds the release package: CouchLink-v<version>-win-x64.zip
.DESCRIPTION
    Publishes the app and PadTest as self-contained win-x64 builds (no .NET
    install needed on the target PC) and zips them with the license files.
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

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

function Publish([string]$project, [string]$destination) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true `
        -p:DebugType=embedded -o $destination
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

Publish 'src/CouchLink.App' $stage
Publish 'src/CouchLink.PadTest' (Join-Path $stage 'PadTest')

foreach ($file in 'LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item (Join-Path $root $file) $stage
}

$zip = Join-Path $out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Package: $zip"
