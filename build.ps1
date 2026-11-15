#!/usr/bin/env pwsh
# 一键构建 Windows 产物（需要 .NET 9 SDK；不需要 Visual Studio）
#   .\build.ps1              构建 + 冒烟测试 + 便携包
#   .\build.ps1 -SkipTests   跳过测试
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host '=== 1/4 构建 ==='
dotnet build src/XpLessonZhCN.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

if (-not $SkipTests) {
    Write-Host '=== 2/4 冒烟测试（核心逻辑 + GUI headless） ==='
    dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '冒烟测试失败' }
}

Write-Host '=== 3/4 发布便携版（win-x64 自包含，原生库与 exe 同目录） ==='
Remove-Item -Recurse -Force dist/win-x64 -ErrorAction SilentlyContinue
dotnet publish src/XpLessonZhCN.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o dist/win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败' }

Write-Host '=== 4/4 打包 ==='
Copy-Item docs/使用说明.md dist/win-x64/使用说明.md -Force
# 放一个空文件提醒用户先解压（不要直接在压缩包里双击运行）
[System.IO.File]::WriteAllBytes((Join-Path 'dist/win-x64' '[解压后再运行]'), @())
$zip = 'dist/XPlane12-FlightLessons-zh-CN-win-x64.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path 'dist/win-x64/*' -DestinationPath $zip

$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLower()
"$hash  $(Split-Path -Leaf $zip)" | Set-Content 'dist/SHA256SUMS-win-x64.txt' -Encoding utf8

Write-Host ''
Write-Host "完成：$zip"
Get-ChildItem dist/win-x64 | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host "SHA256: $hash"
