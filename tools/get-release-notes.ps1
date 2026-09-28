# 从 CHANGELOG.md 提取指定版本的段落，供 GitHub Release 说明使用。
#
# 用法（仓库根目录）：
#   $notes = & .\tools\get-release-notes.ps1 -Version 0.6.0-Miko
#   & .\tools\get-release-notes.ps1 -Version 0.6.0-Miko -OutFile release-notes.md

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Changelog,
    [string]$OutFile
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Changelog) { $Changelog = Join-Path $repo 'CHANGELOG.md' }
if (-not (Test-Path -LiteralPath $Changelog)) { throw "找不到 CHANGELOG：$Changelog" }

$lines = [IO.File]::ReadAllLines($Changelog, [Text.UTF8Encoding]::new($false))
$escaped = [regex]::Escape($Version)
$start = -1
$end = $lines.Count
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($start -lt 0) {
        if ($lines[$i] -match "^##\s+\[$escaped\]") { $start = $i }
        continue
    }

    if ($lines[$i] -match '^##\s+\[?') { $end = $i; break }
}

if ($start -lt 0) {
    throw "CHANGELOG 里找不到版本 [$Version] 的段落。请先补 CHANGELOG 再发版。"
}

$body = (($lines[$start..($end - 1)] -join "`n").Trim() -replace '^##\s+\[', '## [')
if ($OutFile) {
    [IO.File]::WriteAllText($OutFile, $body, [Text.UTF8Encoding]::new($false))
    Write-Output "已写入 $OutFile"
} else {
    Write-Output $body
}
