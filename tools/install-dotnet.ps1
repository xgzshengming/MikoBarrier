# 用微软官方 dotnet-install.ps1 装一份便携 .NET SDK（默认装到 %USERPROFILE%\.dotnet）。
#
# 装完会把 dotnet.exe 全路径写进【用户环境变量 MIKOBARRIER_DOTNET】，
# 供 tools\publish.ps1 和 MikoBarrier 开发模式（GuardLauncher）自动复用。
# 不会修改系统 PATH，也不影响机器上其它 .NET 程序。
#
# 例：powershell -ExecutionPolicy Bypass -File tools\install-dotnet.ps1 -InstallDir D:\dotnet

param(
    [string]$Channel = '8.0',
    [string]$InstallDir = (Join-Path $env:USERPROFILE '.dotnet')
)

$ErrorActionPreference = 'Stop'

$installer = Join-Path $PSScriptRoot 'dotnet-install.ps1'
if (-not (Test-Path $installer)) {
    Write-Host "找不到 $installer" -ForegroundColor Red
    Write-Host '它是微软官方的安装脚本，请从下面的地址下载后放到 tools\ 目录：'
    Write-Host '  https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script'
    exit 1
}

& $installer -Channel $Channel -InstallDir $InstallDir -NoPath
Write-Output 'INSTALL_EXIT_OK'

$dotnetExe = Join-Path $InstallDir 'dotnet.exe'
if (Test-Path $dotnetExe) {
    [Environment]::SetEnvironmentVariable('MIKOBARRIER_DOTNET', $dotnetExe, 'User')
    Write-Host ''
    Write-Host "已安装 dotnet 主机：$dotnetExe" -ForegroundColor Green
    Write-Host '已把用户环境变量 MIKOBARRIER_DOTNET 指向它（新开终端后生效）。'
    Write-Host '如需回退：删除该用户环境变量即可，本机其它 .NET 程序不受影响。'
} else {
    Write-Host "安装脚本已执行，但在 $InstallDir 里没找到 dotnet.exe，请检查上面的输出。" -ForegroundColor Yellow
}