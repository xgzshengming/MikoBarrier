# 从 Assets\MikoBarrier.png 生成应用图标 Assets\MikoBarrier.ico。
#
# 改完母图后重新生成图标即可；平时不需要执行。
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#
# 说明：
#   1. 母图建议是带透明背景的 PNG，方图即可（内容会自动居中）；
#   2. 脚本会先裁掉四周空白，把内容居中放进正方形画布（四周留 Margin 空白），
#      再导出 16 / 20 / 24 / 32 / 40 / 48 / 64 / 96 / 128 / 256 共 10 个尺寸；
#   3. 每个尺寸都写成 32bpp DIB + 1bpp AND 掩码，兼容旧版资源管理器、WPF 与
#      WinForms 的 NotifyIcon（托盘图标）。

param(
    [string]$Source = (Join-Path $PSScriptRoot '..\src\MikoBarrier.App\Assets\MikoBarrier.png'),
    [string]$Output = (Join-Path $PSScriptRoot '..\src\MikoBarrier.App\Assets\MikoBarrier.ico'),
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256),
    [double]$Margin = 0.04
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$outputPath = [System.IO.Path]::GetFullPath($Output)

function Get-PixelData([System.Drawing.Bitmap]$Bitmap) {
    # 把位图锁进内存，后续用字节数组读写比 GetPixel 快得多。
    $rect = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $Bitmap.Width, $Bitmap.Height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $bytes = New-Object 'byte[]' ($stride * $Bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    } finally {
        $Bitmap.UnlockBits($data)
    }
    return [pscustomobject]@{ Stride = $stride; Bytes = $bytes }
}

Write-Host "母图：$sourcePath"

$bitmap = [System.Drawing.Bitmap]::FromFile($sourcePath)
    # 统一按 96 DPI 处理：母图若自带 DPI 元数据，DrawImage 会按 DPI 自动缩放。
    $bitmap.SetResolution(96, 96)
try {
    # 1) 找到不透明内容的包围盒
    $pixels = Get-PixelData $bitmap
    $minX = $bitmap.Width
    $minY = $bitmap.Height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        $row = $y * $pixels.Stride
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if ($pixels.Bytes[$row + $x * 4 + 3] -gt 8) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw '母图整张都是透明的，无法生成图标。' }

    $contentWidth = $maxX - $minX + 1
    $contentHeight = $maxY - $minY + 1
    $centerX = $minX + $contentWidth / 2.0
    $centerY = $minY + $contentHeight / 2.0
    $canvas = [int][Math]::Ceiling([Math]::Max($contentWidth, $contentHeight) * (1 + 2 * $Margin))
    $left = [int][Math]::Round($centerX - $canvas / 2.0)
    $top = [int][Math]::Round($centerY - $canvas / 2.0)

    Write-Host ("内容包围盒：{0},{1} {2}x{3}；正方形画布：{4}x{4}" -f $minX, $minY, $contentWidth, $contentHeight, $canvas)

    # 2) 把内容居中画进正方形母版（原尺寸，不缩放）
    $master = New-Object System.Drawing.Bitmap($canvas, $canvas, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($master)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($bitmap, [int]$left, [int]$top)
    } finally {
        $graphics.Dispose()
    }

    # 3) 逐尺寸缩放，得到每个尺寸的像素（保留透明通道）
    $frames = New-Object System.Collections.ArrayList
    foreach ($size in ($Sizes | Sort-Object -Unique)) {
        $target = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($target)
        $attributes = New-Object System.Drawing.Imaging.ImageAttributes
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            # TileFlipXY 避免贴边采样时出现透明边缘发黑（halo）的问题。
            $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $destination = New-Object System.Drawing.Rectangle -ArgumentList 0, 0, $size, $size
            $graphics.DrawImage($master, $destination, 0, 0, $canvas, $canvas, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
        } finally {
            $attributes.Dispose()
            $graphics.Dispose()
        }

        # 转成 ICO 内部的 32bpp DIB（自下而上）与 1bpp AND 掩码
        $px = Get-PixelData $target
        $w = $target.Width
        $h = $target.Height
        $xor = New-Object 'byte[]' ($w * $h * 4)
        for ($y = 0; $y -lt $h; $y++) {
            [System.Buffer]::BlockCopy($px.Bytes, $y * $px.Stride, $xor, ($h - 1 - $y) * $w * 4, $w * 4)
        }

        $maskStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
        $andMask = New-Object 'byte[]' ($maskStride * $h)
        for ($y = 0; $y -lt $h; $y++) {
            $row = $y * $px.Stride
            $destinationRow = ($h - 1 - $y) * $maskStride
            for ($x = 0; $x -lt $w; $x++) {
                if ($px.Bytes[$row + $x * 4 + 3] -lt 128) {
                    $index = $destinationRow + [int][Math]::Floor($x / 8)
                    $andMask[$index] = $andMask[$index] -bor ([byte](0x80 -shr ($x % 8)))
                }
            }
        }

        $stream = New-Object System.IO.MemoryStream
        $writer = New-Object System.IO.BinaryWriter($stream)
        $writer.Write([uint32]40)                 # BITMAPINFOHEADER.biSize
        $writer.Write([int32]$w)                  # biWidth
        $writer.Write([int32]($h * 2))            # biHeight（高度是 XOR + AND 两份）
        $writer.Write([uint16]1)                  # biPlanes
        $writer.Write([uint16]32)                 # biBitCount
        $writer.Write([uint32]0)                  # biCompression = BI_RGB
        $writer.Write([uint32]($w * $h * 4))      # biSizeImage
        $writer.Write([int32]0)                   # biXPelsPerMeter
        $writer.Write([int32]0)                   # biYPelsPerMeter
        $writer.Write([uint32]0)                  # biClrUsed
        $writer.Write([uint32]0)                  # biClrImportant
        $writer.Write($xor)
        $writer.Write($andMask)
        $writer.Flush()
        $frames.Add([pscustomobject]@{ Width = $w; Height = $h; Data = $stream.ToArray() }) | Out-Null
        $writer.Dispose()
        $stream.Dispose()
        $target.Dispose()

        Write-Host ("  已生成 {0}x{0}" -f $size)
    }
    $master.Dispose()
} finally {
    $bitmap.Dispose()
}

# 4) 按 ICO 格式写文件：ICONDIR + ICONDIRENTRY 表 + 各尺寸 DIB
$outputStream = New-Object System.IO.MemoryStream
$binary = New-Object System.IO.BinaryWriter($outputStream)
$binary.Write([uint16]0)                      # reserved
$binary.Write([uint16]1)                      # type = icon
$binary.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $binary.Write([byte]$(if ($frame.Width -ge 256) { 0 } else { $frame.Width }))
    $binary.Write([byte]$(if ($frame.Height -ge 256) { 0 } else { $frame.Height }))
    $binary.Write([byte]0)                    # 调色板颜色数（真彩色为 0）
    $binary.Write([byte]0)                    # reserved
    $binary.Write([uint16]1)                  # planes
    $binary.Write([uint16]32)                 # bpp
    $binary.Write([uint32]$frame.Data.Length)
    $binary.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) {
    $binary.Write($frame.Data)
}
$binary.Flush()
[System.IO.File]::WriteAllBytes($outputPath, $outputStream.ToArray())
$binary.Dispose()
$outputStream.Dispose()

$check = New-Object System.Drawing.Icon($outputPath)
Write-Host ("已写出：{0}（{1} 字节，默认帧 {2}x{3}）" -f $outputPath, (Get-Item -LiteralPath $outputPath).Length, $check.Width, $check.Height)
$check.Dispose()
