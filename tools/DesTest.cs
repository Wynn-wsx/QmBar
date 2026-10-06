using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using QmBar;

internal static class DesTest
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: DesTest <hexfile> <outfile> [rawDir]");
            return 2;
        }
        string hex = File.ReadAllText(args[0]).Trim();
        byte[] ct = QqDes.FromHex(hex);

        // stage 1: 3DES only
        byte[] pt = QqDes.Decrypt3Des(ct, QqDes.QqKey);
        // stage 2: inflate (skip zlib header)
        byte[] inf;
        using (MemoryStream ms = new MemoryStream(pt, 2, pt.Length - 2))
        using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
        using (MemoryStream outMs = new MemoryStream())
        {
            byte[] buf = new byte[8192];
            int n;
            while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
            inf = outMs.ToArray();
        }

        if (args.Length >= 3)
        {
            File.WriteAllBytes(Path.Combine(args[2], "cs_3des.bin"), pt);
            File.WriteAllBytes(Path.Combine(args[2], "cs_inflate.bin"), inf);
        }

        string s = QqDes.DecryptQrc(hex);
        if (s == null) { Console.Error.WriteLine("DECRYPT FAILED"); return 3; }
        File.WriteAllBytes(args[1], Encoding.UTF8.GetBytes(s));
        Console.Error.WriteLine("3desLen=" + pt.Length + " inflateLen=" + inf.Length + " strChars=" + s.Length
            + " crInInflate=" + CountCr(inf) + " crInString=" + CountCr(Encoding.UTF8.GetBytes(s)));
        return 0;
    }

    private static int CountCr(byte[] b)
    {
        int c = 0;
        for (int i = 0; i < b.Length; i++) if (b[i] == 13) c++;
        return c;
    }
}
