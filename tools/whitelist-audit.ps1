# MikoBarrier 便携白名单体检（只读）。
#
# 用法：把本脚本（连同 whitelist-audit.cmd）和 MikoBarrier.App.exe 放在同一个目录里，
#       双击 whitelist-audit.cmd 即可。报告写到脚本同级的 audit-home\logs\whitelist-audit.txt。
#
# 也可以在仓库里直接运行：会把审计数据隔离到 tools\audit-home\，
# 并自动去找仓库 app\ 目录下已发布的 MikoBarrier.App.exe。

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 找 MikoBarrier.App.exe：优先同目录（便携包），其次仓库的 app\ 目录（从 tools\ 直接运行）。
$appExe = Join-Path $root 'MikoBarrier.App.exe'
if (-not (Test-Path $appExe)) {
    $sibling = Join-Path (Split-Path -Parent $root) 'app\MikoBarrier.App.exe'
    if (Test-Path $sibling) { $appExe = $sibling }
}

if (-not (Test-Path $appExe)) {
    Write-Host ''
    Write-Host '  找不到 MikoBarrier.App.exe。' -ForegroundColor Red
    Write-Host '  请把本脚本和 MikoBarrier.App.exe 放在同一个目录里再运行，'
    Write-Host '  或先在仓库里执行 tools\publish.ps1 生成 app\ 目录。'
    Write-Host ''
    Read-Host '  按回车键退出'
    exit 1
}

# 审计数据隔离在脚本同级，不碰用户真实的 data\ / logs\。
$env:MikoBarrier_HOME = Join-Path $root 'audit-home'

Write-Host ''
Write-Host '  MikoBarrier 白名单体检（只读）' -ForegroundColor Cyan
Write-Host '  正在扫描本机正在运行的程序，请稍候...'
Start-Process -Wait -FilePath $appExe -ArgumentList '--whitelist-audit'

$report = Join-Path $root 'audit-home\logs\whitelist-audit.txt'
Write-Host ''
if (Test-Path $report) {
    Write-Host '  体检完成。报告位置：' -ForegroundColor Green
    Write-Host "    $report"
    Write-Host ''
    Write-Host '  如果报告里有工作必需的软件被列为会被拦截，请在 MikoBarrier 的名单页把它加入白名单。'
} else {
    Write-Host '  体检没有生成报告：可能被杀软拦截，或程序没有权限写入。' -ForegroundColor Yellow
    Write-Host '  请检查该目录是否有 logs\whitelist-audit.txt，或把 MikoBarrier 加入杀软信任区后重试。'
}
Write-Host ''
Write-Host '  本操作只读扫描，不会修改系统设置，也不会修改 MikoBarrier 配置。'
Read-Host '  按回车键退出'