<#
.SYNOPSIS
    Builds the downloadable Printendar release.

.DESCRIPTION
    Runs the whole test suite first and refuses to publish if anything fails. A broken build
    that reaches a release page is worse than no release, and this is the only gate between
    the two.

    Produces a self-contained build: the .NET runtime is inside the folder, so somebody can
    unzip it and run Printendar.exe with nothing else installed. That is the entire
    installation, which is the point.

.PARAMETER Runtime
    RID to publish for. win-x64 by default. linux-x64 and osx-arm64 build from the same
    source, though only the Windows one is tested on real hardware so far.

.PARAMETER SkipTests
    Publish without running the tests. For iterating locally only; never for a release.

.EXAMPLE
    ./publish.ps1
#>

[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test "$root" -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Tests failed. Nothing was published.'
    }
}

$version = (Select-String -Path (Join-Path $root 'Directory.Build.props') -Pattern '<Version>(.*?)</Version>' |
    Select-Object -First 1).Matches.Groups[1].Value

if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.1.0' }

$stage = Join-Path $dist "Printendar-v$version-$Runtime"

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "Publishing $Runtime..." -ForegroundColor Cyan

dotnet publish (Join-Path $root 'src/Printendar.App/Printendar.App.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $stage `
    --nologo

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# libSkiaSharp.pdb is around 89 MB and rides along through NativeCopyLocalItems. It is
# debug symbols for the native library and is no use to anybody downloading the app.
Get-ChildItem $stage -Recurse -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue

$zip = Join-Path $dist "Printendar-v$version-$Runtime.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ''
Write-Host "Built $zip ($sizeMb MB)" -ForegroundColor Green
Write-Host "Unzipped folder: $stage"
