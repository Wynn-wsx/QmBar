Add-Type -AssemblyName System.Drawing

# 抓插件自己的矩形，逐行统计「非背景」像素数，用来确认第一行（歌名）到底画没画。
$W = 460
$H = 48
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(1051, 1032, 0, 0, (New-Object -TypeName System.Drawing.Size -ArgumentList $W, $H))
$g.Dispose()

$bg = $bmp.GetPixel(455, 24)   # 右侧空白处当背景色
Write-Output ("bg = " + $bg.R + "," + $bg.G + "," + $bg.B)

for ($y = 0; $y -lt $H; $y++) {
    $n = 0
    for ($x = 56; $x -lt 320; $x++) {
        $c = $bmp.GetPixel($x, $y)
        $d = [Math]::Abs($c.R - $bg.R) + [Math]::Abs($c.G - $bg.G) + [Math]::Abs($c.B - $bg.B)
        if ($d -gt 60) { $n++ }
    }
    Write-Output ("row " + $y.ToString("00") + " nonbg=" + $n)
}

$bmp.Save((Join-Path $PSScriptRoot 'widget_1x.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
