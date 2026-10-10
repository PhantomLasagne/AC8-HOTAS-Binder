# Regenerates src\icon.ico (a flight stick on a dark tile) as a multi-size PNG icon.
# Only needed if you want to change the icon; build.cmd uses the committed icon.ico.
Add-Type -AssemblyName System.Drawing
$sizes = 256, 64, 48, 32, 16
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 256.0
    function Rect([double]$x, [double]$y, [double]$w, [double]$h) { New-Object System.Drawing.RectangleF ($x * $k), ($y * $k), ($w * $k), ($h * $k) }

    # Rounded dark tile
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 56 * $k; $e = 256 * $k
    $tile.AddArc(0, 0, $r, $r, 180, 90); $tile.AddArc($e - $r, 0, $r, $r, 270, 90)
    $tile.AddArc($e - $r, $e - $r, $r, $r, 0, 90); $tile.AddArc(0, $e - $r, $r, $r, 90, 90); $tile.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 28, 31, 38))), $tile)

    $amber = [System.Drawing.Color]::FromArgb(255, 242, 169, 59)
    $light = [System.Drawing.Color]::FromArgb(255, 230, 232, 236)
    # Base
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 70, 76, 90))), (Rect 48 178 160 44))
    # Shaft
    $pen = New-Object System.Drawing.Pen $light, ([Math]::Max(2, 20 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, [float](128 * $k), [float](196 * $k), [float](150 * $k), [float](100 * $k))
    # Grip
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $light), (Rect 112 36 80 96))
    # Trigger / button
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $amber), (Rect 136 50 34 34))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$d.Length); $w.Write([UInt32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $w.Write($d) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())
"Wrote $(Join-Path $PSScriptRoot 'icon.ico')"
