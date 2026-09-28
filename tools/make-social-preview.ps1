# 生成 GitHub 社交预览图（1280x630，Settings -> Social preview 上传）。
#
# 用法（仓库根目录）：
#   powershell -ExecutionPolicy Bypass -File .\tools\make-social-preview.ps1
#   # 输出：docs\social-preview.png
#
# 素材：主界面截图 docs\screenshots\01-focus-miko.png + 应用图标 Assets\MikoBarrier.png。
# 改版式时改本脚本里的坐标即可，重新跑一遍覆盖同名文件。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repo = Split-Path -Parent $PSScriptRoot
$shot = Join-Path $repo 'docs\screenshots\01-focus-miko.png'
$icon = Join-Path $repo 'src\MikoBarrier.App\Assets\MikoBarrier.png'
$out  = Join-Path $repo 'docs\social-preview.png'

foreach ($p in @($shot, $icon)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "缺少素材：$p" }
}

$W = 1280
$H = 630
$vermillion = [System.Drawing.ColorTranslator]::FromHtml('#D93A2B')
$ink = [System.Drawing.ColorTranslator]::FromHtml('#1F1F1F')
$gray = [System.Drawing.ColorTranslator]::FromHtml('#6B6B6B')
$hair = [System.Drawing.ColorTranslator]::FromHtml('#ECECEC')

function Get-Font([string[]]$names, [float]$size, [System.Drawing.FontStyle]$style) {
    foreach ($n in $names) {
        try {
            $f = New-Object System.Drawing.Font $n, $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
            if ($f.Name -eq $n) { return $f }
            $f.Dispose()
        } catch { }
    }
    return New-Object System.Drawing.Font 'Segoe UI', $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
}

function New-RoundedPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$bmp = New-Object System.Drawing.Bitmap $W, $H, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::White)

# 左侧朱红竖条 + 右下淡色装饰圆
$g.FillRectangle((New-Object System.Drawing.SolidBrush $vermillion), 0, 0, 14, $H)
$soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(18, $vermillion))
$g.FillEllipse($soft, $W - 300, $H - 260, 420, 420)

# 图标
$iconImg = [System.Drawing.Image]::FromFile($icon)
$g.DrawImage($iconImg, (New-Object System.Drawing.Rectangle 70, 78, 96, 96))

# 标题 / 副标题
$fTitle = Get-Font @('Microsoft YaHei UI','Microsoft YaHei','微软雅黑') 62 ([System.Drawing.FontStyle]::Bold)
$fSub   = Get-Font @('Microsoft YaHei UI','Microsoft YaHei','微软雅黑') 30 ([System.Drawing.FontStyle]::Regular)
$fBody  = Get-Font @('Microsoft YaHei UI','Microsoft YaHei','微软雅黑') 23 ([System.Drawing.FontStyle]::Regular)
$fSmall = Get-Font @('Microsoft YaHei UI','Microsoft YaHei','微软雅黑') 20 ([System.Drawing.FontStyle]::Regular)
$bInk = New-Object System.Drawing.SolidBrush $ink
$bVer = New-Object System.Drawing.SolidBrush $vermillion
$bGray = New-Object System.Drawing.SolidBrush $gray
$penHair = New-Object System.Drawing.Pen $hair, 2

$titleMain = 'MikoBarrier'
$titleSub = ' 巫女结界'
$g.DrawString($titleMain, $fTitle, $bInk, 188, 92)
$titleSubX = 188 + $g.MeasureString($titleMain, $fTitle).Width - 14
$g.DrawString($titleSub, $fTitle, $bVer, $titleSubX, 92)
$g.DrawString('Windows 强制自律工具 · 开源自律结界', $fSub, $bGray, 74, 196)
$g.DrawLine($penHair, 74, 256, 600, 256)

$bullets = @(
    '锁软件 · 锁网站 · 锁键盘（Alt+Tab 也按不掉）',
    '双进程互保看门狗，杀不掉的强制结界',
    '关机 / 注销永远放行，密码 + 恢复码保底'
)
$y = 288
foreach ($b in $bullets) {
    $g.FillEllipse($bVer, 76, $y + 9, 12, 12)
    $g.DrawString($b, $fBody, $bInk, 104, $y)
    $y += 52
}

$g.DrawString('github.com/xgzshengming/MikoBarrier', $fSmall, $bGray, 76, 552)
$g.DrawString('Apache-2.0 · 无遥测 · 免安装', $fSmall, $bGray, 76, 584)

# 右侧主界面截图（圆角 + 阴影 + 描边）
$shotImg = [System.Drawing.Image]::FromFile($shot)
$sw = 560
$sh = [int][Math]::Round($shotImg.Height * ($sw / $shotImg.Width))
$sx = $W - $sw - 64
$sy = 268
for ($i = 6; $i -ge 1; $i--) {
    $a = [int](10 * (7 - $i) / 6)
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($a, 0, 0, 0))
    $sp = New-RoundedPath (New-Object System.Drawing.RectangleF ($sx + $i), ($sy + $i), $sw, $sh) 14
    $g.FillPath($shadowBrush, $sp)
    $sp.Dispose(); $shadowBrush.Dispose()
}
$clip = New-RoundedPath (New-Object System.Drawing.RectangleF $sx, $sy, $sw, $sh) 14
$g.SetClip($clip)
$g.DrawImage($shotImg, (New-Object System.Drawing.Rectangle $sx, $sy, $sw, $sh))
$g.ResetClip()
$g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#E3E3E3')), 2), $clip)

$clip.Dispose()
$penHair.Dispose()
$bInk.Dispose(); $bVer.Dispose(); $bGray.Dispose(); $soft.Dispose()
$fTitle.Dispose(); $fSub.Dispose(); $fBody.Dispose(); $fSmall.Dispose()
$shotImg.Dispose(); $iconImg.Dispose()
$g.Dispose()

$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("生成完成：{0}（{1} KB）" -f $out, [Math]::Round((Get-Item $out).Length / 1KB))
