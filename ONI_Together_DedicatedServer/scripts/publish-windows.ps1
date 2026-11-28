#requires -Version 5.1
<#
Publish self-contained win-x64 dedicated server.
#>
param(
    [string]$Configuration = "Release",
    [string]$Output = "$PSScriptRoot\..\artifacts\win-x64"
)
$ErrorActionPreference = "Stop"
$proj = "$PSScriptRoot\..\ONI_Together_DedicatedServer.csproj"

if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

dotnet publish $proj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $Output

Copy-Item "$PSScriptRoot\..\packaging\windows\ONI_Together_DedicatedServer.bat" $Output
Write-Host "win-x64 published to $Output"
