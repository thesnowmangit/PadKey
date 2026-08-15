# Draws PadKey's snowman icon and packs it into a multi-size .ico.
# Run: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # everything is drawn on a 64x64 grid and scaled
    $g.ScaleTransform($s / 64.0, $s / 64.0)

    $snow    = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 244, 248, 255))
    $shade   = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 120, 140, 170)), 2.0
    $accent  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 91, 157, 249))
    $dark    = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 26, 30, 38))
    $carrot  = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 130, 50))

    # body then head, outlined so the icon survives a light taskbar
    $g.FillEllipse($snow, 13, 28, 38, 34)
    $g.DrawEllipse($shade, 13, 28, 38, 34)
    $g.FillEllipse($snow, 18, 4, 28, 28)
    $g.DrawEllipse($shade, 18, 4, 28, 28)

    # scarf in the UI accent colour, ties the icon to the app
    $g.FillRectangle($accent, 21, 30, 22, 5)
    $g.FillRectangle($accent, 38, 34, 5, 11)

    # eyes and carrot; body buttons only where there are pixels to spare
    $g.FillEllipse($dark, 25, 14, 5, 5)
    $g.FillEllipse($dark, 35, 14, 5, 5)
    $nose = New-Object System.Drawing.Drawing2D.GraphicsPath
    $nose.AddPolygon(@(
        (New-Object System.Drawing.PointF 32, 19),
        (New-Object System.Drawing.PointF 45, 22.5),
        (New-Object System.Drawing.PointF 32, 24)))
    $g.FillPath($carrot, $nose)
    if ($s -ge 24) {
        $g.FillEllipse($dark, 30, 40, 4, 4)
        $g.FillEllipse($dark, 30, 49, 4, 4)
    }

    $g.Dispose()

    if ($s -le 32) {
        # Classic 32bpp DIB entry: GDI+ (Icon.ToBitmap) cannot decode PNG entries, so the
        # sizes that actually get used at runtime stay in the universally readable format.
        # Above 32px an uncompressed DIB costs 9-16 KB each, so those go out as PNG.
        $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                              [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $px = New-Object byte[] ($s * $s * 4)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
        $bmp.UnlockBits($data)

        $ms = New-Object System.IO.MemoryStream
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $bw.Write([uint32]($s * $s * 4)); $bw.Write([int32]0); $bw.Write([int32]0)
        $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $s * 4, $s * 4) }   # bottom-up
        $maskRow = [int][Math]::Ceiling($s / 32.0) * 4
        $bw.Write((New-Object byte[] ($maskRow * $s)))                              # alpha does the masking
        $bw.Flush()
        $pngs[$s] = $ms.ToArray()
        $bw.Dispose(); $ms.Dispose()
    }
    else {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs[$s] = $ms.ToArray()
        $ms.Dispose()
    }
    if ($s -eq 64) { $bmp.Save((Join-Path $env:TEMP 'padkey-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICONDIR + ICONDIRENTRY per size + PNG payloads
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $w.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$s].Length)
    $w.Write([uint32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()

$dest = Join-Path (Split-Path -Parent $PSScriptRoot) 'padkey.ico'
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
$w.Dispose(); $out.Dispose()
"wrote $dest ({0:N0} bytes, {1} sizes)" -f (Get-Item $dest).Length, $sizes.Count
