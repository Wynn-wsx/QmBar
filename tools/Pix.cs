using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

// 用法: Pix.exe x y w h [stepY]
// 抓屏幕矩形，按若干行打印每隔 20px 的像素颜色，并存 probe\pix.png。
internal static class Pix
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.WriteLine("usage: Pix.exe x y w h [stepY]");
            return 2;
        }
        int x0 = int.Parse(args[0]);
        int y0 = int.Parse(args[1]);
        int w = int.Parse(args[2]);
        int h = int.Parse(args[3]);
        int stepY = args.Length > 4 ? int.Parse(args[4]) : 6;

        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x0, y0, 0, 0, new Size(w, h));
        }

        string outPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "pix.png");
        int scale = args.Length > 5 ? int.Parse(args[5]) : 1;
        if (scale > 1)
        {
            using (Bitmap big = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb))
            {
                using (Graphics g2 = Graphics.FromImage(big))
                {
                    g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g2.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g2.DrawImage(bmp, new Rectangle(0, 0, w * scale, h * scale));
                }
                big.Save(outPath, ImageFormat.Png);
            }
        }
        else
        {
            bmp.Save(outPath, ImageFormat.Png);
        }
        Console.WriteLine("saved " + System.IO.Path.GetFullPath(outPath));

        for (int y = 0; y < h; y += stepY)
        {
            string line = "y=" + y.ToString().PadLeft(2) + " :";
            for (int x = 0; x < w; x += 20)
            {
                Color c = bmp.GetPixel(x, y);
                line += " " + c.R + "," + c.G + "," + c.B;
            }
            Console.WriteLine(line);
        }
        bmp.Dispose();
        return 0;
    }
}
