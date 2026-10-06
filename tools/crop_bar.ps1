Add-Type -AssemblyName System.Drawing

$w = 510; $h = 58
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $w, $h
"bmp type: $($bmp.GetType().FullName)"
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(1030, 1020, 0, 0, (New-Object -TypeName System.Drawing.Size -ArgumentList $w, $h))
$g.Dispose()

$out = Join-Path $PSScriptRoot 'bar_shot.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)

# 采样确认真的抓到了屏幕内容（不是全黑/全透明）
$p1 = $bmp.GetPixel(25, 30)    # 屏幕 (1055,1050)，应在插件左边缘附近
$p2 = $bmp.GetPixel(300, 30)   # 屏幕 (1330,1050)，应在插件中部
"px(1055,1050) A=$($p1.A) R=$($p1.R) G=$($p1.G) B=$($p1.B)"
"px(1330,1050) A=$($p2.A) R=$($p2.R) G=$($p2.G) B=$($p2.B)"

# 3 倍放大另存，便于辨认文字
$big = New-Object -TypeName System.Drawing.Bitmap -ArgumentList ($w * 3), ($h * 3)
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($bmp, 0, 0, ($w * 3), ($h * 3))
$g2.Dispose()
$out2 = Join-Path $PSScriptRoot 'bar_shot_3x.png'
$big.Save($out2, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose(); $bmp.Dispose()
"saved $out and $out2"
