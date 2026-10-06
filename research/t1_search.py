# -*- coding: utf-8 -*-
import json
from probe_qq import req, show

QUERY = "晴天 周杰伦"

# --- A: modern musicu.fcg search (POST) ---
payload = {
    "comm": {"ct": "19", "cv": "1859", "uin": "0"},
    "req": {
        "method": "DoSearchForQQMusicDesktop",
        "module": "music.search.SearchCgiService",
        "param": {"num_per_page": "3", "page_num": "1", "query": QUERY, "search_type": 0},
    },
}
st, hd, body = req("https://u.y.qq.com/cgi-bin/musicu.fcg",
                   headers={"Content-Type": "application/json"},
                   data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
                   method="POST")
show("A) musicu.fcg POST DoSearchForQQMusicDesktop", st, body, 800)
try:
    j = json.loads(body.decode("utf-8"))
    lst = j["req"]["data"]["body"]["song"]["list"]
    print("CODE:", j.get("code"), "reqCode:", j["req"].get("code"), "n_songs:", len(lst))
    for s in lst[:3]:
        print("  songmid=%s mid=%s title=%s singers=%s album=%s" % (
            s.get("mid"), s.get("id"), s.get("title"),
            "/".join(x.get("name", "") for x in s.get("singer", [])),
            s.get("album", {}).get("title")))
except Exception as e:
    print("PARSE FAIL:", e)
print()

# --- B: modern musicu.fcg search (GET with data= json) ---
q = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
url = "https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data=" + urllib_quote(q) if False else None
import urllib.parse
url = "https://u.y.qq.com/cgi-bin/musicu.fcg?format=json&data=" + urllib.parse.quote(q)
st, hd, body = req(url)
show("B) musicu.fcg GET data=", st, body, 400)
try:
    j = json.loads(body.decode("utf-8"))
    lst = j["req"]["data"]["body"]["song"]["list"]
    print("CODE:", j.get("code"), "n_songs:", len(lst))
    for s in lst[:3]:
        print("  songmid=%s title=%s singers=%s" % (
            s.get("mid"), s.get("title"),
            "/".join(x.get("name", "") for x in s.get("singer", []))))
except Exception as e:
    print("PARSE FAIL:", e)
print()

# --- C: legacy client_search_cp ---
import urllib.parse as up
params = {
    "ct": "24", "qqmusic_ver": "1298", "new_json": "1", "remoteplace": "txt.yqq.song",
    "searchid": "0", "t": "0", "aggr": "1", "cr": "1", "catZhida": "1", "lossless": "0",
    "flag_qc": "0", "p": "1", "n": "3", "w": QUERY, "g_tk": "5381", "loginUin": "0",
    "hostUin": "0", "format": "json", "inCharset": "utf8", "outCharset": "utf-8",
    "notice": "0", "platform": "yqq.json", "needNewCode": "0",
}
st, hd, body = req("https://c.y.qq.com/soso/fcgi-bin/client_search_cp?" + up.urlencode(params))
show("C) legacy client_search_cp", st, body, 600)
