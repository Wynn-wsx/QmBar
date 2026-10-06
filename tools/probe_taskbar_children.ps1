$ErrorActionPreference = 'Stop'
Add-Type -Namespace T2 -Name Api -MemberDefinition @'
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc p, IntPtr l);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@

$tray = [IntPtr]::Zero
$all = New-Object System.Collections.ArrayList
$cb0 = [T2.Api+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][T2.Api]::GetClassNameW($h, $sb, 256)
    if ($sb.ToString() -eq 'Shell_TrayWnd') { $script:tray = $h }
    [void]$all.Add($h)
    return $true
}
[void][T2.Api]::EnumWindows($cb0, [IntPtr]::Zero)
Write-Output "Shell_TrayWnd = $script:tray ; total top-level windows enumerated = $($all.Count)"

if ($script:tray -ne [IntPtr]::Zero) {
    $tr = New-Object T2.Api+RECT
    [void][T2.Api]::GetWindowRect($script:tray, [ref]$tr)
    Write-Output "TASKBAR: $($tr.Left),$($tr.Top) - $($tr.Right),$($tr.Bottom)  h=$($tr.Bottom - $tr.Top)"
    Write-Output ""
    Write-Output "ALL CHILDREN (recursive, visible), sorted by X:"
    $rows = New-Object System.Collections.ArrayList
    $cb1 = [T2.Api+EnumProc]{
        param($h, $l)
        if ([T2.Api]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][T2.Api]::GetClassNameW($h, $sb, 256)
            $tb = New-Object System.Text.StringBuilder 256
            [void][T2.Api]::GetWindowTextW($h, $tb, 256)
            $r = New-Object T2.Api+RECT
            [void][T2.Api]::GetWindowRect($h, [ref]$r)
            [void]$rows.Add([pscustomobject]@{
                Class = $sb.ToString(); Text = $tb.ToString();
                L = $r.Left; R = $r.Right; T = $r.Top; B = $r.Bottom; W = $r.Right - $r.Left
            })
        }
        return $true
    }
    [void][T2.Api]::EnumChildWindows($script:tray, $cb1, [IntPtr]::Zero)
    $rows | Sort-Object L | Format-Table -AutoSize | Out-String -Width 220 | Write-Output

    Write-Output "FREE GAPS inside taskbar (x ranges not covered by any visible child):"
    $covered = ($rows | Where-Object { $_.W -gt 0 } | Sort-Object L)
    $cursor = $tr.Left
    foreach ($c in $covered) {
        if ($c.L - $cursor -gt 40) { "  GAP: $cursor .. $($c.L)   (width $($c.L - $cursor))" }
        if ($c.R -gt $cursor) { $cursor = $c.R }
    }
    if ($tr.Right - $cursor -gt 40) { "  GAP: $cursor .. $($tr.Right)   (width $($tr.Right - $cursor))" }
}
