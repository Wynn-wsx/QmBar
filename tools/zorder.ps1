$ErrorActionPreference = 'Continue'

Add-Type -AssemblyName System.Drawing
$code = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
  public static string Txt(IntPtr h) { StringBuilder s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
}
'@
Add-Type -TypeDefinition $code

function Cls($h) { [W]::Cls($h) }
function Rct($h) { $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r); "[$($r.L),$($r.T) - $($r.R),$($r.B)]" }

$mine = [IntPtr]13897446
$tray = [W]::GetAncestor($mine, 3)   # GA_ROOTOWNER
$parent = [W]::GetParent($mine)
"mine   = $($mine.ToInt64()) cls=$(Cls $mine) vis=$([W]::IsWindowVisible($mine)) rect=$(Rct $mine)"
$cl = 0
[void][W]::DwmGetWindowAttribute($mine, 14, [ref]$cl, 4)
"DWMWA_CLOAKED = $cl   (0 = not cloaked)"
"parent = $($parent.ToInt64()) cls=$(Cls $parent)"

"--- z-order: walk GW_HWNDPREV (upward) from mine ---"
$h = [W]::GetWindow($mine, 3)   # GW_HWNDPREV
while ($h -ne [IntPtr]::Zero -and $parent -ne [IntPtr]::Zero) {
  "  ABOVE: $($h.ToInt64()) cls=$(Cls $h) rect=$(Rct $h) vis=$([W]::IsWindowVisible($h))"
  $h = [W]::GetWindow($h, 3)
}
"--- z-order: walk GW_HWNDNEXT (downward) from mine ---"
$h = [W]::GetWindow($mine, 2)   # GW_HWNDNEXT
while ($h -ne [IntPtr]::Zero) {
  "  BELOW: $($h.ToInt64()) cls=$(Cls $h) rect=$(Rct $h) vis=$([W]::IsWindowVisible($h))"
  $h = [W]::GetWindow($h, 2)
}

"--- PrintWindow(ours, client only) ---"
$r = New-Object W+RECT
[void][W]::GetWindowRect($mine, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [W]::PrintWindow($mine, $hdc, 1)
$g.ReleaseHdc($hdc)
$g.Dispose()
"PrintWindow ok=$ok size=${w}x${ht}"
$colors = @{}
for ($yy = 0; $yy -lt $ht; $yy += 4) {
  for ($xx = 0; $xx -lt $w; $xx += 4) {
    $p = $bmp.GetPixel($xx, $yy)
    $key = "$($p.R),$($p.G),$($p.B)"
    $colors[$key] = 1 + [int]$colors[$key]
  }
}
"distinct colors sampled: $($colors.Count)"
$colors.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 8 | ForEach-Object { "  $($_.Key) x$($_.Value)" }
$out = Join-Path $PSScriptRoot 'printwindow_ours.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved $out"
