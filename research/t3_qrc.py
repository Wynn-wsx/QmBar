# -*- coding: utf-8 -*-
import json, sys, zlib
from Crypto.Cipher import DES3, DES
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from probe_qq import req

MID = "0039MnYb0qxYhV"
SID = 97773

payload = {
    "comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0", "format": "json", "platform": "wk_v17"},
    "req": {
        "module": "music.musichallSong.PlayLyricInfo",
        "method": "GetPlayLyricInfo",
        "param": {"songMID": MID, "songID": SID, "format": "qrc", "qrc": 1,
                  "trans": 1, "roma": 1, "crypt": 1, "lrc_t": 0, "interval": 0},
    },
}
st, hd, body = req("https://u.y.qq.com/cgi-bin/musicu.fcg",
                   headers={"Content-Type": "application/json"},
                   data=json.dumps(payload, ensure_ascii=False).encode("utf-8"), method="POST")
j = json.loads(body.decode("utf-8"))
d = j["req"]["data"]
hexlyric = d["lyric"]
print("http", st, "hexlen", len(hexlyric))
ct = bytes.fromhex(hexlyric)
print("ciphertext bytes:", len(ct), "mod8:", len(ct) % 8)

QQKey = b"!@#)(*$%123ZXC!@!@#)(NHL"
print("keylen", len(QQKey))

# attempt 1: pycryptodome TripleDES ECB (3-key)
try:
    c = DES3.new(QQKey, DES3.MODE_ECB)
    pt = c.decrypt(ct)
    print("3DES-ECB first32:", pt[:32])
    try:
        out = zlib.decompress(pt)
        print("ZLIB OK len", len(out))
        print("DECRYPTED HEAD:")
        print(out.decode("utf-8", "replace")[:700])
    except Exception as e:
        print("zlib fail:", e)
except Exception as e:
    print("DES3 fail:", e)

# attempt 2: plain DES ECB with first 8 bytes (in case single-DES)
try:
    c = DES.new(QQKey[:8], DES.MODE_ECB)
    pt = c.decrypt(ct)
    print("DES-ECB(k0) first16:", pt[:16])
except Exception as e:
    print("DES fail:", e)
