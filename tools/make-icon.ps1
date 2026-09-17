<#
    生成 Assets\app.ico。

    Windows 图标是"多尺寸容器"：任务栏、桌面大图标、文件列表各取不同尺寸的帧。
    只放一个 256px 的帧，Win11 在大图标视图下会显示模糊或被缩小回退。
    所以这里生成 256/128/64/48/32/16 六帧，其中 256 用 PNG 压缩存储
    （Vista 之后的 ICO 容器允许直接内嵌 PNG，体积小很多）。

    用法：pwsh -File tools\make-icon.ps1
#>

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $root 'src\WinLauncher\Assets'
$outFile   = Join-Path $assetsDir 'app.ico'

New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

$sizes = @(256, 128, 64, 48, 32, 16)

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.Clear([System.Drawing.Color]::Transparent)

        # 圆角方形底 + 蓝紫渐变
        $inset  = [Math]::Max(1, [int]($size * 0.045))
        $radius = [int]($size * 0.22)
        $rect   = New-Object System.Drawing.Rectangle($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $radius * 2
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 0x4F, 0x7C, 0xF5),
            [System.Drawing.Color]::FromArgb(255, 0x8B, 0x4F, 0xF0),
            45.0)
        $g.FillPath($brush, $path)

        # 四个白色圆角小方块，2x2 —— 对应"分文件夹的应用列表"
        $cell  = [double]$rect.Width * 0.29
        $gap   = [double]$rect.Width * 0.10
        $blockW = $cell * 2 + $gap
        $startX = $rect.X + ($rect.Width  - $blockW) / 2
        $startY = $rect.Y + ($rect.Height - $blockW) / 2
        $cellRadius = $cell * 0.28

        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
        $dim   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 255, 255))

        for ($row = 0; $row -lt 2; $row++) {
            for ($col = 0; $col -lt 2; $col++) {
                $x = $startX + $col * ($cell + $gap)
                $y = $startY + $row * ($cell + $gap)

                $cp = New-Object System.Drawing.Drawing2D.GraphicsPath
                $cd = $cellRadius * 2
                $cp.AddArc($x, $y, $cd, $cd, 180, 90)
                $cp.AddArc(($x + $cell - $cd), $y, $cd, $cd, 270, 90)
                $cp.AddArc(($x + $cell - $cd), ($y + $cell - $cd), $cd, $cd, 0, 90)
                $cp.AddArc($x, ($y + $cell - $cd), $cd, $cd, 90, 90)
                $cp.CloseFigure()

                # 右上角那一块用半透明，形成视觉层次，不然四个方块太呆板
                $g.FillPath($(if ($row -eq 0 -and $col -eq 1) { $dim } else { $white }), $cp)
                $cp.Dispose()
            }
        }

        $brush.Dispose(); $white.Dispose(); $dim.Dispose(); $path.Dispose()
    }
    finally {
        $g.Dispose()
    }

    return $bmp
}

# 画出各尺寸并编码成 PNG
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    try {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
    finally {
        $bmp.Dispose()
    }
}

# 组装 ICO 容器
$fs = [System.IO.File]::Create($outFile)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)                 # reserved
    $bw.Write([uint16]1)                 # type: 1 = 图标
    $bw.Write([uint16]$frames.Count)     # 帧数

    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dim = if ($frame.Size -ge 256) { 0 } else { $frame.Size }   # 256 在 ICO 里记作 0
        $bw.Write([byte]$dim)            # width
        $bw.Write([byte]$dim)            # height
        $bw.Write([byte]0)               # 调色板数
        $bw.Write([byte]0)               # reserved
        $bw.Write([uint16]1)             # color planes
        $bw.Write([uint16]32)            # bits per pixel
        $bw.Write([uint32]$frame.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $bw.Write($frame.Bytes)
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

$info = Get-Item $outFile
Write-Output "已生成 $($info.FullName)  ($([Math]::Round($info.Length / 1KB, 1)) KB, $($frames.Count) 帧: $($sizes -join '/'))"
