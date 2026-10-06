# -*- coding: utf-8 -*-
import json, urllib.parse as up
from probe_qq import req, show

MID = "0039MnYb0qxYhV"
SID = 97773

# --- 1: legacy LRC (nobase64=1) ---
p = {
    "songmid": MID, "format": "json", "nobase64": "1", "g_tk": "5381", "loginUin": "0",
    "hostUin": "0", "inCharset": "utf8", "outCharset": "utf-8", "notice": "0",
    "platform": "yqq.json", "needNewCode": "0",
}
st, hd, body = req("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?" + up.urlencode(p))
show("1) legacy fcg_query_lyric_new.fcg nobase64=1", st, body, 900)

# --- 2: legacy LRC base64 ---
p2 = dict(p); p2.pop("nobase64")
st, hd, body = req("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?" + up.urlencode(p2))
show("2) legacy fcg_query_lyric_new.fcg (base64)", st, body, 600)

# --- 3: legacy QRC lyric_download.fcg ---
u3 = ("https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg"
      "?version=15&miniversion=82&lrctype=4&musicid=" + MID)
st, hd, body = req(u3)
show("3) legacy lyric_download.fcg lrctype=4 (QRC)", st, body, 900)

# --- 4: modern GetPlayLyricInfo via musicu.fcg ---
payload = {
    "comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0", "format": "json", "platform": "wk_v17"},
    "req": {
        "module": "music.musichallSong.PlayLyricInfo",
        "method": "GetPlayLyricInfo",
        "param": {
            "songMID": MID, "songID": SID, "format": "qrc", "qrc": 1,
            "trans": 1, "roma": 1, "crypt": 1, "lrc_t": 0, "interval": 0,
        },
    },
}
st, hd, body = req("https://u.y.qq.com/cgi-bin/musicu.fcg",
                   headers={"Content-Type": "application/json"},
                   data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
                   method="POST")
show("4) modern GetPlayLyricInfo format=qrc crypt=1", st, body, 1200)
try:
    j = json.loads(body.decode("utf-8"))
    d = j["req"]["data"]
    print("KEYS:", sorted(d.keys()))
    for k, v in d.items():
        if isinstance(v, str):
            print("  %s: len=%d head=%r" % (k, len(v), v[:80]))
        else:
            print("  %s: %r" % (k, v))
except Exception as e:
    print("PARSE FAIL:", e)
