Add-Type -AssemblyName System.Drawing

# Capture exactly the widget rect from the composited screen
$x = 1051; $y = 1032; $w = 460; $h = 48
$shot = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $w, $h
$g = [System.Drawing.Graphics]::FromImage($shot)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object -TypeName System.Drawing.Size -ArgumentList $w, $h))
$g.Dispose()
$out = Join-Path $PSScriptRoot 'screen_widget_rect.png'
$shot.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)

$ref = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'printwindow_ours.png'))

$sum = 0.0; $n = 0; $same = 0
for ($yy = 0; $yy -lt $h; $yy++) {
  for ($xx = 0; $xx -lt $w; $xx++) {
    $a = $shot.GetPixel($xx, $yy); $b = $ref.GetPixel($xx, $yy)
    $d = [math]::Abs($a.R - $b.R) + [math]::Abs($a.G - $b.G) + [math]::Abs($a.B - $b.B)
    $sum += $d; $n++
    if ($d -le 12) { $same++ }
  }
}
"screen-capture vs PrintWindow(ours): meanAbsDiff={0:N2}  pixelsWithin12={1:P1}" -f ($sum / $n), ($same / $n)

# 4x zoom of the exact rect for eyeballing
$big = New-Object -TypeName System.Drawing.Bitmap -ArgumentList ($w * 4), ($h * 4)
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($shot, 0, 0, ($w * 4), ($h * 4))
$g2.Dispose()
$out2 = Join-Path $PSScriptRoot 'screen_widget_rect_4x.png'
$big.Save($out2, [System.Drawing.Imaging.ImageFormat]::Png)

# 采样 Widget 中几个已知不同颜色的点（封面/文字/按钮）在屏幕上的实际值
$probe = @(@(20,24), @(120,24), @(300,20), @(420,24), @(60,46), @(400,46))
foreach ($p in $probe) {
  $s = $shot.GetPixel($p[0], $p[1]); $r = $ref.GetPixel($p[0], $p[1])
  "  ({0},{1}) screen=({2},{3},{4}) printwindow=({5},{6},{7})" -f $p[0], $p[1], $s.R, $s.G, $s.B, $r.R, $r.G, $r.B
}

$shot.Dispose(); $ref.Dispose(); $big.Dispose()
"saved $out2"
