using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace QmBar
{
    /// <summary>
    /// QQ音乐 QRC 歌词解密：自定义 TripleDES（**不是**标准 DES）+ zlib。
    /// 逐行移植自本机已对 20+ 首真实响应验证过的 Python 参考实现 qqmusic_des.py，
    /// 其来源为 WXRIW/Lyricify-Lyrics-Helper 的 Decrypter/Qrc/DESHelper.cs。
    ///
    /// 上游：https://github.com/WXRIW/Lyricify-Lyrics-Helper （Apache-2.0，Copyright (c) WXRIW）
    /// 本文件是对上游算法的**再实现**（改写为 C# 2/5 语法并做适配），依 Apache-2.0 第 4 条
    /// 保留来源声明。上游原文件未随本仓库分发，需要对照请直接访问上面的仓库。
    ///
    /// ⚠ 下面 SBOX 等表与标准 DES 存在**刻意差异**（例如 SBOX[1] 行1 的 "8,15"、
    /// SBOX[3] 行3 的 "10,10" 都不是标准值）。必须原样保留，看到就"顺手修正"会让解密整体失效。
    /// </summary>
    internal static class QqDes
    {
        private const int ENCRYPT = 1;
        private const int DECRYPT = 0;
        private const uint M32 = 0xFFFFFFFFu;

        public static readonly byte[] QqKey = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");

        private static readonly byte[][] SBOX = new byte[][]
        {
            new byte[]{
                14,4,13,1,2,15,11,8,3,10,6,12,5,9,0,7, 0,15,7,4,14,2,13,1,10,6,12,11,9,5,3,8,
                4,1,14,8,13,6,2,11,15,12,9,7,3,10,5,0, 15,12,8,2,4,9,1,7,5,11,3,14,10,0,6,13},
            new byte[]{
                15,1,8,14,6,11,3,4,9,7,2,13,12,0,5,10, 3,13,4,7,15,2,8,15,12,0,1,10,6,9,11,5,
                0,14,7,11,10,4,13,1,5,8,12,6,9,3,2,15, 13,8,10,1,3,15,4,2,11,6,7,12,0,5,14,9},
            new byte[]{
                10,0,9,14,6,3,15,5,1,13,12,7,11,4,2,8, 13,7,0,9,3,4,6,10,2,8,5,14,12,11,15,1,
                13,6,4,9,8,15,3,0,11,1,2,12,5,10,14,7, 1,10,13,0,6,9,8,7,4,15,14,3,11,5,2,12},
            new byte[]{
                7,13,14,3,0,6,9,10,1,2,8,5,11,12,4,15, 13,8,11,5,6,15,0,3,4,7,2,12,1,10,14,9,
                10,6,9,0,12,11,7,13,15,1,3,14,5,2,8,4, 3,15,0,6,10,10,13,8,9,4,5,11,12,7,2,14},
            new byte[]{
                2,12,4,1,7,10,11,6,8,5,3,15,13,0,14,9, 14,11,2,12,4,7,13,1,5,0,15,10,3,9,8,6,
                4,2,1,11,10,13,7,8,15,9,12,5,6,3,0,14, 11,8,12,7,1,14,2,13,6,15,0,9,10,4,5,3},
            new byte[]{
                12,1,10,15,9,2,6,8,0,13,3,4,14,7,5,11, 10,15,4,2,7,12,9,5,6,1,13,14,0,11,3,8,
                9,14,15,5,2,8,12,3,7,0,4,10,1,13,11,6, 4,3,2,12,9,5,15,10,11,14,1,7,6,0,8,13},
            new byte[]{
                4,11,2,14,15,0,8,13,3,12,9,7,5,10,6,1, 13,0,11,7,4,9,1,10,14,3,5,12,2,15,8,6,
                1,4,11,13,12,3,7,14,10,15,6,8,0,5,9,2, 6,11,13,8,1,4,10,7,9,5,0,15,14,2,3,12},
            new byte[]{
                13,2,8,4,6,15,11,1,10,9,3,14,5,0,12,7, 1,15,13,8,10,3,7,4,12,5,6,11,0,14,9,2,
                7,11,4,1,9,12,14,2,0,6,10,13,15,3,5,8, 2,1,14,7,4,10,8,13,15,12,9,0,3,5,6,11},
        };

        private static readonly int[] KEY_RND_SHIFT = new int[] { 1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1 };

        private static readonly int[] KEY_PERM_C = new int[] {
            56,48,40,32,24,16,8,0,57,49,41,33,25,17,9,1,58,50,42,34,26,18,10,2,59,51,43,35 };

        private static readonly int[] KEY_PERM_D = new int[] {
            62,54,46,38,30,22,14,6,61,53,45,37,29,21,13,5,60,52,44,36,28,20,12,4,27,19,11,3 };

        private static readonly int[] KEY_COMPRESSION = new int[] {
            13,16,10,23,0,4,2,27,14,5,20,9,22,18,11,3,25,7,15,6,26,19,12,1,
            40,51,30,36,46,54,29,39,50,44,32,47,43,48,38,55,33,52,45,41,49,35,28,31 };

        private static readonly int[] IP_T0 = new int[] {
            57,49,41,33,25,17,9,1,59,51,43,35,27,19,11,3,61,53,45,37,29,21,13,5,63,55,47,39,31,23,15,7 };

        private static readonly int[] IP_T1 = new int[] {
            56,48,40,32,24,16,8,0,58,50,42,34,26,18,10,2,60,52,44,36,28,20,12,4,62,54,46,38,30,22,14,6 };

        private static readonly int[] P = new int[] {
            15,6,19,20,28,11,27,16,0,14,22,25,4,17,30,9,1,7,23,13,31,26,2,8,18,12,29,5,21,10,3,24 };

        // ---------------- 位运算原语 ----------------

        private static uint BITNUM(byte[] a, int b, int c)
        {
            return (uint)(((a[b / 32 * 4 + 3 - (b % 32) / 8] >> (7 - (b % 8))) & 0x01) << c);
        }

        private static uint BITNUMINTR(uint a, int b, int c)
        {
            return ((a >> (31 - b)) & 1u) << c;
        }

        private static uint BITNUMINTL(uint a, int b, int c)
        {
            return (((a << b) & 0x80000000u) & M32) >> c;
        }

        private static int SBOXBIT(int a)
        {
            return (a & 0x20) | ((a & 0x1f) >> 1) | ((a & 0x01) << 4);
        }

        private static byte[] Slice8(byte[] key, int off)
        {
            byte[] r = new byte[8];
            for (int i = 0; i < 8; i++) r[i] = key[off + i];
            return r;
        }

        // ---------------- 密钥编排 ----------------

        private static byte[][] KeySchedule(byte[] key, int mode)
        {
            byte[][] schedule = new byte[16][];
            for (int i = 0; i < 16; i++) schedule[i] = new byte[6];

            uint C = 0, D = 0;
            int j = 31;
            for (int i = 0; i < 28; i++) { C |= BITNUM(key, KEY_PERM_C[i], j); j--; }
            j = 31;
            for (int i = 0; i < 28; i++) { D |= BITNUM(key, KEY_PERM_D[i], j); j--; }
            C &= M32; D &= M32;

            for (int i = 0; i < 16; i++)
            {
                int s = KEY_RND_SHIFT[i];
                C = ((C << s) | (C >> (28 - s))) & 0xfffffff0u;
                D = ((D << s) | (D >> (28 - s))) & 0xfffffff0u;

                int toGen = (mode == DECRYPT) ? (15 - i) : i;
                for (int k = 0; k < 24; k++)
                    schedule[toGen][k / 8] |= (byte)BITNUMINTR(C, KEY_COMPRESSION[k], 7 - (k % 8));
                for (int k = 24; k < 48; k++)
                    schedule[toGen][k / 8] |= (byte)BITNUMINTR(D, KEY_COMPRESSION[k] - 27, 7 - (k % 8));
            }
            return schedule;
        }

        private static byte[][][] TripleDesKeySetup(byte[] key, int mode)
        {
            byte[][][] sched = new byte[3][][];
            if (mode == ENCRYPT)
            {
                sched[0] = KeySchedule(Slice8(key, 0), mode);
                sched[1] = KeySchedule(Slice8(key, 8), DECRYPT);
                sched[2] = KeySchedule(Slice8(key, 16), mode);
            }
            else
            {
                sched[2] = KeySchedule(Slice8(key, 0), mode);
                sched[1] = KeySchedule(Slice8(key, 8), ENCRYPT);
                sched[0] = KeySchedule(Slice8(key, 16), mode);
            }
            return sched;
        }

        // ---------------- 分组运算 ----------------

        private static uint[] IP(byte[] inp)
        {
            uint[] st = new uint[2];
            for (int k = 0; k < 2; k++)
            {
                int[] bits = (k == 0) ? IP_T0 : IP_T1;
                uint v = 0;
                for (int idx = 0; idx < bits.Length; idx++)
                    v |= BITNUM(inp, bits[idx], 31 - idx);
                st[k] = v & M32;
            }
            return st;
        }

        private static int InvIpByte(uint[] state, int m)
        {
            return (int)(BITNUMINTR(state[1], m, 7) | BITNUMINTR(state[0], m, 6)
                | BITNUMINTR(state[1], m + 8, 5) | BITNUMINTR(state[0], m + 8, 4)
                | BITNUMINTR(state[1], m + 16, 3) | BITNUMINTR(state[0], m + 16, 2)
                | BITNUMINTR(state[1], m + 24, 1) | BITNUMINTR(state[0], m + 24, 0));
        }

        private static byte[] InvIP2(uint[] state)
        {
            byte[] o = new byte[8];
            o[3] = (byte)InvIpByte(state, 7);
            o[2] = (byte)InvIpByte(state, 6);
            o[1] = (byte)InvIpByte(state, 5);
            o[0] = (byte)InvIpByte(state, 4);
            o[7] = (byte)InvIpByte(state, 3);
            o[6] = (byte)InvIpByte(state, 2);
            o[5] = (byte)InvIpByte(state, 1);
            o[4] = (byte)InvIpByte(state, 0);
            return o;
        }

        private static uint F(uint state, byte[] key)
        {
            uint t1 = BITNUMINTL(state, 31, 0) | ((state & 0xf0000000u) >> 1) | BITNUMINTL(state, 4, 5)
                | BITNUMINTL(state, 3, 6) | ((state & 0x0f000000u) >> 3) | BITNUMINTL(state, 8, 11)
                | BITNUMINTL(state, 7, 12) | ((state & 0x00f00000u) >> 5) | BITNUMINTL(state, 12, 17)
                | BITNUMINTL(state, 11, 18) | ((state & 0x000f0000u) >> 7) | BITNUMINTL(state, 16, 23);

            uint t2 = BITNUMINTL(state, 15, 0) | ((state & 0x0000f000u) << 15) | BITNUMINTL(state, 20, 5)
                | BITNUMINTL(state, 19, 6) | ((state & 0x00000f00u) << 13) | BITNUMINTL(state, 24, 11)
                | BITNUMINTL(state, 23, 12) | ((state & 0x000000f0u) << 11) | BITNUMINTL(state, 28, 17)
                | BITNUMINTL(state, 27, 18) | ((state & 0x0000000fu) << 9) | BITNUMINTL(state, 0, 23);

            t1 &= M32; t2 &= M32;

            byte[] ls = new byte[6];
            ls[0] = (byte)((t1 >> 24) & 0xFF);
            ls[1] = (byte)((t1 >> 16) & 0xFF);
            ls[2] = (byte)((t1 >> 8) & 0xFF);
            ls[3] = (byte)((t2 >> 24) & 0xFF);
            ls[4] = (byte)((t2 >> 16) & 0xFF);
            ls[5] = (byte)((t2 >> 8) & 0xFF);
            for (int i = 0; i < 6; i++) ls[i] = (byte)(ls[i] ^ key[i]);

            uint st = (((uint)SBOX[0][SBOXBIT(ls[0] >> 2)] << 28)
                | ((uint)SBOX[1][SBOXBIT(((ls[0] & 0x03) << 4) | (ls[1] >> 4))] << 24)
                | ((uint)SBOX[2][SBOXBIT(((ls[1] & 0x0f) << 2) | (ls[2] >> 6))] << 20)
                | ((uint)SBOX[3][SBOXBIT(ls[2] & 0x3f)] << 16)
                | ((uint)SBOX[4][SBOXBIT(ls[3] >> 2)] << 12)
                | ((uint)SBOX[5][SBOXBIT(((ls[3] & 0x03) << 4) | (ls[4] >> 4))] << 8)
                | ((uint)SBOX[6][SBOXBIT(((ls[4] & 0x0f) << 2) | (ls[5] >> 6))] << 4)
                | (uint)SBOX[7][SBOXBIT(ls[5] & 0x3f)]) & M32;

            uint outp = 0;
            for (int idx = 0; idx < P.Length; idx++)
                outp |= BITNUMINTL(st, P[idx], idx);
            return outp & M32;
        }

        private static byte[] Crypt(byte[] inp, byte[][] sched)
        {
            uint[] state = IP(inp);
            for (int idx = 0; idx < 15; idx++)
            {
                uint t = state[1];
                state[1] = F(state[1], sched[idx]) ^ state[0];
                state[0] = t;
            }
            state[0] = F(state[1], sched[15]) ^ state[0];
            return InvIP2(state);
        }

        // ---------------- 对外入口 ----------------

        public static byte[] Decrypt3Des(byte[] data, byte[] key)
        {
            byte[][][] sched = TripleDesKeySetup(key, DECRYPT);
            int blocks = data.Length / 8;
            byte[] outBuf = new byte[blocks * 8];
            byte[] blk = new byte[8];
            for (int i = 0; i < blocks; i++)
            {
                Array.Copy(data, i * 8, blk, 0, 8);
                blk = Crypt(blk, sched[0]);
                blk = Crypt(blk, sched[1]);
                blk = Crypt(blk, sched[2]);
                Array.Copy(blk, 0, outBuf, i * 8, 8);
            }
            return outBuf;
        }

        /// <summary>QRC 密文（hex）→ 明文歌词（自动跳过 zlib 头、剥 BOM）。失败返回 null。</summary>
        public static string DecryptQrc(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return null;
                byte[] ct = FromHex(hex);
                if (ct.Length < 8) return null;
                byte[] pt = Decrypt3Des(ct, QqKey);
                if (pt.Length < 3) return null;
                // zlib 头是 2 字节（实测 78 9C），DeflateStream 要跳过它
                using (MemoryStream ms = new MemoryStream(pt, 2, pt.Length - 2))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                    byte[] o = outMs.ToArray();
                    int start = 0;
                    if (o.Length >= 3 && o[0] == 0xEF && o[1] == 0xBB && o[2] == 0xBF) start = 3;
                    return Encoding.UTF8.GetString(o, start, o.Length - start);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static byte[] FromHex(string hex)
        {
            if (hex == null) return new byte[0];
            int n = hex.Length / 2;
            byte[] r = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return new byte[0];
                r[i] = (byte)((hi << 4) | lo);
            }
            return r;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
