#!/usr/bin/env pwsh
# 一键构建 Windows 产物（需要 .NET 9 SDK；不需要 Visual Studio）
#
#   .\scripts\build.ps1                 构建 + 冒烟测试 + 便携包（win-x64）
#   .\scripts\build.ps1 -SkipTests      跳过测试
#   .\scripts\build.ps1 -Rid win-arm64  指定运行时标识（RID）
#   .\scripts\build.ps1 -Version 1.2.3  透传版本号（CI 打 tag 时使用）
#
# publish 参数、产物文件名规则、随包文档路径统一来自 scripts/lib/build-common.props
# （唯一事实源），CI 只调用本脚本，不再内联复制参数。
param(
    [switch]$SkipTests,
    [string]$Rid = 'win-x64',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
# 本脚本位于 scripts/，仓库根是它的上一级
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

# 共享配置（publish 属性 / 产物名 / 随包文档）——唯一事实源
$common = @{}
$propsPath = Join-Path $root 'scripts/lib/build-common.props'
foreach ($line in [System.IO.File]::ReadAllLines($propsPath)) {
    $t = $line.Trim()
    if ($t -eq '' -or $t.StartsWith('#')) { continue }
    $idx = $t.IndexOf('=')
    if ($idx -lt 1) { continue }
    $common[$t.Substring(0, $idx)] = $t.Substring($idx + 1)
}
$AppBaseName    = $common['APP_BASENAME']
$UserGuide      = $common['USER_GUIDE']
$UserGuideName  = $common['USER_GUIDE_NAME']
$publishProps   = @($common['PUBLISH_PROPS'] -split ',' | Where-Object { $_ -ne '' })

# publish 参数集中在此一处（MSBuild 属性来自 build-common.props）
$publishArgs = @('-c', 'Release', '-r', $Rid, '--self-contained', 'true')
foreach ($p in $publishProps) { $publishArgs += "-p:$p" }
if ($Version) { $publishArgs += "-p:Version=$Version" }

Write-Host '=== 1/4 构建 ==='
dotnet build src/XpLessonZhCN.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw '构建失败' }

if (-not $SkipTests) {
    Write-Host '=== 2/4 冒烟测试（核心逻辑 + GUI headless） ==='
    dotnet run --project tests/SmokeTests/SmokeTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '冒烟测试失败' }
}

Write-Host "=== 3/4 发布便携版（$Rid 自包含，原生库与 exe 同目录） ==="
$outDir = Join-Path 'dist' $Rid
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
dotnet publish src/XpLessonZhCN.csproj @publishArgs -o $outDir --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败' }

Write-Host '=== 4/4 打包 ==='
Copy-Item (Join-Path $root $UserGuide) (Join-Path $outDir $UserGuideName) -Force
# 放一个空文件提醒用户先解压（不要直接在压缩包里双击运行）
[System.IO.File]::WriteAllBytes((Join-Path $outDir '[解压后再运行]'), @())
$zip = "dist/$AppBaseName-$Rid.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
# 用 ZipFile 而不是 Compress-Archive：后者按通配符 'dist/<rid>/*' 收集文件，
# 遇到含 [ ] 的文件名（如 '[解压后再运行]'）可能匹配失败；ZipFile 直接收整个目录，
# 压缩包内的目录结构与原来一致（文件都在根下）。
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root $outDir), (Join-Path $root $zip))

# SHA256SUMS：格式与 sha256sum 兼容（每行 "<hash>  <文件名>"），UTF-8 无 BOM
# （Set-Content -Encoding utf8 在 PowerShell 5.1 会写 BOM，这里显式用无 BOM 的 UTF8）
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLower()
$sums = Join-Path 'dist' "SHA256SUMS-$Rid.txt"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $root $sums), "$hash  $(Split-Path -Leaf $zip)`n", $utf8NoBom)

Write-Host ''
Write-Host "完成：$zip"
Get-ChildItem $outDir | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host "SHA256: $hash"
