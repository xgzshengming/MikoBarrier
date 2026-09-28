# 用隔离的 MikoBarrier_HOME 启动已发布版，抓取主界面与各页面截图。
#
# 用法：
#   1. 先运行 tools\publish.ps1 生成 .\app\MikoBarrier.App.exe；
#   2. 保持桌面处于可交互状态，然后执行：
#        powershell -ExecutionPolicy Bypass -File .\tools\screenshots.ps1
#
# 也可以手动指定：
#   .\tools\screenshots.ps1 -Exe "D:\build\MikoBarrier.App.exe" -OutDir ".\docs\screenshots"

param(
    [string]$Exe,
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $repo 'app\MikoBarrier.App.exe' }
if (-not $OutDir) { $OutDir = Join-Path $repo 'docs\screenshots' }

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "找不到发布版程序：$Exe。请先运行 tools\publish.ps1，或用 -Exe 指定路径。"
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Shot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
"@

$home = Join-Path $env:TEMP 'mb-screenshot-home'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# ---- 隔离数据目录：预置所有引导为“已看过”，避免弹引导框 ----
if (Test-Path -LiteralPath $home) { Remove-Item -LiteralPath $home -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $home 'data') -Force | Out-Null
$keys = @('welcome','focus.hub','focus.quick','focus.task','focus.policy','focus.network',
          'rules.hub','rules.black','rules.white','rules.sites','tasks','stats','stats.history',
          'stats.tasks','settings.appearance','settings.hub','settings.password','settings.recovery',
          'settings.questions','settings.forgot')
$json = @{ seenGuides = $keys } | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $home 'data\ui-state.json'), $json, (New-Object System.Text.UTF8Encoding $false))

$env:MikoBarrier_HOME = $home
Write-Host "隔离数据目录: $home"
Write-Host "启动程序:     $Exe"

$proc = Start-Process -FilePath $Exe -PassThru

# ---- 等主窗口出现 ----
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    if ($h -ne [IntPtr]::Zero) { break }
}
if ($h -eq [IntPtr]::Zero) { throw '主窗口未出现' }
Write-Host "主窗口句柄: $h  标题: $($proc.MainWindowTitle)"

# ---- 固定位置与大小，方便重复截图 ----
$flags = 0x0004 -bor 0x0010   # SWP_NOZORDER | SWP_NOACTIVATE
[void][Win32Shot]::ShowWindow($h, 9)   # SW_RESTORE
[void][Win32Shot]::SetWindowPos($h, [IntPtr]::Zero, 60, 40, 1180, 780, $flags)
Start-Sleep -Milliseconds 800

function Save-Shot([string]$name) {
    [void][Win32Shot]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 900
    $r = New-Object Win32Shot+RECT
    [void][Win32Shot]::GetWindowRect($h, [ref]$r)
    $w = $r.Right - $r.Left
    $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { throw "窗口尺寸异常 $w x $ht" }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $ht)))
    $path = Join-Path $OutDir $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ("已保存 {0}  ({1}x{2}, {3:N0} KB)" -f $name, $w, $ht, ((Get-Item $path).Length / 1KB))
}

Save-Shot 'ui-01-focus.png'

# ---- 用 UIAutomation 切换导航页面 ----
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)

function Click-Nav([string]$text, [string]$file) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $text)
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Write-Warning "找不到导航项: $text"; return }
    $pattern = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 1200
    Save-Shot $file
}

Click-Nav '名单管理' 'ui-02-rules.png'
Click-Nav '任务清单' 'ui-03-tasks.png'
Click-Nav '自律统计' 'ui-04-stats.png'
Click-Nav '系统设置' 'ui-05-settings.png'
Click-Nav '自律结界' 'ui-06-focus-portal.png'

if (-not $proc.HasExited) {
    $proc.CloseMainWindow() | Out-Null
    Start-Sleep -Milliseconds 1000
    if (-not $proc.HasExited) { $proc.Kill() }
}

Write-Host '完成'
