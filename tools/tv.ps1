$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $PSScriptRoot 'tv'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$body = '{"comm":{"ct":11,"cv":"1003006","v":"1003006","uin":"0","format":"json","platform":"wk_v17"},"req":{"module":"music.musichallSong.PlayLyricInfo","method":"GetPlayLyricInfo","param":{"songMID":"0039MnYb0qxYhV","songID":97773,"format":"qrc","qrc":1,"trans":1,"roma":1,"crypt":1,"lrc_t":0,"interval":0}}}'
Write-Host "POST body bytes: $($body.Length)"
$ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36'
Set-Content -Path (Join-Path $dir 'body.json') -Value $body -Encoding Ascii -NoNewline
Push-Location $dir
try {
    $resp = & curl.exe -s -S -X POST -H 'Content-Type: application/json' -H "User-Agent: $ua" --data-binary '@body.json' 'https://u.y.qq.com/cgi-bin/musicu.fcg' 2>&1
} finally { Pop-Location }
Write-Host "resp type: $($resp.GetType().FullName)  len: $(([string]$resp).Length)"
Write-Host "resp head: $(([string]$resp).Substring(0, [Math]::Min(200, ([string]$resp).Length)))"

$json = [string]$resp
Set-Content -Path (Join-Path $dir 'raw.json') -Value $json -Encoding UTF8
$o = $json | ConvertFrom-Json
Write-Host "topcode=$($o.code) reqcode=$($o.req.code)"
$hex = [string]$o.req.data.lyric
Write-Host "lyricHexLen=$($hex.Length)"
Set-Content -Path (Join-Path $dir 'qrc.hex') -Value $hex -Encoding Ascii

$py = 'python'
$code = @"
import sys
root, d = sys.argv[1], sys.argv[2]
sys.path.insert(0, root)
import qqmusic_des as qd
h = open(d + r'\qrc.hex').read().strip()
if not h:
    print('EMPTY HEX'); sys.exit(1)
open(d + r'\expected.txt', 'wb').write(qd.decrypt_qrc(h).encode('utf-8'))
print('python decrypt ok')
"@
Set-Content -Path (Join-Path $dir 'ref.py') -Value $code -Encoding Ascii
& $py (Join-Path $dir 'ref.py') $root $dir
if ($LASTEXITCODE -ne 0) { throw "python failed" }

& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 /codepage:65001 `
    /out:"$dir\DesTest.exe" /r:System.dll /r:System.Core.dll `
    "$root\src\QqDes.cs" "$root\probe\cs\DesTest.cs"
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
Write-Host "csc ok"

& "$dir\DesTest.exe" "$dir\qrc.hex" "$dir\actual.txt"
$he = (Get-FileHash (Join-Path $dir 'expected.txt')).Hash
$ha = (Get-FileHash (Join-Path $dir 'actual.txt')).Hash
Write-Host "expected=$he"
Write-Host "actual  =$ha"
if ($he -eq $ha) { Write-Host "=== BYTE-IDENTICAL ===" } else { Write-Host "=== MISMATCH ===" }
Write-Host "--- first 200 chars ---"
(Get-Content (Join-Path $dir 'actual.txt') -Encoding UTF8 -Raw).Substring(0, 200)
