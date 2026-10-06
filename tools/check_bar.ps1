Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W2 {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, int flags);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassName(h,sb,256); return sb.ToString(); }
  public static string Txt(IntPtr h){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); return sb.ToString(); }
  public static string Info(IntPtr h){
    RECT r; GetWindowRect(h, out r);
    return string.Format("hwnd={0} cls={1} text='{2}' rect=[{3},{4} - {5},{6}] ({7}x{8}) vis={9} parent={10} root={11} style=0x{12:X8} ex=0x{13:X8}",
      h, Cls(h), Txt(h), r.L, r.T, r.R, r.B, r.R-r.L, r.B-r.T, IsWindowVisible(h), GetParent(h), GetAncestor(h,2),
      GetWindowLong(h,-16), GetWindowLong(h,-20));
  }
}
"@

$proc = Get-Process QmBar -ErrorAction SilentlyContinue | Select-Object -First 1
$target = $proc.Id
"QmBar pid = $target"

$tray = [W2]::FindWindow("Shell_TrayWnd", $null)
"Shell_TrayWnd = $tray"

"=== ALL Shell_TrayWnd children ==="
$all = New-Object System.Collections.ArrayList
$cb1 = [W2+EnumProc]{ param($h,$p) [void]$all.Add($h); return $true }
[void][W2]::EnumChildWindows($tray, $cb1, [IntPtr]::Zero)
"count=$($all.Count)"
foreach ($h in $all) {
  $hpid = 0; [void][W2]::GetWindowThreadProcessId($h, [ref]$hpid)
  $mark = if ($hpid -eq $target) { '  <<< QmBar' } else { '' }
  "  " + [W2]::Info($h) + " pid=$hpid$mark"
}

"=== ALL top-level windows of QmBar pid ==="
$top = New-Object System.Collections.ArrayList
$cb2 = [W2+EnumProc]{
  param($h,$p)
  $pp = 0; [void][W2]::GetWindowThreadProcessId($h, [ref]$pp)
  if ($pp -eq $target) { [void]$top.Add($h) }
  return $true
}
[void][W2]::EnumWindows($cb2, [IntPtr]::Zero)
"count=$($top.Count)"
foreach ($h in $top) { "  " + [W2]::Info($h) }
