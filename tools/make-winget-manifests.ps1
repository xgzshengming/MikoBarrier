# 生成 winget 清单（填充 packaging\winget\template 里的占位符）。
#
# 用法（仓库根目录，Release 已发布后）：
#   powershell -ExecutionPolicy Bypass -File .\tools\make-winget-manifests.ps1 `
#       -Tag v0.6.0-Miko -Sha256 <zip 的 sha256>
#
# 生成结果在 packaging\winget\out\<版本>\，对应 winget-pkgs 仓库的
# manifests\m\MikoBarrier\MikoBarrier\<版本>\ 目录。

param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$Sha256,
    [string]$PackageVersion,
    [string]$Repo = 'xgzshengming/MikoBarrier',
    [string]$ReleaseDate,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$templateDir = Join-Path $repoRoot 'packaging\winget\template'
if (-not (Test-Path -LiteralPath $templateDir)) { throw "找不到模板目录：$templateDir" }

$Sha256 = $Sha256.Trim().ToLowerInvariant()
if ($Sha256 -notmatch '^[0-9a-f]{64}$') { throw "Sha256 格式不对：$Sha256" }

$fileVersion = $Tag -replace '^v', ''
if (-not $PackageVersion) { $PackageVersion = ($fileVersion -split '-')[0] }
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+') { throw "PackageVersion 建议用纯数字三段：$PackageVersion" }
if (-not $ReleaseDate) { $ReleaseDate = Get-Date -Format 'yyyy-MM-dd' }

$zipName = "MikoBarrier-$fileVersion-win-x64.zip"
$url = "https://github.com/$Repo/releases/download/$Tag/$zipName"

if (-not $OutDir) { $OutDir = Join-Path $repoRoot "packaging\winget\out\$PackageVersion" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$replacements = [ordered]@{
    '__VERSION__'     = $PackageVersion
    '__TAG__'         = $Tag
    '__URL__'         = $url
    '__SHA256__'      = $Sha256
    '__RELEASEDATE__' = $ReleaseDate
}

foreach ($template in Get-ChildItem -LiteralPath $templateDir -Filter *.yaml) {
    $text = [IO.File]::ReadAllText($template.FullName, [Text.UTF8Encoding]::new($false))
    foreach ($pair in $replacements.GetEnumerator()) {
        $text = $text.Replace($pair.Key, $pair.Value)
    }

    $target = Join-Path $OutDir $template.Name
    [IO.File]::WriteAllText($target, $text, [Text.UTF8Encoding]::new($false))
    Write-Output "生成 $target"
}

Write-Output ''
Write-Output "下一步："
Write-Output "  1. fork https://github.com/microsoft/winget-pkgs"
Write-Output "  2. 把 $OutDir 里的 3 个 yaml 复制到 manifests\m\MikoBarrier\MikoBarrier\$PackageVersion\"
Write-Output "  3. 安装 wingetcreate 或用 winget validate 本地校验：winget validate --manifest <目录>"
Write-Output "  4. 提 PR，标题：New package: MikoBarrier.MikoBarrier version $PackageVersion"
Write-Output ''
Write-Output "zip 地址：$url"
