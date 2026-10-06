using System;
using System.Threading;
using QmBar;

// 直接验证 SMTC 的定位能力（进度条拖拽最终就是调这个）
static class SeekTest
{
    static void Main()
    {
        Smtc sm = new Smtc();
        Console.WriteLine("init=" + sm.Init());
        System.Collections.Generic.List<MediaSource> all = sm.List();
        Console.WriteLine("sessions=" + all.Count);
        MediaSource pick = sm.Pick("", all);
        if (pick == null) { Console.WriteLine("no session"); return; }

        MediaInfo a = sm.Read(pick, false, null, null);
        Console.WriteLine("before: app=" + a.AppId + " pos=" + a.Position + " dur=" + a.Duration
            + " canSeek=" + a.CanSeek + " playing=" + a.IsPlaying + " err=" + a.Error);

        // QQ 音乐暂停时 timeline 不刷新，先播起来再试定位
        Console.WriteLine("toggle -> " + sm.Toggle(pick.Session));
        Thread.Sleep(3000);
        MediaInfo p1 = sm.Read(pick, false, null, null);
        Console.WriteLine("playing: pos=" + p1.Position + " playing=" + p1.IsPlaying);

        TimeSpan target = TimeSpan.FromSeconds(137);
        bool ok = sm.Seek(pick.Session, target);
        Console.WriteLine("Seek(" + target + ") = " + ok);

        Thread.Sleep(2000);
        MediaInfo b = sm.Read(pick, false, null, null);
        Console.WriteLine("after:  pos=" + b.Position + " err=" + b.Error);

        Console.WriteLine("toggle back -> " + sm.Toggle(pick.Session));
    }
}
