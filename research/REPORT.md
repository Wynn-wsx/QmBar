# QQ音乐逐字歌词（QRC）获取 — 实测报告

环境：Windows / 无 pwsh7（`pwsh` 实为 Windows PowerShell 5.1 Desktop）/ python 3.14
工作目录：本仓库根目录
所有请求**未使用任何 cookie / 登录态**，本机实测成功。

---

## SEARCH

**旧接口已死**：`https://c.y.qq.com/soso/fcgi-bin/client_search_cp` → `HTTP/1.1 500 Internal Server Error`，`Server: nginx`，`Content-Length: 0`（空 body，带不带 Referer 都一样）。

**现行接口（POST，推荐）**

```
POST https://u.y.qq.com/cgi-bin/musicu.fcg
Content-Type: application/json
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36

{"comm":{"ct":"19","cv":"1859","uin":"0"},"req":{"method":"DoSearchForQQMusicDesktop","module":"music.search.SearchCgiService","param":{"num_per_page":"3","page_num":"1","query":"晴天 周杰伦","search_type":0}}}
```

**GET 等价形式**（结果字节级一致）：

```
https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data=<urlencode(上面的JSON)>
```

- `search_type`: 0=单曲, 2=专辑, 3=歌单
- HTTP 200，body 约 63–69 KB
- **不需要 Referer**，UA 不限（`python-urllib/3.14` 也 200）

**JSON 路径**

```
code                       -> 0
req.code                   -> 0
req.data.body.song.list[]  -> 候选数组
  [].mid        -> songmid，14 字符  ← 就是它
  [].id         -> 数字 songID（QRC 请求必填）
  [].title      -> 歌名
  [].singer[].name
  [].album.title
  [].file.media_mid
```

**实测响应片段**（`query=晴天 周杰伦`）

```
mid=0039MnYb0qxYhV id=97773 title=晴天 singers=周杰伦 album=叶惠美
mid=004Fs2FP1EvZYc title=晴天 (Live) ...
mid=001398Rf3gykz8 title=晴天 (Live) ...
```

空 query → `list` 长度 0。无关词也能返回 15 条（模糊匹配），所以**必须自己做打分**（标题精确匹配 + 歌手匹配）。

---

## LRC

**旧接口（可用，但必须带 Referer）**

```
GET https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid=0039MnYb0qxYhV&format=json&nobase64=1&g_tk=5381&loginUin=0&hostUin=0&inCharset=utf8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=0
Referer: https://y.qq.com/
User-Agent: Mozilla/5.0 ... Chrome/126.0.0.0 Safari/537.36
```

- ✅ 带 Referer → 200，2671 B：
  `{"retcode":0,"code":0,"subcode":0,"lyric":"[ti:晴天]\n[ar:周杰伦]\n[al:叶惠美]\n[by:]\n[offset:0]\n[00:00.00]晴..."}`
- ❌ **不带 Referer** → 200 但只 46 B：`{"retcode":-1310,"code":-1310,"subcode":-1310}`
- 去掉 `nobase64=1` → `lyric` 为 base64（`W3RpOuaZtOWkqV0K...`），需 base64 解码。

**现代接口取 LRC**：`GetPlayLyricInfo` + `"format":"lrc"` —— **实测返回的仍是同一份加密 QRC**（`format` 参数被忽略）。所以不要指望它给明文 LRC，直接用 QRC 流程即可。

---

## QRC — VERDICT：✅ 无需登录，可获取并成功解密

**旧接口已废**：`https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg?version=15&miniversion=82&lrctype=4&musicid=0039MnYb0qxYhV` → 200 但是空壳 XML：`<result>0</result>` + `musicid="0"` + `<content ...><![CDATA[]]></content>`（原文/译文/罗马音全空）。

**现行接口**

```
POST https://u.y.qq.com/cgi-bin/musicu.fcg
Content-Type: application/json
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36

{"comm":{"ct":11,"cv":"1003006","v":"1003006","uin":"0","format":"json","platform":"wk_v17"},"req":{"module":"music.musichallSong.PlayLyricInfo","method":"GetPlayLyricInfo","param":{"songMID":"0039MnYb0qxYhV","songID":97773,"format":"qrc","qrc":1,"trans":1,"roma":1,"crypt":1,"lrc_t":0,"interval":0}}}
```

- HTTP 200，11617 B，`code:0`
- **不需要 Referer**（无 Referer + `python-urllib/3.14` 也 200 / 11599 B，内容一致）
- `crypt:0` 与 `crypt:1` 返回**完全相同的密文** → crypt 参数无意义
- GET 形式同样可用：`https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data=<urlencode(JSON)>` → 200，11599 B
- `req.data` 键：`songID songName songType singerName qrc crypt lyric trans roma lrc_t qrc_t trans_t roma_t lyric_style classical introduceTitle introduceText vecSongID track startTs transSource hasContributor hasTransContributor singingAnnotationsLyric singingAnnotationsTs hasMultiTrans lt_lyric lt_lyric_t`

**解密流程（实测通过）**

```
lyric 字段(hex 字符串)
  -> bytes.fromhex()
  -> QQMusic 自定义 TripleDES（不是标准 DES！）密钥 = ASCII "!@#)(*$%123ZXC!@!@#)(NHL" (24 字节)
  -> zlib.decompress()
  -> UTF-8 解码（可能带 BOM，需 strip）
```

- 密文长度恒为 8 的倍数（晴天 9808 hex = 4904 B）
- 来源实现：`WXRIW/Lyricify-Lyrics-Helper` → `Lyricify.Lyrics.Helper/Decrypter/Qrc/Decrypter.cs`（`QQKey = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL")`、`DESHelper.TripleDESKeySetup` + `TripleDESCrypt` + `SharpZipLibDecompress`）与同目录 `DESHelper.cs`
- **纯 Python 移植已写好并实测通过**：`research/qqmusic_des.py`，导出 `decrypt_3des(data, key)` 与 `decrypt_qrc(hex_or_bytes, key=b"!@#)(*$%123ZXC!@!@#)(NHL")`（内部完成 zlib + 去 BOM，返回 `str`）
- 3DES 密钥编排（DECRYPT 方向）：`schedule[2]=KeySchedule(key[0:8],DECRYPT)`、`schedule[1]=KeySchedule(key[8:16],ENCRYPT)`、`schedule[0]=KeySchedule(key[16:24],DECRYPT)`，按 `0,1,2` 顺序 Crypt
- 自定义 DES 的关键差异：`SBOXBIT(a)=((a&0x20)|((a&0x1f)>>1)|((a&1)<<4))`；`KeySchedule` 中 `C=((C<<s)|(C>>(28-s)))&0xfffffff0`；`F()` 内置 P 置换；`InvIP` 非常规字节写回顺序 `input[3],input[2],input[1],input[0],input[7],input[6],input[5],input[4]`
- **pycryptodome 标准 3DES 无效**：`DES3.new(QQKey, MODE_ECB)` 直接解、以及密文/密钥的 4 字节组内反转、8 字节块反转、全字节位反转及组合（20+ 变体）**全部失败**（zlib header 校验全不过）

**实测解密结果**（`qrc_plain.txt`，9821 B）

```xml
<?xml version="1.0" encoding="utf-8"?><QrcInfos><QrcHeadInfo SaveTime="269" Version="100"/><LyricInfo LyricCount="1"><Lyric_1 LyricType="1" LyricContent="[ti:晴天]
[ar:周杰伦]
[al:叶惠美]
[by:]
[offset:0]
[0,2250]晴(0,160)天(160,160) (320,160)-(480,160) (640,160)周(800,160)杰(960,160)伦(1120,160)...
[2250,2250]词(2250,450)：(2700,450)周(3150,450)杰(3600,450)伦(4050,450)...
```

**QRC 行格式**：`[行起始ms,行时长ms]字(字起始ms,字时长ms)字(字起始ms,字时长ms)...`
（注意行内字的时间是**绝对 ms**，不是相对行首）

**trans / roma**：同样是 hex 密文，**用完全相同的算法解密**。

```
Shape of You (mid=0041gObR2QG98x, id=200369396)
  lyric -> 12473 chars OK   QRC 逐字
  trans -> 2562 chars OK    "[00:00.00]//\n[00:02.38]//...[00:09.55]俱乐部不是找另一半的绝佳场所..."
  roma  -> EMPTY
Hello / Adele (mid=002Il7Ya1tZ6UZ, id=105738437)
  lyric 5936 hex, trans 2256 hex (解密 OK)
晴天 -> trans/roma 均为空
```

**返回值分支（实测）**

| 情况 | 表现 |
|---|---|
| 正常 | `code:0` / `req.code:0` / `lyric` = hex 密文 |
| 无版权歌词 | `req.code: 24001`，`lyric` 为空字符串（HTTP 仍 200） |
| 有版权无歌词（如伴奏） | `req.code:0`，`lyric` 为空字符串 |
| 纯音乐占位 | `lyric` 只有 144 hex，解密后是**明文 LRC** `[00:00:00]此歌曲为没有填词的纯音乐，请您欣赏` |
| 用户上传质量差 | QRC 存在但全为 `(0,0)` 零时长；或 `[11596,2270]` 行首偏移但首行无时间 |

→ **解析器必须同时兼容 QRC 和纯 LRC**（占位曲返回的是 `[00:00:00]...` 而非 QRC）。

**易混淆的另一套方案**：本地 `.qrc` 文件（Lyricify 导出的那种）用不同密钥 + XOR + zlib，`blockshy/LyricTools` 的 `java/utils/QrcLyricDecrypter.java` 用 `KEY1="!@#)(NHLiuy*$%^&"`、`KEY2="123ZXC!@#)(*$%^&"`、`KEY3="!@#)(*$%^&abcDEF"` + `XOR_KEY`。**与本 API 响应解密无关，勿混用。**
`instr3/QRCDecoder` 依赖 32 位原生 DLL（`msl.loadlib`），不可移植。

---

## FALLBACK

**LRCLIB（本机可用，无需 Referer / 无需 cookie）**

```
GET https://lrclib.net/api/get?artist_name=<urlencode>&track_name=<urlencode>
User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36
```

- `?artist_name=周杰伦&track_name=晴天` → 200，9352 B：
  `{"id":17788,"name":"晴天","trackName":"晴天","artistName":"周杰伦","albumName":"叶惠美","duration":270.0,"instrumental":false,"hasWordSync":false,"plainLyrics":"故事的小黃花...","syncedLyrics":"[00:29.36] 故事的小黃花\n[00:32.77] 從出生那年就飄著..."}`
- `?artist_name=Ed Sheeran&track_name=Shape of You` → 200，18662 B
- 模糊搜索：`GET https://lrclib.net/api/search?q=<urlencode>` → 200，185489 B，20 条结果（晴天 命中 `id:7976416`）
- 未命中：`GET /api/get?artist_name=zzzz&track_name=qqqqqq` → **404** `{"message":"Failed to find specified track","name":"TrackNotFound","statusCode":404}`
- ⚠️ **`hasWordSync: false`** —— LRCLIB **只有逐行 LRC，没有逐字时间轴**。歌词是繁体、时间轴与 QQ 官方不完全一致。只能当降级兜底。

---

## REQUIRED HEADERS

| 接口 | User-Agent | Referer | Cookie | Content-Type |
|---|---|---|---|---|
| `u.y.qq.com/cgi-bin/musicu.fcg` 搜索 | 不限 | **不需要** | 不需要 | POST 时必需 `application/json` |
| `u.y.qq.com/cgi-bin/musicu.fcg` `GetPlayLyricInfo` | 不限 | **不需要** | 不需要 | POST 时必需 `application/json` |
| `c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg` | 不限 | **必需 `https://y.qq.com/`** | 不需要 | — |
| `lrclib.net/api/*` | 常规 Chrome UA 即可 | 不需要 | 不需要 | — |

- **限流/反爬：未观测到**。对 `GetPlayLyricInfo` 连续 12 次快速请求：12/12 → `200`，`code:0`，`lyriclen:9808`，总耗时 **1.62 s**（约 7.4 req/s）。
- 全程无 cookie、无登录态。

---

## UNRESOLVED

- **Windows PowerShell 5.1 的 `Invoke-WebRequest` 在本机非交互模式下不可用**（报「Windows PowerShell 处于非交互模式。朗读和提示功能不可用。」，IE 解析引擎），本次全部改用 `curl.exe` / python `urllib` 实测。若控件要用 PS 抓取，需避免 `Invoke-WebRequest`。
- 本机**没有 pwsh 7**（`pwsh` = PowerShell 5.1 Desktop）。
- 未在**带登录 cookie** 的条件下测试旧 `lyric_download.fcg` 是否恢复（假设无 cookie 故判废）。
- 未测试 VIP / 付费歌曲（`code != 0` 之外的其他错误码）。
- `singingAnnotationsLyric` / `lt_lyric` / `vecSongID` 等字段未解析，用途未知。
- 未验证 `search_type=2/3`（专辑/歌单）的返回结构。
- 未使用 `ztmy_saturn.qrc` 已知测试向量交叉验证 DES 移植（已由多首真实 QQ 响应自证：晴天 / Shape of You / Hello / 失恋俱乐部 等 20+ 首均解密成功）。

---

## 关键结论（一句话）

**QRC 逐字时间轴无需登录即可拿到**：搜索拿 `mid`+`id` → `GetPlayLyricInfo`（`format:"qrc"`）取 hex 密文 → **QQMusic 自定义 3DES**（密钥 `!@#)(*$%123ZXC!@!@#)(NHL`）→ zlib → XML 里的 `LyricContent` 就是逐字 QRC。纯 Python 实现已在 `qqmusic_des.py` 验证通过。
