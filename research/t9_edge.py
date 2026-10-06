# -*- coding: utf-8 -*-
import json, sys, time, urllib.request, urllib.error
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0.0 Safari/537.36"

def post(payload, retries=0):
    r = urllib.request.Request("https://u.y.qq.com/cgi-bin/musicu.fcg",
        data=json.dumps(payload, ensure_ascii=False).encode(),
        headers={"User-Agent": UA, "Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()

def search(q, n=1):
    st, b = post({"comm": {"ct": "19", "cv": "1859", "uin": "0"},
        "req": {"method": "DoSearchForQQMusicDesktop", "module": "music.search.SearchCgiService",
                "param": {"num_per_page": str(n), "page_num": "1", "query": q, "search_type": 0}}})
    return json.loads(b.decode())["req"]["data"]["body"]["song"]["list"]

def lyric(mid, sid):
    st, b = post({"comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0",
                           "format": "json", "platform": "wk_v17"},
                  "req": {"module": "music.musichallSong.PlayLyricInfo",
                          "method": "GetPlayLyricInfo",
                          "param": {"songMID": mid, "songID": sid, "format": "qrc",
                                    "qrc": 1, "trans": 1, "roma": 1, "crypt": 1,
                                    "lrc_t": 0, "interval": 0}}})
    j = json.loads(b.decode())
    return st, j.get("code"), j["req"].get("code"), j["req"].get("data", {})

# --- rate limit: 12 rapid lyric calls
t0 = time.time(); codes = []
for i in range(12):
    st, c, rc, d = lyric("0039MnYb0qxYhV", 97773)
    codes.append((st, c, rc, len(d.get("lyric", ""))))
print("12x rapid lyric:", time.time()-t0, "s")
print("  ", codes)
print()

# --- search with empty query / bad query
for q in ("", "zzzzqqqqxxxx不存在的歌名12345", "晴天"):
    s = search(q, 3)
    print(f"search {q!r}: {len(s)} results", [(x['mid'], x['title']) for x in s[:3]])
print()

# --- no-lyrics candidates: 纯音乐 / 冷门
for q in ("叶惠美 纯音乐 伴奏", "Adele 12345 nonexistent"):
    s = search(q, 2)
    for x in s:
        st, c, rc, d = lyric(x["mid"], x["id"])
        print(f"  {x['mid']} {x['title']!r} -> http {st} code {c} reqcode {rc} lyriclen {len(d.get('lyric','') or '')} keys {list(d.keys())[:6]}")
print()

# --- GET form of the lyric endpoint too (some clients use GET)
try:
    import urllib.parse as up
    data = json.dumps({"comm": {"ct": 11, "cv": "1003006", "uin": "0"},
                       "req": {"module": "music.musichallSong.PlayLyricInfo",
                               "method": "GetPlayLyricInfo",
                               "param": {"songMID": "0039MnYb0qxYhV", "songID": 97773,
                                         "format": "qrc", "qrc": 1, "crypt": 1}}}, ensure_ascii=False)
    url = "https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data=" + up.quote(data)
    r = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(r, timeout=30) as resp:
        b = resp.read(); j = json.loads(b.decode())
        print("GET lyric ->", resp.status, len(b), "code", j.get("code"),
              "lyriclen", len(j["req"]["data"]["lyric"]))
except Exception as e:
    print("GET lyric ERR", type(e).__name__, e)
