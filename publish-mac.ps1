<#
.SYNOPSIS
    Builds a macOS .app bundle for Printendar.

.DESCRIPTION
    Publishing alone produces a folder with a bare executable in it. macOS will run that from
    a terminal and little else: no icon, no name in the menu bar, nothing sensible when it is
    double-clicked. This wraps the same output in the folder layout macOS expects.

    It does NOT sign or notarize. An unsigned app downloaded from the internet is blocked by
    Gatekeeper outright rather than merely warned about, so the instructions printed at the end
    are not optional advice, they are how the thing starts at all.

    Can be run from Windows: everything here is file copying and text, and the only step that
    needs a Mac is the one this deliberately does not do.

.PARAMETER Runtime
    osx-arm64 for Apple Silicon, osx-x64 for Intel. Both by default.

.PARAMETER SkipTests
    For iterating locally only.
#>

[CmdletBinding()]
param(
    [string[]]$Runtime = @('osx-arm64', 'osx-x64'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test "$root" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was packaged.' }
}

$version = (Select-String -Path (Join-Path $root 'Directory.Build.props') -Pattern '<Version>(.*?)</Version>' |
    Select-Object -First 1).Matches.Groups[1].Value

foreach ($rid in $Runtime) {

    Write-Host "Publishing $rid..." -ForegroundColor Cyan

    $payload = Join-Path $dist "mac-payload-$rid"
    Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue

    dotnet publish (Join-Path $root 'src/Printendar.App/Printendar.App.csproj') `
        -c Release `
        -f net10.0 `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $payload `
        --nologo

    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $rid." }

    # libSkiaSharp's symbols are around 89 MB and are no use to anybody running the app.
    Get-ChildItem $payload -Recurse -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue

    $app = Join-Path $dist "Printendar-v$version-$rid\Printendar.app"
    Remove-Item (Split-Path $app -Parent) -Recurse -Force -ErrorAction SilentlyContinue

    $macos = Join-Path $app 'Contents\MacOS'
    $resources = Join-Path $app 'Contents\Resources'

    New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null

    Copy-Item (Join-Path $payload '*') $macos -Recurse -Force
    Copy-Item (Join-Path $root 'assets\printendar.icns') (Join-Path $resources 'Printendar.icns') -Force

    # CFBundleExecutable names the file inside Contents/MacOS that macOS launches, and it is
    # the assembly name rather than the project name. LSMinimumSystemVersion matches what .NET
    # 10 itself supports; claiming lower would let it launch on a system that cannot run it.
    @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>              <string>Printendar</string>
    <key>CFBundleDisplayName</key>       <string>Printendar</string>
    <key>CFBundleIdentifier</key>        <string>com.levinium.printendar</string>
    <key>CFBundleVersion</key>           <string>$version</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleExecutable</key>        <string>Printendar</string>
    <key>CFBundleIconFile</key>          <string>Printendar</string>
    <key>CFBundlePackageType</key>       <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>LSMinimumSystemVersion</key>    <string>12.0</string>
    <key>NSHighResolutionCapable</key>   <true/>
    <key>LSApplicationCategoryType</key> <string>public.app-category.productivity</string>
</dict>
</plist>
"@ | Set-Content -Path (Join-Path $app 'Contents\Info.plist') -Encoding UTF8

    $zip = Join-Path $dist "Printendar-v$version-$rid.zip"
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $app -DestinationPath $zip -CompressionLevel Optimal

    Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "Built $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green
}

Write-Host ''
Write-Host 'On the Mac, in Terminal, from wherever the zip was unpacked:' -ForegroundColor Yellow
Write-Host ''
Write-Host '    xattr -dr com.apple.quarantine Printendar.app'
Write-Host '    chmod +x Printendar.app/Contents/MacOS/Printendar'
Write-Host '    open Printendar.app'
Write-Host ''
Write-Host 'Both lines matter. The first clears the flag macOS puts on anything downloaded,' -ForegroundColor DarkGray
Write-Host 'which otherwise blocks an unsigned app outright. The second restores the execute' -ForegroundColor DarkGray
Write-Host 'permission, which a zip written on Windows cannot carry.' -ForegroundColor DarkGray
