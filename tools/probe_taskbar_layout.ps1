$ErrorActionPreference = 'Stop'
Add-Type -Namespace T -Name Api -MemberDefinition @'
[DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string win);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);
[DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll", SetLastError=true)] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetParent(IntPtr h);
[DllImport("shcore.dll")] public static extern int GetScaleFactorForMonitor(IntPtr hmon, out int scale);
[DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
[DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);
[DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

$tray = [T.Api]::FindWindow('Shell_TrayWnd', $null)
$tr = New-Object T.Api+RECT
[void][T.Api]::GetWindowRect($tray, [ref]$tr)
Write-Output "TASKBAR rect: L=$($tr.Left) T=$($tr.Top) R=$($tr.Right) B=$($tr.Bottom)  h=$($tr.Bottom - $tr.Top)"
Write-Output ""
Write-Output "CHILDREN of Shell_TrayWnd (class @ rect):"
$h = [IntPtr]::Zero
$rows = @()
while ($true) {
    $h = [T.Api]::FindWindowEx($tray, $h, $null, $null)
    if ($h -eq [IntPtr]::Zero) { break }
    $sb = New-Object System.Text.StringBuilder 256
    [void][T.Api]::GetClassName($h, $sb, 256)
    $r = New-Object T.Api+RECT
    [void][T.Api]::GetWindowRect($h, [ref]$r)
    $vis = [T.Api]::IsWindowVisible($h)
    $rows += [pscustomobject]@{ Class = $sb.ToString(); L = $r.Left; T = $r.Top; R = $r.Right; B = $r.Bottom; W = $r.Right - $r.Left; Visible = $vis }
    $h = $h
}
$rows | Sort-Object L | Format-Table -AutoSize | Out-String -Width 200 | Write-Output

Write-Output "GRANDCHILDREN of the task list / tray containers:"
foreach ($row in ($rows | Where-Object { $_.W -gt 0 })) {
    $p = [T.Api]::FindWindowEx($tray, [IntPtr]::Zero, $null, $null)
    # find by class again to get handle
    $hh = [IntPtr]::Zero
    while ($true) {
        $hh = [T.Api]::FindWindowEx($tray, $hh, $null, $null)
        if ($hh -eq [IntPtr]::Zero) { break }
        $sb2 = New-Object System.Text.StringBuilder 256
        [void][T.Api]::GetClassName($hh, $sb2, 256)
        if ($sb2.ToString() -eq $row.Class) {
            $c = [IntPtr]::Zero
            while ($true) {
                $c = [T.Api]::FindWindowEx($hh, $c, $null, $null)
                if ($c -eq [IntPtr]::Zero) { break }
                $sb3 = New-Object System.Text.StringBuilder 256
                [void][T.Api]::GetClassName($c, $sb3, 256)
                $r3 = New-Object T.Api+RECT
                [void][T.Api]::GetWindowRect($c, [ref]$r3)
                "  $($row.Class) -> $($sb3.ToString())  @ $($r3.Left),$($r3.Top)-$($r3.Right),$($r3.Bottom)"
            }
            break
        }
    }
}

Write-Output ""
Write-Output "DPI / SCALING:"
$mon = [T.Api]::MonitorFromWindow($tray, 2)
$scale = 0
$rc = [T.Api]::GetScaleFactorForMonitor($mon, [ref]$scale)
"MonitorFromWindow(taskbar) = $mon   GetScaleFactorForMonitor rc=$rc scale=$scale%"
$dc = [T.Api]::GetDC([IntPtr]::Zero)
"Screen LOGPIXELSX = $([T.Api]::GetDeviceCaps($dc, 88))  (96 = 100%, 120 = 125%)"

Write-Output ""
Write-Output "OTHER MONITORS (Shell_SecondaryTrayWnd):"
Add-Type -Namespace T -Name E -MemberDefinition 'public delegate bool EnumProc(IntPtr h, IntPtr l); [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int Left, Top, Right, Bottom; }'
$cb = [T.E+EnumProc]{
    param($hw, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][T.E]::GetClassName($hw, $sb, 256)
    $cn = $sb.ToString()
    if ($cn -eq 'Shell_SecondaryTrayWnd' -or $cn -eq 'Shell_TrayWnd') {
        $r = New-Object T.E+RECT
        [void][T.E]::GetWindowRect($hw, [ref]$r)
        "  $cn @ $($r.Left),$($r.Top)-$($r.Right),$($r.Bottom)"
    }
    return $true
}
[void][T.E]::EnumWindows($cb, [IntPtr]::Zero)
