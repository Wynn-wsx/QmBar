# -*- coding: utf-8 -*-
import json, sys, urllib.request
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from qqmusic_des import decrypt_qrc
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0.0 Safari/537.36"
h = lambda s: s if isinstance(s, str) else s.decode("utf-8", "replace")

def post(p):
    r = urllib.request.Request("https://u.y.qq.com/cgi-bin/musicu.fcg",
        data=json.dumps(p, ensure_ascii=False).encode(),
        headers={"User-Agent": UA, "Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(r, timeout=30) as resp:
        return json.loads(resp.read().decode())

def get_lyric(mid, sid):
    j = post({"comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0",
                       "format": "json", "platform": "wk_v17"},
              "req": {"module": "music.musichallSong.PlayLyricInfo",
                      "method": "GetPlayLyricInfo",
                      "param": {"songMID": mid, "songID": sid, "format": "qrc",
                                "qrc": 1, "trans": 1, "roma": 1, "crypt": 1,
                                "lrc_t": 0, "interval": 0}}})
    return j["req"].get("code"), j["req"].get("data", {})

# short one: find its numeric id from search
j = post({"comm": {"ct": "19", "cv": "1859", "uin": "0"},
          "req": {"method": "DoSearchForQQMusicDesktop", "module": "music.search.SearchCgiService",
                  "param": {"num_per_page": "5", "page_num": "1", "query": "记忆的鱼缸", "search_type": 0}}})
lst = j["req"]["data"]["body"]["song"]["list"]
for s in lst:
    rc, d = get_lyric(s["mid"], s["id"])
    L = d.get("lyric", "") or ""
    print(f"{s['mid']} id={s['id']} {s['title'][:20]!r} reqcode={rc} lyriclen={len(L)}")
    if L:
        try:
            print("   PLAIN:", repr(h(decrypt_qrc(L))[:300]))
        except Exception as e:
            print("   decrypt FAIL", e)
    else:
        print("   raw lyric field:", repr(L))
print()
for mid, sid in [("0039MnYb0qxYhV", 97773)]:
    rc, d = get_lyric(mid, sid)
    print("晴天 reqcode", rc, "lyriclen", len(d.get("lyric", "")))
