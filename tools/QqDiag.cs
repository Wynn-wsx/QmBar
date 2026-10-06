using System;
using System.Reflection;
using System.Text;
using QmBar;

internal static class QqDiag
{
    private static MethodInfo M(string name)
    {
        MethodInfo mi = typeof(Lyrics).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (mi == null) throw new Exception("method not found: " + name);
        return mi;
    }

    private static string Post(string url, string body)
    {
        return (string)M("HttpPostJson").Invoke(null, new object[] { url, body });
    }

    private static string Arr(string json, string key)
    {
        return (string)M("ExtractArray").Invoke(null, new object[] { json, key });
    }

    private static string Str(string json, string key)
    {
        return (string)M("ExtractString").Invoke(null, new object[] { json, key });
    }

    private static string[] Objs(string arr)
    {
        return (string[])M("SplitJsonObjects").Invoke(null, new object[] { arr });
    }

    private static string SongId(string obj, string mid)
    {
        return (string)M("FindSongId").Invoke(null, new object[] { obj, mid });
    }

    private static int Main(string[] args)
    {
        string title = args.Length > 0 ? args[0] : "晴天";
        string artist = args.Length > 1 ? args[1] : "周杰伦";
        string q = title + " " + artist;

        StringBuilder b = new StringBuilder();
        b.Append("{\"comm\":{\"ct\":\"19\",\"cv\":\"1859\",\"uin\":\"0\"},");
        b.Append("\"req\":{\"method\":\"DoSearchForQQMusicDesktop\",\"module\":\"music.search.SearchCgiService\",");
        b.Append("\"param\":{\"num_per_page\":\"10\",\"page_num\":\"1\",\"query\":\"");
        b.Append(q);
        b.Append("\",\"search_type\":0}}}");

        string json = Post("https://u.y.qq.com/cgi-bin/musicu.fcg", b.ToString());
        Console.WriteLine("resp null? " + (json == null) + "  len=" + (json == null ? 0 : json.Length));
        if (json != null)
        {
            Console.WriteLine("head: " + json.Substring(0, Math.Min(300, json.Length)));
            string list = (string)M("FindSongList").Invoke(null, new object[] { json });
            Console.WriteLine("list found? " + (list != null) + " len=" + (list == null ? 0 : list.Length));
            if (list != null)
            {
                string[] objs = Objs(list);
                Console.WriteLine("objects = " + objs.Length);
                for (int i = 0; i < Math.Min(objs.Length, 5); i++)
                {
                    string o = objs[i];
                    string flat = (string)M("FlattenTopLevel").Invoke(null, new object[] { o });
                    string mid = Str(flat, "mid");
                    string ti = Str(flat, "title");
                    if (string.IsNullOrEmpty(ti)) ti = Str(flat, "name");
                    string singerArr = Arr(o, "singer");
                    string[] names = singerArr == null ? new string[0] : (string[])M("ExtractAll").Invoke(null, new object[] { singerArr, "name" });
                    string[] ivs = (string[])M("ExtractAll").Invoke(null, new object[] { flat, "interval" });
                    Console.WriteLine(string.Format("  #{0} mid={1} id={2} title={3} singers=[{4}] interval={5}",
                        i, mid, SongId(flat, mid), ti, string.Join(",", names), ivs.Length > 0 ? ivs[0] : "-"));
                }
            }
        }

        object[] qa = new object[] { title, artist, TimeSpan.FromSeconds(269), null, null };
        bool ok = (bool)M("QqSearch").Invoke(null, qa);
        Console.WriteLine("QqSearch ok=" + ok + " mid=" + qa[3] + " songId=" + qa[4]);

        object doc = M("TryQqQrc").Invoke(null, new object[] { qa[3], qa[4] });
        if (doc == null)
        {
            Console.WriteLine("TryQqQrc -> null");
        }
        else
        {
            Console.WriteLine("TryQqQrc -> " + doc.GetType().Name
                + " source=" + doc.GetType().GetField("Source").GetValue(doc)
                + " word=" + doc.GetType().GetField("WordTiming").GetValue(doc)
                + " ok=" + doc.GetType().GetField("Ok").GetValue(doc));
        }
        return 0;
    }
}
