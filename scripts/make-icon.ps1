Add-Type -AssemblyName System.Drawing

function New-IconLayer {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $radius = [int]($size * 0.22)

    # rounded-square dark bezel
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 20, 20, 24), [System.Drawing.Color]::FromArgb(255, 10, 10, 12), 90)
    $g.FillPath($bgBrush, $path)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 60, 30, 30), [Math]::Max(1, $size * 0.012))
    $g.DrawPath($borderPen, $path)

    $cx = $size / 2.0
    $cy = $size / 2.0
    $maxR = $size * 0.34

    # outer thin ring (lens bezel)
    $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, 90, 40, 38), [Math]::Max(1, $size * 0.02))
    $g.DrawEllipse($ringPen, [float]($cx - $maxR), [float]($cy - $maxR), [float]($maxR * 2), [float]($maxR * 2))

    # small tick marks around the ring, like a radial spectrum
    $tickPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 160, 70, 60), [Math]::Max(1, $size * 0.012))
    for ($i = 0; $i -lt 16; $i++) {
        $angle = [Math]::PI * 2 * $i / 16
        $r1 = $maxR * 1.05
        $r2 = $maxR * 1.22
        $x1 = $cx + [Math]::Cos($angle) * $r1
        $y1 = $cy + [Math]::Sin($angle) * $r1
        $x2 = $cx + [Math]::Cos($angle) * $r2
        $y2 = $cy + [Math]::Sin($angle) * $r2
        $g.DrawLine($tickPen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
    }

    # soft outer glow
    $glowR = $maxR * 0.62
    $glowRect = New-Object System.Drawing.RectangleF([float]($cx - $glowR * 1.8), [float]($cy - $glowR * 1.8), [float]($glowR * 3.6), [float]($glowR * 3.6))
    $glowBrush = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowBrush.AddEllipse($glowRect)
    $glowPathBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowBrush)
    $glowPathBrush.CenterColor = [System.Drawing.Color]::FromArgb(160, 229, 59, 46)
    $glowPathBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 229, 59, 46))
    $g.FillEllipse($glowPathBrush, $glowRect)

    # core lens (red eye)
    $coreR = $maxR * 0.56
    $coreRect = New-Object System.Drawing.RectangleF([float]($cx - $coreR), [float]($cy - $coreR), [float]($coreR * 2), [float]($coreR * 2))
    $corePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $corePath.AddEllipse($coreRect)
    $coreBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($corePath)
    $coreBrush.CenterColor = [System.Drawing.Color]::FromArgb(255, 255, 140, 110)
    $coreBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 190, 30, 24))
    $g.FillEllipse($coreBrush, $coreRect)

    $coreOutline = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 120, 20, 18), [Math]::Max(1, $size * 0.01))
    $g.DrawEllipse($coreOutline, $coreRect)

    # small glossy highlight
    $hlR = $coreR * 0.32
    $hlRect = New-Object System.Drawing.RectangleF([float]($cx - $coreR * 0.35), [float]($cy - $coreR * 0.55), [float]($hlR * 2), [float]($hlR * 2))
    $hlBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 255, 235, 225))
    $g.FillEllipse($hlBrush, $hlRect)

    $g.Flush()
    return $bmp
}

function ConvertTo-Png([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

$sizes = @(16, 32, 48, 256)
$pngs = @{}
foreach ($s in $sizes) {
    $bmp = New-IconLayer -size $s
    $pngs[$s] = ConvertTo-Png $bmp
    $bmp.Dispose()
}

# --- assemble .ico (ICONDIR + ICONDIRENTRY[] + PNG payloads) ---
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

$bw.Write([UInt16]0)        # reserved
$bw.Write([UInt16]1)        # type = icon
$bw.Write([UInt16]$sizes.Count)

$headerSize = 6 + (16 * $sizes.Count)
$offset = $headerSize
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $wByte = if ($s -ge 256) { 0 } else { $s }
    $hByte = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$wByte)
    $bw.Write([Byte]$hByte)
    $bw.Write([Byte]0)      # color count
    $bw.Write([Byte]0)      # reserved
    $bw.Write([UInt16]1)    # planes
    $bw.Write([UInt16]32)   # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) {
    [byte[]]$bytes = $pngs[$s]
    $bw.Write($bytes, 0, $bytes.Length)
}
$bw.Flush()

$bytes = $out.ToArray()
[System.IO.File]::WriteAllBytes("O:\MICREC\app.ico", $bytes)
[System.IO.File]::WriteAllBytes("O:\MICREC\icon.ico", $bytes)
Write-Host "Written O:\MICREC\app.ico and icon.ico ($($bytes.Length) bytes)"
