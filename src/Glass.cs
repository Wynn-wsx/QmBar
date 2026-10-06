using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QmBar
{
    /// <summary>
    /// 毛玻璃材质的载体：分层窗口（WS_EX_LAYERED）+ UpdateLayeredWindow 逐像素 alpha。
    /// 控件每帧先画进一张 32bpp 预乘 alpha 位图，再整张贴到屏幕上，
    /// 于是面板半透明、圆角平滑（不会被 GDI 的矩形裁剪切成直角）。
    /// 注意：位图必须是 Format32bppPArgb，UpdateLayeredWindow 要的是预乘 alpha。
    /// </summary>
    internal static class Glass
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        private const int ULW_ALPHA = 0x02;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi,
            uint usage, out IntPtr bits, IntPtr section, uint offset);

        public static Bitmap NewFrame(int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            return new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        }

        private static Bitmap _noise;

        /// <summary>
        /// 磨砂颗粒：一张 64x64 的可平铺噪声贴图（预乘 alpha，alpha 只有 0..12）。
        /// Windows 的亚克力材质本身就带一层很淡的噪声，没有这层颗粒看上去就只是「半透明」而不是「磨砂」。
        /// </summary>
        public static Bitmap Noise()
        {
            if (_noise != null) return _noise;
            int n = 64;
            Bitmap bmp = new Bitmap(n, n, PixelFormat.Format32bppPArgb);
            Random rnd = new Random(20261006);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int a = rnd.Next(0, 13);
                    bool dark = rnd.Next(0, 2) == 0;
                    // 预乘：颜色分量 = 亮度 * alpha / 255，这里 alpha 很小，直接按比例给白/黑
                    int v = dark ? 0 : 255;
                    int c = v * a / 255;
                    bmp.SetPixel(x, y, Color.FromArgb(a, c, c, c));
                }
            }
            _noise = bmp;
            return _noise;
        }

        /// <summary>
        /// 把整张位图贴到窗口上（位置与尺寸保持当前窗口矩形不变）。
        /// 关键：不能直接把 Graphics.GetHdc() 拿到的 DC 交给 UpdateLayeredWindow ——
        /// 那条路会把 alpha 通道丢掉，窗口变成全透明（日志里 ok=True 却什么都看不见）。
        /// 必须自己建一个 32bpp 顶朝下的 DIBSection，用 LockBits 把预乘 alpha 字节
        /// 原样拷进去，再把这块位图选进内存 DC 传给 UpdateLayeredWindow。
        /// </summary>
        public static bool Present(IntPtr hwnd, Bitmap frame, int x, int y)
        {
            if (hwnd == IntPtr.Zero || frame == null) return false;
            int w = frame.Width;
            int h = frame.Height;
            if (w < 1 || h < 1) return false;

            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBmp = IntPtr.Zero;
            IntPtr oldBmp = IntPtr.Zero;
            try
            {
                screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return false;
                memDc = CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero) return false;

                BITMAPINFOHEADER bmi = new BITMAPINFOHEADER();
                bmi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.biWidth = w;
                bmi.biHeight = -h;          // 负数 = 顶朝下，和 GDI+ 的行序一致
                bmi.biPlanes = 1;
                bmi.biBitCount = 32;
                bmi.biCompression = 0;      // BI_RGB

                IntPtr bits;
                hBmp = CreateDIBSection(screenDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (hBmp == IntPtr.Zero || bits == IntPtr.Zero) return false;

                byte[] row = new byte[w * 4];
                Rectangle rect = new Rectangle(0, 0, w, h);
                BitmapData bd = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    for (int r = 0; r < h; r++)
                    {
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)r * bd.Stride), row, 0, row.Length);
                        Marshal.Copy(row, 0, new IntPtr(bits.ToInt64() + (long)r * w * 4), row.Length);
                    }
                }
                finally
                {
                    frame.UnlockBits(bd);
                }

                oldBmp = SelectObject(memDc, hBmp);

                POINT dst;
                dst.X = x;
                dst.Y = y;
                SIZE size;
                size.cx = w;
                size.cy = h;
                POINT src;
                src.X = 0;
                src.Y = 0;
                BLENDFUNCTION bf;
                bf.BlendOp = AC_SRC_OVER;
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = AC_SRC_ALPHA;

                return UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, ULW_ALPHA);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (memDc != IntPtr.Zero && oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
                if (hBmp != IntPtr.Zero) DeleteObject(hBmp);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>把一张不透明位图按指定 alpha 整体变淡（给面板底色用）。</summary>
        public static Color WithAlpha(Color c, int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            int a = percent * 255 / 100;
            return Color.FromArgb(a, c);
        }
    }
}
