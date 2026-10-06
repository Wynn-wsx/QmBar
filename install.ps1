# 安装 QmBar：创建桌面/开始菜单快捷方式，可选开机自启。
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -AutoStart
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
param(
    [switch]$AutoStart,
    [switch]$StartMenuOnly,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Definition }
$exe = Join-Path $root 'bin\QmBar.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'QmBar'

$desktop = [Environment]::GetFolderPath('Desktop')
$lnkDesktop = Join-Path $desktop 'QmBar 任务栏播控.lnk'
$lnkStart = Join-Path ([Environment]::GetFolderPath('Programs')) 'QmBar 任务栏播控.lnk'

function New-Shortcut([string]$path, [string]$target) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($path)
    $sc.TargetPath = $target
    $sc.WorkingDirectory = Split-Path -Parent $target
    $sc.Description = '任务栏快捷播控（QQ音乐 / 任意播放器）'
    $sc.Save()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}

if ($Uninstall) {
    foreach ($p in @($lnkDesktop, $lnkStart)) {
        if (Test-Path $p) { Remove-Item $p -Force; Write-Host "已删除 $p" }
    }
    if (Get-ItemProperty -Path $runKey -Name $valueName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $valueName -Force
        Write-Host '已取消开机自启'
    }
    Write-Host '卸载完成（配置文件与歌词缓存保留在 %APPDATA%\QmBar，可手动删除）'
    return
}

if (-not (Test-Path $exe)) {
    Write-Host "找不到 $exe —— 请先编译：powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1"
    exit 1
}

New-Shortcut -path $lnkStart -target $exe
Write-Host "已创建开始菜单快捷方式：$lnkStart"
if (-not $StartMenuOnly) {
    New-Shortcut -path $lnkDesktop -target $exe
    Write-Host "已创建桌面快捷方式：$lnkDesktop"
}

if ($AutoStart) {
    # 用 --autostart 启动：不弹提示、直接静默挂到任务栏
    Set-ItemProperty -Path $runKey -Name $valueName -Value ('"' + $exe + '" --autostart')
    Write-Host '已设置开机自启（也可在控件右键菜单里开关）'
}

Write-Host ''
Write-Host '下一步：双击快捷方式运行；右键控件可打开设置菜单。'


