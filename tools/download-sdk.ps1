# 下载指定版本的 .NET SDK 压缩包（只下载，不安装、不解压）。
#
# 一般用不到：装 SDK 请优先用 tools\install-dotnet.ps1。
# 这个脚本留给需要离线 / 手工部署 SDK 的场景。
#
# 例：powershell -ExecutionPolicy Bypass -File tools\download-sdk.ps1 -Version 8.0.425 -OutFile D:\sdk.zip

param(
    [string]$Version = '8.0.425',
    [string]$OutFile = (Join-Path $env:TEMP "dotnet-sdk-$($Version)-win-x64.zip")
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$Version/dotnet-sdk-$($Version)-win-x64.zip"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Host "下载 $url"
Write-Host "到   $OutFile"
$webClient = New-Object System.Net.WebClient
$webClient.DownloadFile($url, $OutFile)
Write-Output "DOWNLOAD_OK $OutFile"