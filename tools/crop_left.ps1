Add-Type -AssemblyName System.Drawing

# 抓插件左侧 100x56（屏幕 1040,1028），6 倍放大，用来确认走马灯文字有没有越界压到封面。
$w = 100
$h = 56
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(1040, 1028, 0, 0, (New-Object -TypeName System.Drawing.Size -ArgumentList $w, $h))
$g.Dispose()

$w6 = 600
$h6 = 336
$big = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $w6, $h6
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.DrawImage($bmp, 0, 0, $w6, $h6)
$g2.Dispose()
$big.Save((Join-Path $PSScriptRoot 'left6x.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
$bmp.Dispose()
"ok"

