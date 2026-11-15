#!/usr/bin/env pwsh
# 一键构建 Windows 产物（需要 .NET 9 SDK；不需要 Visual Studio）
#   .\build.ps1              构建 + 冒烟测试 + 便携包 + 单文件版
#   .\build.ps1 -SkipTests   跳过测试
#   .\build.ps1 -SkipSingle  不构建单文件版
param(
    [switch]$SkipTests,
    [switch]$SkipSingle
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host '=== 1/5 构建 ==='
dotnet build src/XpLessonZhCN.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

if (-not $SkipTests) {
    Write-Host '=== 2/5 冒烟测试（核心逻辑 + GUI headless） ==='
    dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '冒烟测试失败' }
}

Write-Host '=== 3/5 发布便携版（win-x64 自包含，原生库与 exe 同目录） ==='
Remove-Item -Recurse -Force dist/win-x64 -ErrorAction SilentlyContinue
dotnet publish src/XpLessonZhCN.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o dist/win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败' }

Write-Host '=== 4/5 打包便携版 ==='
Copy-Item README.md dist/win-x64/使用说明.md -Force
$zip = 'dist/XPlane12-FlightLessons-zh-CN-win-x64.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path 'dist/win-x64/*' -DestinationPath $zip

$artifacts = @($zip)

if (-not $SkipSingle) {
    Write-Host '=== 5/5 发布单文件版（原生库内嵌，启动时解压到临时目录） ==='
    Remove-Item -Recurse -Force dist/win-x64-single -ErrorAction SilentlyContinue
    dotnet publish src/XpLessonZhCN.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o dist/win-x64-single --nologo
    if ($LASTEXITCODE -ne 0) { throw '单文件版发布失败' }

    $single = 'dist/XPlane12-FlightLessons-zh-CN-win-x64-single.exe'
    Copy-Item 'dist/win-x64-single/XPlane12-FlightLessons-zh-CN.exe' $single -Force
    $artifacts += $single
}

$lines = foreach ($file in $artifacts) {
    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLower()
    "$hash  $(Split-Path -Leaf $file)"
}
$lines | Set-Content 'dist/SHA256SUMS-win-x64.txt' -Encoding utf8

Write-Host ''
Write-Host '完成：'
Get-ChildItem dist -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
$lines | ForEach-Object { Write-Host $_ }
