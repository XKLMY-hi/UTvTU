# 打包 UTvTU 安装包（Windows x64）
#
# 产出（默认落到 dist\）：
#   UTvTU-Setup-<版本>.exe   自研 MD3 安装器（self-contained）
#   payload.zip              主程序发布产物（安装器解包用；必须与 Setup.exe 同目录）
#
# 用法：
#   pwsh -File scripts\build-installer.ps1                 # 全流程
#   pwsh -File scripts\build-installer.ps1 -SkipPublish    # 复用已有 bin\win-x64
#
# 注意（AGENTS.md 记过的坑）：
#   · VstProbe 缺 win-x64 资产会让 publish 报 NETSDK1047 ⇒ 先单独 restore 一次；
#   · 构建前请先关掉正在运行的 UTvTU（否则 bin 被锁、报 MSB3027）。

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutDir = 'dist',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

$version = (Select-String -Path 'installer\UTvTU.Installer\UTvTU.Installer.csproj' -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
Write-Host "== UTvTU 安装包 $version ($Configuration/$Runtime) =="

# 1) 关掉正在运行的实例（避免 bin 被锁）
Get-Process -Name OpenUtau, UTvTU-Setup -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  关闭占用进程 PID=$($_.Id) $($_.ProcessName)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Seconds 2

if (-not $SkipPublish) {
    Write-Host '-- 1/3 发布主程序（self-contained）'
    dotnet restore VstProbe\VstProbe.csproj -p:RuntimeIdentifiers=$Runtime -p:TreatWarningsAsErrors=false --ignore-failed-sources | Out-Null
    dotnet publish OpenUtau\OpenUtau.csproj -c $Configuration -r $Runtime --self-contained true `
        -o "bin\$Runtime" -p:UsedAvaloniaProducts= -p:TreatWarningsAsErrors=false
    if ($LASTEXITCODE -ne 0) { throw "publish 失败（$LASTEXITCODE）" }
}

Write-Host '-- 2/3 打包载荷 payload.zip'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$payload = Join-Path $OutDir 'payload.zip'
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path "bin\$Runtime\*" -DestinationPath $payload -CompressionLevel Optimal
Write-Host "   payload.zip = $([math]::Round((Get-Item $payload).Length / 1MB, 1)) MB"

Write-Host '-- 3/3 构建并复制安装器'
# 版本号 = 构建时刻版本号（用户 2026-10 要求）：UTvTU-<yy.M.d>-<HHmmss>
$stamp = 'UTvTU-' + (Get-Date -Format 'yy.M.d-HHmmss')
Write-Host "   版本号（同时用于 exe 属性与产物名）: $stamp"
dotnet publish installer\UTvTU.Installer\UTvTU.Installer.csproj -c $Configuration -r $Runtime `
    --self-contained true -o "$OutDir\installer" -p:RuntimeIdentifiers=$Runtime `
    -p:InformationalVersion=$stamp -p:ProductVersion=$stamp
if ($LASTEXITCODE -ne 0) { throw "安装器 publish 失败（$LASTEXITCODE）" }

$setup = Join-Path $OutDir 'installer\UTvTU-Setup.exe'
$final = Join-Path $OutDir "$stamp.exe"
Copy-Item $setup $final -Force

Write-Host ''
Write-Host '== 完成 =='
Write-Host "  $final  ($([math]::Round((Get-Item $final).Length / 1MB, 1)) MB)  版本 $stamp"
Write-Host "  $payload"
Write-Host '  分发时两者必须在同一目录（安装器会在自身旁边找 payload.zip）。'
