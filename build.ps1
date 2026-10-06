#requires -version 5
<#
  QmBar 构建脚本 —— 只用 Windows 自带的 .NET Framework 编译器，零下载、零依赖。
  用法：
    powershell -ExecutionPolicy Bypass -File build.ps1             # 编译到 bin\QmBar.exe
    powershell -ExecutionPolicy Bypass -File build.ps1 -Run        # 编译后直接运行
    powershell -ExecutionPolicy Bypass -File build.ps1 -Clean      # 清理产物
#>
param(
    [switch]$Run,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src  = Join-Path $Root 'src'
$Out  = Join-Path $Root 'bin'
$Exe  = Join-Path $Out 'QmBar.exe'

if ($Clean) {
    if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
    Write-Host "已清理 $Out" -ForegroundColor Yellow
    return
}

$Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $Csc)) {
    $Csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $Csc)) { throw "找不到 csc.exe（需要 .NET Framework 4.x）" }

$FxDir = Split-Path -Parent $Csc
$Refs = @(
    (Join-Path $FxDir 'System.dll'),
    (Join-Path $FxDir 'System.Core.dll'),
    (Join-Path $FxDir 'System.Drawing.dll'),
    (Join-Path $FxDir 'System.Windows.Forms.dll'),
    (Join-Path $FxDir 'System.Runtime.WindowsRuntime.dll')
)
foreach ($r in $Refs) {
    if (-not (Test-Path $r)) { throw "缺少引用程序集: $r" }
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null

$Sources = Get-ChildItem -Path $Src -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $Sources) { throw "src 目录下没有 .cs 文件" }

$refArgs = $Refs | ForEach-Object { '/r:' + $_ }

Write-Host "编译器: $Csc" -ForegroundColor DarkGray
Write-Host "源文件: $($Sources.Count) 个" -ForegroundColor DarkGray

# /target:winexe = 无控制台窗口；/platform:anycpu = 64 位系统上以 64 位运行
# /codepage:65001 = 源文件按 UTF-8 读取（否则无 BOM 的 .cs 会被按 GBK 解码，中文全乱码）
& $Csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /codepage:65001 `
    "/out:$Exe" /utf8output @refArgs @Sources

if ($LASTEXITCODE -ne 0) { throw "编译失败（exit=$LASTEXITCODE）" }

$fi = Get-Item $Exe
Write-Host ("编译成功: {0}  ({1:N0} 字节)" -f $fi.FullName, $fi.Length) -ForegroundColor Green

if ($Run) {
    Write-Host "启动中…（日志: $env:APPDATA\QmBar\QmBar.log）" -ForegroundColor Cyan
    Start-Process -FilePath $Exe
}
