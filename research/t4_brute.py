# -*- coding: utf-8 -*-
"""Brute-force the byte/bit ordering of QQMusic QRC 'TripleDES' against a zlib-header oracle."""
import json, sys, zlib
from Crypto.Cipher import DES3, DES
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
from probe_qq import req

MID, SID = "0039MnYb0qxYhV", 97773
payload = {
    "comm": {"ct": 11, "cv": "1003006", "v": "1003006", "uin": "0", "format": "json", "platform": "wk_v17"},
    "req": {"module": "music.musichallSong.PlayLyricInfo", "method": "GetPlayLyricInfo",
            "param": {"songMID": MID, "songID": SID, "format": "qrc", "qrc": 1,
                      "trans": 1, "roma": 1, "crypt": 1, "lrc_t": 0, "interval": 0}},
}
st, hd, body = req("https://u.y.qq.com/cgi-bin/musicu.fcg",
                   headers={"Content-Type": "application/json"},
                   data=json.dumps(payload, ensure_ascii=False).encode("utf-8"), method="POST")
ct = bytes.fromhex(json.loads(body.decode("utf-8"))["req"]["data"]["lyric"])
QQKey = b"!@#)(*$%123ZXC!@!@#)(NHL"
open("qrc_cipher.bin", "wb").write(ct)
print("cipher bytes", len(ct))

REVBITS = bytes(int('{:08b}'.format(b)[::-1], 2) for b in range(256))

def swap_dwords(d):
    return b"".join(d[i+3:i+4] + d[i+2:i+3] + d[i+1:i+2] + d[i:i+1] for i in range(0, len(d), 4))

def swap_blocks(d):
    return b"".join(d[i:i+8][::-1] for i in range(0, len(d), 8))

def bitrev(d):
    return d.translate(REVBITS)

data_variants = {
    "raw": ct,
    "dwordswap": swap_dwords(ct),
    "blockrev": swap_blocks(ct),
    "bitrev": bitrev(ct),
    "bitrev+blockrev": swap_blocks(bitrev(ct)),
}

key_variants = {
    "QQKey": QQKey,
    "QQKey_blockrev": swap_blocks(QQKey),
    "QQKey_dwordswap": swap_dwords(QQKey),
    "QQKey_bitrev": bitrev(QQKey),
}

def try_inflate(b):
    for wbits in (15, -15, 47):
        try:
            out = zlib.decompressobj(wbits).decompress(b)
            if out and len(out) > 200:
                return out
        except Exception:
            pass
    return None

hits = []
for kn, k in key_variants.items():
    try:
        c = DES3.new(k, DES3.MODE_ECB)
    except Exception as e:
        print("key reject", kn, e); continue
    for dn, d in data_variants.items():
        pt = c.decrypt(d)
        for postn, post in (("id", lambda x: x), ("blockrev", swap_blocks), ("bitrev", bitrev)):
            cand = post(pt)
            if cand[:2] in (b"\x78\x9c", b"\x78\x01", b"\x78\xda", b"\x78\x5e"):
                hits.append((kn, dn, postn, cand[:8].hex()))
                out = try_inflate(cand)
                if out:
                    print("*** HIT", kn, dn, postn, "inflated", len(out))
                    open("qrc_out.txt", "wb").write(out)
                    print(out.decode("utf-8", "replace")[:400])
                    sys.exit(0)
print("no hits; zlib-header candidates:", hits)
print("first bytes sample:", DES3.new(QQKey, DES3.MODE_ECB).decrypt(ct)[:16].hex())
print("dwordswap sample:", DES3.new(QQKey, DES3.MODE_ECB).decrypt(swap_dwords(ct))[:16].hex())
print("blockrev sample :", DES3.new(QQKey, DES3.MODE_ECB).decrypt(swap_blocks(ct))[:16].hex())
print("bitrev sample   :", DES3.new(QQKey, DES3.MODE_ECB).decrypt(bitrev(ct))[:16].hex())
