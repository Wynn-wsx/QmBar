$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace W -Name Api -MemberDefinition @'
[DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string win);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
[DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetParent(IntPtr h);
[DllImport("user32.dll", SetLastError=true)] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

function Rect($h) {
    $r = New-Object W.Api+RECT
    [void][W.Api]::GetWindowRect($h, [ref]$r)
    return "$($r.Left),$($r.Top) - $($r.Right),$($r.Bottom)  (w=$($r.Right-$r.Left) h=$($r.Bottom-$r.Top))"
}

$tray = [W.Api]::FindWindow('Shell_TrayWnd', $null)
Write-Output "Shell_TrayWnd handle = $tray"
if ($tray -eq [IntPtr]::Zero) { Write-Output "RESULT: NO_TASKBAR"; exit 1 }
Write-Output "taskbar rect = $(Rect $tray)"

# secondary taskbars
Add-Type -Namespace W -Name E -MemberDefinition 'public delegate bool EnumProc(IntPtr h, IntPtr l); [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l); [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);'
$found = New-Object System.Collections.ArrayList
$cb = [W.E+EnumProc]{
    param($h, $l)
    $sb = New-Object System.Text.StringBuilder 256
    [void][W.E]::GetClassName($h, $sb, 256)
    if ($sb.ToString() -like 'Shell_*TrayWnd') { [void]$found.Add("$($sb.ToString()) = $h") }
    return $true
}
[void][W.E]::EnumWindows($cb, [IntPtr]::Zero)
Write-Output "all taskbar-class windows:"; $found | ForEach-Object { "  $_" }

# --- try to embed a test window ---
$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.ShowInTaskbar = $false
$form.StartPosition = 'Manual'
$form.BackColor = [System.Drawing.Color]::FromArgb(30, 120, 200)
$form.Text = 'DSH-Test'
$form.Size = New-Object System.Drawing.Size(200, 34)
$form.Location = New-Object System.Drawing.Point(300, 300)
$lbl = New-Object System.Windows.Forms.Label
$lbl.Text = 'EMBED TEST'; $lbl.ForeColor = 'White'; $lbl.Dock = 'Fill'; $lbl.TextAlign = 'MiddleCenter'
$form.Controls.Add($lbl)
$form.Show()
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 200

Write-Output "form hwnd = $($form.Handle)"
Write-Output "form rect BEFORE = $(Rect $form.Handle)"

$old = [W.Api]::SetParent($form.Handle, $tray)
Write-Output "SetParent returned (old parent) = $old ; lastErr=$([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 300

$parentNow = [W.Api]::GetParent($form.Handle)
Write-Output "GetParent now = $parentNow  (== taskbar? $($parentNow -eq $tray))"

# position it inside the taskbar's left free area
$r = New-Object W.Api+RECT
[void][W.Api]::GetWindowRect($tray, [ref]$r)
$tbH = $r.Bottom - $r.Top
$newY = $tbH - 34
if ($newY -lt 0) { $newY = 0 }
[void][W.Api]::SetWindowPos($form.Handle, [IntPtr]::Zero, 300, $newY, 200, 34, 0x0004)
[System.Windows.Forms.Application]::DoEvents()
Start-Sleep -Milliseconds 400
Write-Output "form rect AFTER embed = $(Rect $form.Handle)"
Write-Output "taskbar rect again      = $(Rect $tray)"

Start-Sleep -Seconds 3
$form.Close()
$form.Dispose()
Write-Output "RESULT: EMBED_TEST_DONE"
