# 发布 MikoBarrier 自包含单文件到 <仓库根>\app\。
#
# app\ 已被 .gitignore 排除：发布产物走 GitHub Releases 分发，不进源码仓库。
#
# dotnet 主机解析顺序（命中即用）：
#   1. 命令行 -DotnetExe
#   2. 环境变量 MIKOBARRIER_DOTNET（本项目专用，指向 dotnet.exe 全路径）
#   3. PATH 里的 dotnet
# 需要便携 SDK 时先跑 tools\install-dotnet.ps1。
#
# 本机可选覆盖：tools\publish.local.ps1（已被 .gitignore 排除），可在这里
# 设置 $env:NUGET_PACKAGES 之类的本机专属变量。

param(
    [string]$DotnetExe,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# 仓库根目录 = 本脚本所在 tools\ 的上一级
$repo = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repo 'app'

$local = Join-Path $PSScriptRoot 'publish.local.ps1'
if (Test-Path $local) { . $local }

if (-not $DotnetExe) {
    if ($env:MIKOBARRIER_DOTNET -and (Test-Path $env:MIKOBARRIER_DOTNET)) {
        $DotnetExe = $env:MIKOBARRIER_DOTNET
    } else {
        $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($cmd) { $DotnetExe = $cmd.Source }
    }
}

if (-not $DotnetExe -or -not (Test-Path $DotnetExe)) {
    Write-Host '未找到 dotnet 主机，请任选一种解决：' -ForegroundColor Red
    Write-Host '  1) 安装 .NET 8 SDK，并确保 dotnet 出现在 PATH 里；'
    Write-Host '  2) 运行 tools\install-dotnet.ps1 安装便携 SDK，然后重开终端；'
    Write-Host '  3) 设置环境变量 MIKOBARRIER_DOTNET 指向 dotnet.exe 全路径；'
    Write-Host '  4) 本次直接指定：tools\publish.ps1 -DotnetExe "<dotnet.exe 路径>"'
    exit 1
}

Write-Output "dotnet = $DotnetExe"
New-Item -ItemType Directory -Force -Path $app | Out-Null

$commonArgs = @(
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:NuGetAudit=false',
    '-o', $app,
    '-v', 'minimal'
)

Write-Output '=== publish app ==='
& $DotnetExe publish (Join-Path $repo 'src\MikoBarrier.App\MikoBarrier.App.csproj') @commonArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "app publish 失败，exit=$LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Output '=== publish guard ==='
& $DotnetExe publish (Join-Path $repo 'src\MikoBarrier.Guard\MikoBarrier.Guard.csproj') @commonArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "guard publish 失败，exit=$LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Output '=== copy whitelist audit launcher and notices ==='
Copy-Item (Join-Path $PSScriptRoot 'whitelist-audit.cmd') (Join-Path $app 'whitelist-audit.cmd') -Force
Copy-Item (Join-Path $PSScriptRoot 'whitelist-audit.ps1') (Join-Path $app 'whitelist-audit.ps1') -Force
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $app 'LICENSE') -Force
Copy-Item (Join-Path $repo 'NOTICE') (Join-Path $app 'NOTICE') -Force
Write-Output '=== generate SHA256SUMS.txt ==='
$sumFiles = @('MikoBarrier.App.exe','MikoBarrier.Guard.exe','whitelist-audit.cmd','whitelist-audit.ps1','LICENSE','NOTICE')
$sumLines = foreach ($name in $sumFiles) {
    $file = Join-Path $app $name
    if (Test-Path -LiteralPath $file) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name"
    }
}
[IO.File]::WriteAllLines((Join-Path $app 'SHA256SUMS.txt'), $sumLines, (New-Object System.Text.UTF8Encoding($false)))

Write-Output 'PUBLISH_ALL_DONE'