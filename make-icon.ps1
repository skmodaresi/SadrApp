# Generates SadrApp/Assets/app.ico (256/48/32/16) + icon-256.png preview.
# Artwork: blue gradient rounded square, white ledger/receipt card with
# torn bottom edge, ledger bars and a rising project chart line.
# ASCII-only on purpose.
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot 'SadrApp\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # s = scale unit (1/100 of the canvas)
    $s = $size / 100.0

    # ---- background: rounded square, vertical blue gradient ----
    $r = 22 * $s
    $bgRect = [System.Drawing.RectangleF]::new(2*$s, 2*$s, 96*$s, 96*$s)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 47, 111, 237),   # 2F6FED
        [System.Drawing.Color]::FromArgb(255, 31, 58, 95),     # 1F3A5F
        90.0)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($bgRect.X, $bgRect.Y, $d, $d, 180, 90)
    $path.AddArc($bgRect.Right - $d, $bgRect.Y, $d, $d, 270, 90)
    $path.AddArc($bgRect.Right - $d, $bgRect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($bgRect.X, $bgRect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($grad, $path)

    # ---- white receipt/ledger card with torn bottom edge ----
    $cardRect = [System.Drawing.RectangleF]::new(26*$s, 18*$s, 48*$s, 62*$s)
    $cr = 6 * $s
    $cd = 2 * $cr
    $card = New-Object System.Drawing.Drawing2D.GraphicsPath
    $top = $cardRect.Y
    $bot = $cardRect.Bottom
    $step = ($cardRect.Width - $cd) / 3.0
    $xL = $cardRect.X + $cr
    $xR = $cardRect.Right - $cr
    $teeth = 7 * $s
    $card.AddArc($cardRect.X, $top, $cd, $cd, 180, 90)          # top-left round
    $card.AddArc($cardRect.Right - $cd, $top, $cd, $cd, 270, 90) # top-right round
    $card.AddLine([System.Drawing.PointF]::new($xR, $top + $cr), [System.Drawing.PointF]::new($xR, $bot - $teeth))
    $card.AddLine([System.Drawing.PointF]::new($xR, $bot - $teeth), [System.Drawing.PointF]::new(($xR - $step/2), $bot - 1*$s))
    $card.AddLine([System.Drawing.PointF]::new(($xR - $step/2), $bot - 1*$s), [System.Drawing.PointF]::new(($xR - $step), $bot - $teeth))
    $card.AddLine([System.Drawing.PointF]::new(($xR - $step), $bot - $teeth), [System.Drawing.PointF]::new(($xR - 1.5*$step), $bot - 1*$s))
    $card.AddLine([System.Drawing.PointF]::new(($xR - 1.5*$step), $bot - 1*$s), [System.Drawing.PointF]::new(($xR - 2*$step), $bot - $teeth))
    $card.AddLine([System.Drawing.PointF]::new(($xR - 2*$step), $bot - $teeth), [System.Drawing.PointF]::new($xL, $bot - $teeth))
    $card.AddLine([System.Drawing.PointF]::new($xL, $bot - $teeth), [System.Drawing.PointF]::new($xL, $top + $cr))
    $card.CloseFigure()
    $white = [System.Drawing.Brushes]::White
    $g.FillPath($white, $card)

    # ---- ledger bars (left) ----
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 63, 199, 216)) # teal 3FC7D8
    $g.FillRectangle($barBrush, [System.Drawing.RectangleF]::new(($cardRect.X + 6*$s), ($cardRect.Y + 8*$s), 20*$s, (3.4*$s)))
    $g.FillRectangle($barBrush, [System.Drawing.RectangleF]::new(($cardRect.X + 6*$s), ($cardRect.Y + 15*$s), 14*$s, (3.4*$s)))
    $g.FillRectangle($barBrush, [System.Drawing.RectangleF]::new(($cardRect.X + 6*$s), ($cardRect.Y + 22*$s), 17*$s, (3.4*$s)))

    # ---- rising chart (right side of card) ----
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 47, 111, 237), (3.4*$s))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pts = @(
        [System.Drawing.PointF]::new(($cardRect.X + 30*$s), ($cardRect.Y + 24*$s)),
        [System.Drawing.PointF]::new(($cardRect.X + 35*$s), ($cardRect.Y + 17*$s)),
        [System.Drawing.PointF]::new(($cardRect.X + 40*$s), ($cardRect.Y + 20*$s))
    )
    $g.DrawLines($pen, $pts)

    # coin circle with IRR hint
    $coinRect = [System.Drawing.RectangleF]::new(($cardRect.X + 27*$s), ($cardRect.Y + 30*$s), (15*$s), (15*$s))
    $goldGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $coinRect,
        [System.Drawing.Color]::FromArgb(255, 255, 214, 92),   # FFD65C
        [System.Drawing.Color]::FromArgb(255, 232, 158, 40),   # E89E28
        45.0)
    $g.FillEllipse($goldGrad, $coinRect)
    $gPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 255, 255), (2*$s))
    $cx = $coinRect.X + $coinRect.Width/2
    $cy = $coinRect.Y + $coinRect.Height/2
    $g.DrawLine($gPen, $cx, ($cy - 4.5*$s), $cx, ($cy + 4.5*$s))

    $g.Dispose()
    return $bmp
}

# ---- write PNG preview ----
$png = New-IconBitmap 256
$png.Save((Join-Path $outDir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# ---- assemble multi-size ICO from PNG frames ----
$sizes = @(256, 48, 32, 16)
$frames = @()
foreach ($sz in $sizes) {
    $b = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($sz, $ms.ToArray())
    $b.Dispose(); $ms.Dispose()
}

$icoPath = Join-Path $outDir 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$frames.Count)
# ICONDIRENTRY x N
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $sz = $f[0]; $bytes = $f[1]
    $bw.Write([Byte]($(if ($sz -eq 256) { 0 } else { $sz })))   # width
    $bw.Write([Byte]($(if ($sz -eq 256) { 0 } else { $sz })))   # height
    $bw.Write([Byte]0)   # palette
    $bw.Write([Byte]0)   # reserved
    $bw.Write([UInt16]1) # planes
    $bw.Write([UInt16]32)# bpp
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Output ("ICON OK: {0}" -f $icoPath)
Write-Output ("PNG  OK: {0}" -f (Join-Path $outDir 'icon-256.png'))
