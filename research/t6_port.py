# -*- coding: utf-8 -*-
import sys, zlib
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
import qqmusic_des as D

QQKey = b"!@#)(*$%123ZXC!@!@#)(NHL"
print("key len", len(QQKey), QQKey)

ct = open("qrc_cipher.bin", "rb").read()
pt = D.decrypt_3des(ct, QQKey)
print("first16:", pt[:16].hex())
try:
    out = zlib.decompress(pt)
    print("ZLIB OK", len(out))
    txt = out.decode("utf-8", "replace").lstrip("\ufeff")
    print(txt[:1200])
    open("qrc_plain.txt", "w", encoding="utf-8").write(txt)
except Exception as e:
    print("zlib fail:", e)
