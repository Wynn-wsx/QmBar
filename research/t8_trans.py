# -*- coding: utf-8 -*-
import json, sys, urllib.request
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from qqmusic_des import decrypt_qrc

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0.0 Safari/537.36"

def musicu(payload):
    r = urllib.request.Request("https://u.y.qq.com/cgi-bin/musicu.fcg",
        data=json.dumps(payload, ensure_ascii=False).encode(),
        headers={"User-Agent": UA, "Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(r, timeout=30) as resp:
        return resp.read()

def lyric(mid, sid, fmt="qrc", trans=1, roma=1):
    b = musicu({"comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0",
                         "format": "json", "platform": "wk_v17"},
                "req": {"module": "music.musichallSong.PlayLyricInfo",
                        "method": "GetPlayLyricInfo",
                        "param": {"songMID": mid, "songID": sid, "format": fmt,
                                  "qrc": 1, "trans": trans, "roma": roma,
                                  "crypt": 1, "lrc_t": 0, "interval": 0}}})
    return json.loads(b.decode())["req"]["data"]

h = lambda s: s if isinstance(s, str) else s.decode("utf-8", "replace")

d = lyric("0041gObR2QG98x", 200369396)
for k in ("lyric", "trans", "roma"):
    v = d.get(k, "")
    if not v:
        print(k, "-> EMPTY"); continue
    try:
        t = h(decrypt_qrc(v))
        print(f"{k} -> OK {len(t)} chars"); print("   ", repr(t[:300]))
    except Exception as e:
        print(k, "-> FAIL", type(e).__name__, e)
print("transSource:", d.get("transSource"), "hasTransContributor:", d.get("hasTransContributor"))
print()

# 中文歌 + 英文翻译 (e.g. 起风了)
def search(q, n=1):
    b = musicu({"comm": {"ct": "19", "cv": "1859", "uin": "0"},
        "req": {"method": "DoSearchForQQMusicDesktop", "module": "music.search.SearchCgiService",
                "param": {"num_per_page": str(n), "page_num": "1", "query": q, "search_type": 0}}})
    return json.loads(b.decode())["req"]["data"]["body"]["song"]["list"]

for q in ("起风了 买辣椒也用券", "Hello Adele"):
    s = search(q, 1)[0]
    d = lyric(s["mid"], s["id"])
    tr = d.get("trans", "") or ""
    print(q, s["mid"], s["id"], "lyric", len(d["lyric"]), "trans", len(tr), "roma", len(d.get("roma","") or ""))
    if tr:
        try: print("   trans head:", repr(h(decrypt_qrc(tr))[:220]))
        except Exception as e: print("   trans FAIL", e)

# format=lrc on modern endpoint
d = lyric("0039MnYb0qxYhV", 97773, fmt="lrc", trans=0, roma=0)
try:
    t = h(decrypt_qrc(d["lyric"]))
    print("\nmodern format=lrc decrypted head:", repr(t[:220]))
except Exception as e:
    print("\nformat=lrc decrypt FAIL", e)
