# -*- coding: utf-8 -*-
"""Faithful Python port of Lyricify.Lyrics.Helper/Decrypter/Qrc/DESHelper.cs (QQMusic custom DES/3DES).

Upstream: https://github.com/WXRIW/Lyricify-Lyrics-Helper  (Apache-2.0, Copyright (c) WXRIW)
This file is a re-implementation (Python port) of that algorithm, kept here as the
reference implementation used to validate src/QqDes.cs byte-for-byte.
"""

ENCRYPT, DECRYPT = 1, 0
M32 = 0xFFFFFFFF

SBOX = [
 [14,4,13,1,2,15,11,8,3,10,6,12,5,9,0,7, 0,15,7,4,14,2,13,1,10,6,12,11,9,5,3,8,
  4,1,14,8,13,6,2,11,15,12,9,7,3,10,5,0, 15,12,8,2,4,9,1,7,5,11,3,14,10,0,6,13],
 [15,1,8,14,6,11,3,4,9,7,2,13,12,0,5,10, 3,13,4,7,15,2,8,15,12,0,1,10,6,9,11,5,
  0,14,7,11,10,4,13,1,5,8,12,6,9,3,2,15, 13,8,10,1,3,15,4,2,11,6,7,12,0,5,14,9],
 [10,0,9,14,6,3,15,5,1,13,12,7,11,4,2,8, 13,7,0,9,3,4,6,10,2,8,5,14,12,11,15,1,
  13,6,4,9,8,15,3,0,11,1,2,12,5,10,14,7, 1,10,13,0,6,9,8,7,4,15,14,3,11,5,2,12],
 [7,13,14,3,0,6,9,10,1,2,8,5,11,12,4,15, 13,8,11,5,6,15,0,3,4,7,2,12,1,10,14,9,
  10,6,9,0,12,11,7,13,15,1,3,14,5,2,8,4, 3,15,0,6,10,10,13,8,9,4,5,11,12,7,2,14],
 [2,12,4,1,7,10,11,6,8,5,3,15,13,0,14,9, 14,11,2,12,4,7,13,1,5,0,15,10,3,9,8,6,
  4,2,1,11,10,13,7,8,15,9,12,5,6,3,0,14, 11,8,12,7,1,14,2,13,6,15,0,9,10,4,5,3],
 [12,1,10,15,9,2,6,8,0,13,3,4,14,7,5,11, 10,15,4,2,7,12,9,5,6,1,13,14,0,11,3,8,
  9,14,15,5,2,8,12,3,7,0,4,10,1,13,11,6, 4,3,2,12,9,5,15,10,11,14,1,7,6,0,8,13],
 [4,11,2,14,15,0,8,13,3,12,9,7,5,10,6,1, 13,0,11,7,4,9,1,10,14,3,5,12,2,15,8,6,
  1,4,11,13,12,3,7,14,10,15,6,8,0,5,9,2, 6,11,13,8,1,4,10,7,9,5,0,15,14,2,3,12],
 [13,2,8,4,6,15,11,1,10,9,3,14,5,0,12,7, 1,15,13,8,10,3,7,4,12,5,6,11,0,14,9,2,
  7,11,4,1,9,12,14,2,0,6,10,13,15,3,5,8, 2,1,14,7,4,10,8,13,15,12,9,0,3,5,6,11],
]

def BITNUM(a, b, c):
    return ((a[b // 32 * 4 + 3 - (b % 32) // 8] >> (7 - (b % 8))) & 0x01) << c

def BITNUMINTR(a, b, c):
    return (((a >> (31 - b)) & 1) << c)

def BITNUMINTL(a, b, c):
    return ((((a << b) & 0x80000000) & M32) >> c)

def SBOXBIT(a):
    return (a & 0x20) | ((a & 0x1f) >> 1) | ((a & 0x01) << 4)

KEY_RND_SHIFT = [1,1,2,2,2,2,2,2,1,2,2,2,2,2,2,1]
KEY_PERM_C = [56,48,40,32,24,16,8,0,57,49,41,33,25,17,9,1,58,50,42,34,26,18,10,2,59,51,43,35]
KEY_PERM_D = [62,54,46,38,30,22,14,6,61,53,45,37,29,21,13,5,60,52,44,36,28,20,12,4,27,19,11,3]
KEY_COMPRESSION = [13,16,10,23,0,4,2,27,14,5,20,9,22,18,11,3,25,7,15,6,26,19,12,1,
                   40,51,30,36,46,54,29,39,50,44,32,47,43,48,38,55,33,52,45,41,49,35,28,31]

def key_schedule(key, mode):
    schedule = [[0]*6 for _ in range(16)]
    C = D = 0
    j = 31
    for i in range(28):
        C |= BITNUM(key, KEY_PERM_C[i], j); j -= 1
    j = 31
    for i in range(28):
        D |= BITNUM(key, KEY_PERM_D[i], j); j -= 1
    C &= M32; D &= M32
    for i in range(16):
        s = KEY_RND_SHIFT[i]
        C = ((C << s) | (C >> (28 - s))) & 0xfffffff0
        D = ((D << s) | (D >> (28 - s))) & 0xfffffff0
        toGen = (15 - i) if mode == DECRYPT else i
        schedule[toGen] = [0]*6
        for j in range(24):
            schedule[toGen][j // 8] |= BITNUMINTR(C, KEY_COMPRESSION[j], 7 - (j % 8))
        for j in range(24, 48):
            schedule[toGen][j // 8] |= BITNUMINTR(D, KEY_COMPRESSION[j] - 27, 7 - (j % 8))
        schedule[toGen] = [x & 0xFF for x in schedule[toGen]]
    return schedule

IP_T0 = [57,49,41,33,25,17,9,1,59,51,43,35,27,19,11,3,61,53,45,37,29,21,13,5,63,55,47,39,31,23,15,7]
IP_T1 = [56,48,40,32,24,16,8,0,58,50,42,34,26,18,10,2,60,52,44,36,28,20,12,4,62,54,46,38,30,22,14,6]

def IP(inp):
    st = [0, 0]
    for k, bits in ((0, IP_T0), (1, IP_T1)):
        v = 0
        for idx, bitpos in enumerate(bits):
            v |= BITNUM(inp, bitpos, 31 - idx)
        st[k] = v & M32
    return st

def _invip_byte(state, m):
    return (BITNUMINTR(state[1], m, 7) | BITNUMINTR(state[0], m, 6) |
            BITNUMINTR(state[1], m + 8, 5) | BITNUMINTR(state[0], m + 8, 4) |
            BITNUMINTR(state[1], m + 16, 3) | BITNUMINTR(state[0], m + 16, 2) |
            BITNUMINTR(state[1], m + 24, 1) | BITNUMINTR(state[0], m + 24, 0))

def InvIP2(state):
    out = bytearray(8)
    out[3] = _invip_byte(state, 7)
    out[2] = _invip_byte(state, 6)
    out[1] = _invip_byte(state, 5)
    out[0] = _invip_byte(state, 4)
    out[7] = _invip_byte(state, 3)
    out[6] = _invip_byte(state, 2)
    out[5] = _invip_byte(state, 1)
    out[4] = _invip_byte(state, 0)
    return out

def F(state, key):
    t1 = (BITNUMINTL(state, 31, 0) | ((state & 0xf0000000) >> 1) | BITNUMINTL(state, 4, 5) |
          BITNUMINTL(state, 3, 6) | ((state & 0x0f000000) >> 3) | BITNUMINTL(state, 8, 11) |
          BITNUMINTL(state, 7, 12) | ((state & 0x00f00000) >> 5) | BITNUMINTL(state, 12, 17) |
          BITNUMINTL(state, 11, 18) | ((state & 0x000f0000) >> 7) | BITNUMINTL(state, 16, 23))
    t2 = (BITNUMINTL(state, 15, 0) | ((state & 0x0000f000) << 15) | BITNUMINTL(state, 20, 5) |
          BITNUMINTL(state, 19, 6) | ((state & 0x00000f00) << 13) | BITNUMINTL(state, 24, 11) |
          BITNUMINTL(state, 23, 12) | ((state & 0x000000f0) << 11) | BITNUMINTL(state, 28, 17) |
          BITNUMINTL(state, 27, 18) | ((state & 0x0000000f) << 9) | BITNUMINTL(state, 0, 23))
    t1 &= M32; t2 &= M32
    ls = [(t1 >> 24) & 0xFF, (t1 >> 16) & 0xFF, (t1 >> 8) & 0xFF,
          (t2 >> 24) & 0xFF, (t2 >> 16) & 0xFF, (t2 >> 8) & 0xFF]
    for i in range(6):
        ls[i] ^= key[i]
    state = (((SBOX[0][SBOXBIT(ls[0] >> 2)] << 28) |
              (SBOX[1][SBOXBIT(((ls[0] & 0x03) << 4) | (ls[1] >> 4))] << 24) |
              (SBOX[2][SBOXBIT(((ls[1] & 0x0f) << 2) | (ls[2] >> 6))] << 20) |
              (SBOX[3][SBOXBIT(ls[2] & 0x3f)] << 16) |
              (SBOX[4][SBOXBIT(ls[3] >> 2)] << 12) |
              (SBOX[5][SBOXBIT(((ls[3] & 0x03) << 4) | (ls[4] >> 4))] << 8) |
              (SBOX[6][SBOXBIT(((ls[4] & 0x0f) << 2) | (ls[5] >> 6))] << 4) |
              SBOX[7][SBOXBIT(ls[5] & 0x3f)])) & M32
    P = [15,6,19,20,28,11,27,16,0,14,22,25,4,17,30,9,1,7,23,13,31,26,2,8,18,12,29,5,21,10,3,24]
    out = 0
    for idx, b in enumerate(P):
        out |= BITNUMINTL(state, b, idx)
    return out & M32

def Crypt(inp, sched):
    state = IP(inp)
    for idx in range(15):
        t = state[1]
        state[1] = F(state[1], sched[idx]) ^ state[0]
        state[0] = t
    state[0] = F(state[1], sched[15]) ^ state[0]
    return bytes(InvIP2(state))

def triple_des_key_setup(key, mode):
    sched = [None, None, None]
    if mode == ENCRYPT:
        sched[0] = key_schedule(key[0:8], mode)
        sched[1] = key_schedule(key[8:16], DECRYPT)
        sched[2] = key_schedule(key[16:24], mode)
    else:
        sched[2] = key_schedule(key[0:8], mode)
        sched[1] = key_schedule(key[8:16], ENCRYPT)
        sched[0] = key_schedule(key[16:24], mode)
    return sched

def decrypt_3des(data, key):
    sched = triple_des_key_setup(key, DECRYPT)
    out = bytearray()
    for i in range(0, len(data), 8):
        b = data[i:i+8]
        b = Crypt(b, sched[0])
        b = Crypt(b, sched[1])
        b = Crypt(b, sched[2])
        out += b
    return bytes(out)

def decrypt_qrc(hex_or_bytes, key=b"!@#)(*$%123ZXC!@!@#)(NHL"):
    import zlib
    ct = bytes.fromhex(hex_or_bytes) if isinstance(hex_or_bytes, str) else hex_or_bytes
    pt = decrypt_3des(ct, key)
    out = zlib.decompress(pt)
    if out[:3] == b'\xef\xbb\xbf':
        out = out[3:]
    return out.decode("utf-8")
