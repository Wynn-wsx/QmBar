using System;
using System.Collections.Generic;
using QmBar;

// 列出当前所有 GSMTC 会话，用来确认 QQ 音乐是否在播、以及插件会选到哪一路。
internal static class SessDiag
{
    private static int Main(string[] args)
    {
        Smtc s = new Smtc();
        if (!s.Init())
        {
            Console.WriteLine("Smtc.Init() failed");
            return 1;
        }
        List<MediaSource> all = s.List();
        Console.WriteLine("sessions = " + all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            MediaSource m = all[i];
            Console.WriteLine(string.Format("  [{0}] app={1} playing={2} title={3}",
                i, m.AppId, m.IsPlaying, m.Title));
        }

        string want = args.Length > 0 ? args[0] : "";
        MediaSource pick = s.Pick(want, all);
        Console.WriteLine("pick = " + (pick == null ? "<null>" : pick.AppId + " / " + pick.Title));

        if (pick != null)
        {
            MediaInfo info = s.Read(pick, true, null, null);
            Console.WriteLine("read: title=" + info.Title
                + " | artist=" + info.Artist
                + " | album=" + info.Album
                + " | playing=" + info.IsPlaying
                + " | pos=" + info.Position.ToString()
                + " | dur=" + info.Duration.ToString()
                + " | cover=" + (info.Cover != null)
                + " | err=" + info.Error);
        }
        return 0;
    }
}
