using System;
using System.Drawing;

// 检查走马灯文字是否越出裁剪区（压到封面）：统计封面与文字区之间那道空隙里有没有内容。
static class BandScan
{
    static void Main(string[] args)
    {
        int x0 = int.Parse(args[0]);
        int y0 = int.Parse(args[1]);
        int w = int.Parse(args[2]);
        int h = int.Parse(args[3]);

        using (Bitmap b = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(b))
                g.CopyFromScreen(x0, y0, 0, 0, new Size(w, h));

            Color bg = b.GetPixel(330, 2);
            Console.WriteLine("bg = " + bg.R + "," + bg.G + "," + bg.B);

            // 封面右边界约在 46，文字区从 56 起。空隙 48..54 应该是干净背景。
            Console.WriteLine("gap 48..54  titleBand 非背景像素 = " + Count(b, bg, 48, 55, 4, 26));
            Console.WriteLine("gap 48..54  subBand   非背景像素 = " + Count(b, bg, 48, 55, 24, 46));
            Console.WriteLine("text 56..320 titleBand 非背景像素 = " + Count(b, bg, 56, 321, 4, 26));
            Console.WriteLine("text 56..320 subBand   非背景像素 = " + Count(b, bg, 56, 321, 24, 46));

            // 文字是中性亮色（243,243,243），封面是彩色照片。统计封面区里的「中性亮像素」，
            // 就能判断走马灯有没有画到封面上。
            Console.WriteLine("neutral-bright x40..55 titleBand = " + CountNeutral(b, 40, 56, 4, 26));
            Console.WriteLine("neutral-bright x 6..39 titleBand = " + CountNeutral(b, 6, 40, 4, 26));

            foreach (int yy in new int[] { 15, 35 })
            {
                string s = "y=" + yy + " :";
                for (int x = 40; x <= 66; x += 2)
                {
                    Color c = b.GetPixel(x, yy);
                    s += " " + x + "(" + c.R + "," + c.G + "," + c.B + ")";
                }
                Console.WriteLine(s);
            }

            b.Save("probe\\band.png", System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    static int CountNeutral(Bitmap b, int xa, int xb, int ya, int yb)
    {
        int n = 0;
        for (int x = xa; x < xb && x < b.Width; x++)
            for (int y = ya; y < yb && y < b.Height; y++)
            {
                Color c = b.GetPixel(x, y);
                int mx = Math.Max(c.R, Math.Max(c.G, c.B));
                int mn = Math.Min(c.R, Math.Min(c.G, c.B));
                if (mn > 170 && (mx - mn) < 60) { n++; break; }
            }
        return n;
    }

    static int Count(Bitmap b, Color bg, int xa, int xb, int ya, int yb)
    {
        int n = 0;
        for (int x = xa; x < xb && x < b.Width; x++)
            for (int y = ya; y < yb && y < b.Height; y++)
            {
                Color c = b.GetPixel(x, y);
                int d = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
                if (d > 90) { n++; break; }
            }
        return n;
    }
}
