# -*- coding: utf-8 -*-
"""Verify: QQMusic custom DES == standard DES with 32-bit-word byte swap (S . D . S)."""
import sys, zlib
from Crypto.Cipher import DES3
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ct = open("qrc_cipher.bin", "rb").read()
QQKey = b"!@#)(*$%123ZXC!@!@#)(NHL"

def S(d):  # swap byte order inside each 4-byte group
    return b"".join(d[i+3:i+4] + d[i+2:i+3] + d[i+1:i+2] + d[i:i+1] for i in range(0, len(d), 4))

def inflate_ok(b):
    for wbits in (15, -15):
        try:
            out = zlib.decompressobj(wbits).decompress(b)
            if out and len(out) > 100:
                return out
        except Exception:
            pass
    return None

tests = {
    "S.D.S dup":      lambda c, k: S(DES3.new(S(k), DES3.MODE_ECB).decrypt(S(c))),
    "S.D only c":     lambda c, k: DES3.new(k, DES3.MODE_ECB).decrypt(S(c)),
    "S.D.S(key unsw)":lambda c, k: S(DES3.new(k, DES3.MODE_ECB).decrypt(S(c))),
    "D.S.S":          lambda c, k: DES3.new(S(k), DES3.MODE_ECB).decrypt(S(S(c))),
}
for name, fn in tests.items():
    try:
        pt = fn(ct, QQKey)
    except Exception as e:
        print(name, "err", e); continue
    print("%-18s head=%s" % (name, pt[:16].hex()))
    out = inflate_ok(pt)
    if out:
        print("  *** ZLIB OK len", len(out))
        print(out.decode("utf-8", "replace")[:600])
        open("qrc_out.txt", "wb").write(out)
        sys.exit(0)
print("none worked")
