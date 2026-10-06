using System;
using System.Drawing;
using System.Windows.Forms;
using QmBar;

// 验证逐字歌词：①CursorPos 随时间推进 ②卡拉OK绿条用离屏位图裁剪能正确截断
static class KaraokeTest
{
    static string F(float v)
    {
        return v.ToString("0.00");
    }

    [STAThread]
    static void Main()
    {
        LyricsDoc doc = Lyrics.Get("晴天", "周杰伦", "叶惠美", TimeSpan.FromSeconds(269));
        Console.WriteLine("source=" + doc.Source + " wordTiming=" + doc.WordTiming + " lines=" + doc.Lines.Length);

        int shown = 0;
        foreach (LyricLine ln in doc.Lines)
        {
            if (ln.Words == null || ln.Words.Length == 0) continue;
            int s = ln.StartMs;
            Console.WriteLine("[" + s + "," + ln.EndMs + "] \"" + ln.Text + "\" words=" + ln.Words.Length
                + " c0=" + F(doc.CursorPos(ln, s))
                + " c25=" + F(doc.CursorPos(ln, s + ln.DurMs / 4))
                + " c50=" + F(doc.CursorPos(ln, s + ln.DurMs / 2))
                + " c75=" + F(doc.CursorPos(ln, s + ln.DurMs * 3 / 4))
                + " c100=" + F(doc.CursorPos(ln, ln.EndMs))
                + " len=" + ln.Text.Length);
            if (++shown >= 6) break;
        }

        LyricLine line = null;
        foreach (LyricLine ln in doc.Lines)
            if (ln.Words != null && ln.Words.Length > 0) { line = ln; break; }
        if (line == null)
        {
            Console.WriteLine("no word-timed line");
            return;
        }

        Color bg = Color.FromArgb(28, 28, 28);
        Color gray = Color.FromArgb(170, 170, 170);
        Color green = Color.FromArgb(49, 194, 124);
        TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        int maxW = 4000;

        using (Font f = new Font("Microsoft YaHei UI", 9f))
        using (Bitmap b = new Bitmap(460, 128))
        {
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(bg);
                int[] pct = new int[] { 0, 25, 50, 75, 100 };
                for (int i = 0; i < pct.Length; i++)
                {
                    Rectangle rr = new Rectangle(6, 2 + i * 25, 448, 22);
                    float cur = doc.CursorPos(line, line.StartMs + line.DurMs * pct[i] / 100);
                    int chars = (int)Math.Floor(cur);
                    if (chars < 0) chars = 0;
                    if (chars > line.Text.Length) chars = line.Text.Length;

                    int played = TextRenderer.MeasureText(g, line.Text.Substring(0, chars), f,
                        new Size(maxW, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    if (chars < line.Text.Length)
                    {
                        int cw = TextRenderer.MeasureText(g, line.Text.Substring(chars, 1), f,
                            new Size(maxW, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                        played += (int)(cw * (cur - chars));
                    }
                    Console.WriteLine("t=" + pct[i] + "% cursor=" + F(cur) + " chars=" + chars + " playedPx=" + played);

                    TextRenderer.DrawText(g, line.Text, f, rr, gray, fl);

                    int cw2 = Math.Min(played, rr.Width);
                    if (cw2 > 0)
                    {
                        using (Bitmap tmp = new Bitmap(cw2, rr.Height))
                        {
                            using (Graphics g2 = Graphics.FromImage(tmp))
                            {
                                g2.Clear(bg);
                                TextRenderer.DrawText(g2, line.Text, f, new Rectangle(0, 0, rr.Width, rr.Height), green, fl);
                            }
                            g.DrawImageUnscaled(tmp, rr.X, rr.Y);
                        }
                    }

                    g.DrawString("t=" + pct[i] + "%", SystemFonts.DefaultFont, Brushes.White, 390, 4 + i * 25);
                }
            }
            b.Save("probe\\karaoke_demo.png", System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("saved probe\\karaoke_demo.png");
        }
    }
}
