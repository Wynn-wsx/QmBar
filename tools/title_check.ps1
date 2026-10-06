param([string]$Src = "$PSScriptRoot\screen_widget_rect.png")

Add-Type -AssemblyName System.Drawing

$img = [System.Drawing.Image]::FromFile($Src)
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $img.Width, $img.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.DrawImage($img, 0, 0, $img.Width, $img.Height)
$g.Dispose()

Write-Output ("image " + $bmp.Width + "x" + $bmp.Height)

# 两行文字带：title 在 y=5..25，sub 在 y=24..42
foreach ($band in @(@(3, 26, "title"), @(24, 42, "sub"))) {
    $y0 = $band[0]; $y1 = $band[1]
    $min = 255; $max = 0; $nonBg = 0
    for ($y = $y0; $y -lt $y1; $y++) {
        for ($x = 55; $x -lt $bmp.Width; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $l = [int](0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B)
            if ($l -lt $min) { $min = $l }
            if ($l -gt $max) { $max = $l }
            if ([Math]::Abs($l - 32) -gt 25) { $nonBg++ }
        }
    }
    Write-Output ("  band " + $band[2] + " y=" + $y0 + ".." + $y1 + " lum=" + $min + ".." + $max + " nonBg=" + $nonBg)
}

# 把 title 带放大存出来看
$tb = New-Object -TypeName System.Drawing.Bitmap -ArgumentList ($bmp.Width - 55), 24
for ($y = 0; $y -lt 24; $y++) {
    for ($x = 0; $x -lt ($bmp.Width - 55); $x++) {
        $tb.SetPixel($x, $y, $bmp.GetPixel($x + 55, $y + 3))
    }
}
$big = New-Object -TypeName System.Drawing.Bitmap -ArgumentList (($bmp.Width - 55) * 4), 96
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($tb, 0, 0, $big.Width, $big.Height)
$g2.Dispose()
$out = Join-Path $PSScriptRoot "titleband_4x.png"
$big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output ("saved " + $out)
$big.Dispose(); $tb.Dispose(); $bmp.Dispose(); $img.Dispose()
