using System;
using QmBar;

internal static class LyricTest
{
    private static int Main(string[] args)
    {
        string title = args.Length > 0 ? args[0] : "晴天";
        string artist = args.Length > 1 ? args[1] : "周杰伦";
        string album = args.Length > 2 ? args[2] : "叶惠美";
        double dur = args.Length > 3 ? double.Parse(args[3]) : 269.0;

        LyricsDoc d = Lyrics.Get(title, artist, album, TimeSpan.FromSeconds(dur));
        if (d == null) { Console.WriteLine("RESULT: NULL"); return 1; }
        Console.WriteLine("source=" + d.Source + " wordTiming=" + d.WordTiming + " ok=" + d.Ok + " lines=" + d.Lines.Length);
        int n = Math.Min(d.Lines.Length, 10);
        for (int i = 0; i < n; i++)
        {
            LyricLine l = d.Lines[i];
            Console.WriteLine(string.Format("  [{0,7} +{1,5}] {2}   (words={3})",
                l.StartMs, l.DurMs, l.Text, l.Words == null ? 0 : l.Words.Length));
        }
        return d.Ok ? 0 : 2;
    }
}
