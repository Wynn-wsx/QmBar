# -*- coding: utf-8 -*-
import json, sys, urllib.parse as up
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from probe_qq import req, show

MID, SID = "0039MnYb0qxYhV", 97773

def musicu(payload, referer=True):
    h = {"Content-Type": "application/json"}
    if not referer:
        h["Referer"] = ""
    import urllib.request
    hh = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0.0.0 Safari/537.36",
          "Content-Type": "application/json"}
    if referer:
        hh["Referer"] = "https://y.qq.com/"
    r = urllib.request.Request("https://u.y.qq.com/cgi-bin/musicu.fcg",
                               data=json.dumps(payload, ensure_ascii=False).encode(),
                               headers=hh, method="POST")
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()
    except Exception as e:
        return None, str(e).encode()

def lyric_req(mid, sid, fmt="qrc", crypt=1, **extra):
    p = {"songMID": mid, "songID": sid, "format": fmt, "qrc": 1, "trans": 1, "roma": 1,
         "crypt": crypt, "lrc_t": 0, "interval": 0}
    p.update(extra)
    return musicu({"comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0",
                            "format": "json", "platform": "wk_v17"},
                   "req": {"module": "music.musichallSong.PlayLyricInfo",
                           "method": "GetPlayLyricInfo", "param": p}})

# --- D: format=lrc via modern endpoint, crypt=1
st, body = lyric_req(MID, SID, fmt="lrc")
j = json.loads(body.decode())
d = j["req"]["data"]
print("D) modern format=lrc crypt=1 -> http", st, "code", j.get("code"), "lyric_len", len(d["lyric"]), "hexhead", d["lyric"][:48])
print()

# --- E: crypt=0 (no encryption?)
st, body = lyric_req(MID, SID, fmt="qrc", crypt=0)
j = json.loads(body.decode()); d = j["req"]["data"]
print("E) modern format=qrc crypt=0 -> http", st, "code", j.get("code"), "lyric_len", len(d["lyric"]))
print("   lyric head:", repr(d["lyric"][:300]))
print()

# --- F: no Referer header
st, body = lyric_req(MID, SID, fmt="qrc", crypt=1)
print("F1) with Referer ->", st, len(body))
import urllib.request, urllib.error
payload = {"comm": {"ct": 11, "cv": "1003006", "uin": "0"}, "req": {"module": "music.musichallSong.PlayLyricInfo",
    "method": "GetPlayLyricInfo", "param": {"songMID": MID, "songID": SID, "format": "qrc", "qrc": 1, "crypt": 1}}}
r = urllib.request.Request("https://u.y.qq.com/cgi-bin/musicu.fcg",
                           data=json.dumps(payload).encode(),
                           headers={"User-Agent": "python-urllib/3.14", "Content-Type": "application/json"},
                           method="POST")
try:
    with urllib.request.urlopen(r, timeout=30) as resp:
        b2 = resp.read(); print("F2) NO Referer, urllib UA ->", resp.status, len(b2), b2[:120])
except urllib.error.HTTPError as e:
    print("F2) NO Referer -> HTTP", e.code, e.read()[:200])
except Exception as e:
    print("F2) NO Referer -> ERR", e)
print()

# --- G: song with translation (English, likely has Chinese trans)
def search(q, n=5):
    p = {"comm": {"ct": "19", "cv": "1859", "uin": "0"},
         "req": {"method": "DoSearchForQQMusicDesktop", "module": "music.search.SearchCgiService",
                 "param": {"num_per_page": str(n), "page_num": "1", "query": q, "search_type": 0}}}
    st, b = musicu(p)
    j = json.loads(b.decode())
    return j["req"]["data"]["body"]["song"]["list"]

for s in search("Shape of You Ed Sheeran", 3):
    print("  cand:", s["mid"], s["id"], s["title"], "/".join(x["name"] for x in s["singer"]))
    st, body = lyric_req(s["mid"], s["id"], fmt="qrc", crypt=1)
    j = json.loads(body.decode()); d = j["req"]["data"]
    print("   -> lyric_len", len(d["lyric"]), "trans_len", len(d.get("trans", "")), "roma_len", len(d.get("roma", "")))
    break
print()

# --- H: legacy LRC without Referer
p = {"songmid": MID, "format": "json", "nobase64": "1"}
st, hd, body = req("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?" + up.urlencode(p))
print("H1) legacy LRC WITH Referer ->", st, len(body), body[:120])
r = urllib.request.Request("https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?" + up.urlencode(p),
                           headers={"User-Agent": "python-urllib/3.14"})
try:
    with urllib.request.urlopen(r, timeout=30) as resp:
        b3 = resp.read(); print("H2) legacy LRC NO Referer ->", resp.status, len(b3), b3[:200])
except urllib.error.HTTPError as e:
    print("H2) legacy LRC NO Referer -> HTTP", e.code, e.read()[:200])
except Exception as e:
    print("H2) NO Referer ERR", e)
