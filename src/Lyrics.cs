using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QmBar
{
    internal sealed class LyricWord
    {
        public string Text = "";
        public int StartMs;
        public int DurMs;
    }

    internal sealed class LyricLine
    {
        public int StartMs;
        public int DurMs;
        public string Text = "";
        public LyricWord[] Words;      // 逐字（QRC）时有值

        public int EndMs
        {
            get { return DurMs > 0 ? StartMs + DurMs : StartMs; }
        }
    }

    internal sealed class LyricsDoc
    {
        public string Source = "";
        public bool WordTiming;
        public string Raw;                     // 缓存用：原始 LRC/QRC 文本
        public LyricLine[] Lines = new LyricLine[0];

        public bool Ok { get { return Lines != null && Lines.Length > 0; } }

        /// <summary>二分找出当前时刻对应的行（返回 -1 表示还没到第一行）。</summary>
        public int IndexAt(int ms)
        {
            if (Lines.Length == 0) return -1;
            int lo = 0, hi = Lines.Length - 1, r = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Lines[mid].StartMs <= ms) { r = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return r;
        }

        /// <summary>行内进度 0..1（无逐字时按整行时长线性；有逐字时按字精确）。</summary>
        public float Progress(LyricLine line, int ms)
        {
            if (line == null) return 0f;
            int len = line.Text.Length;
            if (len == 0) return 0f;
            return CursorPos(line, ms) / len;
        }

        /// <summary>已唱到第几个字符（浮点，整数部分=完整字符数）。</summary>
        public float CursorPos(LyricLine line, int ms)
        {
            if (line == null) return 0f;
            int len = line.Text.Length;
            if (line.Words != null && line.Words.Length > 0)
            {
                int done = 0;
                for (int i = 0; i < line.Words.Length; i++)
                {
                    LyricWord w = line.Words[i];
                    int wEnd = w.StartMs + (w.DurMs > 0 ? w.DurMs : 0);
                    if (ms >= wEnd)
                    {
                        done += w.Text.Length;
                        continue;
                    }
                    if (ms <= w.StartMs) return done;
                    float f = w.DurMs > 0 ? (float)(ms - w.StartMs) / w.DurMs : 0f;
                    if (f < 0f) f = 0f;
                    if (f > 1f) f = 1f;
                    return done + w.Text.Length * f;
                }
                return done;
            }
            if (line.DurMs > 0)
            {
                float f = (float)(ms - line.StartMs) / line.DurMs;
                if (f < 0f) f = 0f;
                if (f > 1f) f = 1f;
                return len * f;
            }
            return 0f;
        }
    }

    /// <summary>歌词抓取：QQ音乐（LRC/QRC 逐字）优先，LRCLIB 兜底，本地缓存。</summary>
    internal static class Lyrics
    {
        private static readonly Regex ReLrcTag = new Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
        private static readonly Regex ReOffset = new Regex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled);
        private static readonly Regex ReQrcLine = new Regex(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
        private static readonly Regex ReQrcWord = new Regex(@"([^()]*)\((\d+),(\d+)\)", RegexOptions.Compiled);

        public static string CacheDir
        {
            get { return Path.Combine(Config.Dir, "lyrics"); }
        }

        /// <summary>取歌词（阻塞，仅后台线程调用）。返回 null 表示没有。</summary>
        public static LyricsDoc Get(string title, string artist, string album, TimeSpan duration)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string key = Key(title, artist, album);
            string cache = Path.Combine(CacheDir, key + ".lrc");

            LyricsDoc doc = LoadCache(cache);
            if (doc != null) return doc.Ok ? doc : null;

            doc = FetchQq(title, artist, duration);
            if (doc == null || !doc.Ok) doc = FetchLrclib(title, artist, album, duration);
            if (doc == null) doc = new LyricsDoc();

            SaveCache(cache, doc);
            return doc.Ok ? doc : null;
        }

        private static string Key(string title, string artist, string album)
        {
            string s = (title + "\n" + artist + "\n" + album).ToLowerInvariant();
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------- 缓存 ----------------

        private static LyricsDoc LoadCache(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string[] all = File.ReadAllLines(path, Encoding.UTF8);
                if (all.Length < 2) return null;
                string head = all[0];
                if (head.IndexOf("#QMBAR1") != 0) return null;
                bool word = head.IndexOf("word=1") >= 0;
                string source = "cache";
                int si = head.IndexOf("source=");
                if (si >= 0)
                {
                    int end = head.IndexOf(' ', si);
                    source = end > si ? head.Substring(si + 7, end - si - 7) : head.Substring(si + 7);
                }
                string payload = string.Join("\n", all, 1, all.Length - 1);
                if (payload.Trim().Length == 0) return new LyricsDoc { Source = "cache-empty" };
                LyricsDoc d = word ? ParseQrc(payload) : ParseLrc(payload);
                d.Source = source + "(cache)";
                return d;
            }
            catch (Exception) { return null; }
        }

        private static void SaveCache(string path, LyricsDoc doc)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("#QMBAR1 source=" + doc.Source + " word=" + (doc.WordTiming ? "1" : "0"));
                if (doc.Raw != null) sb.Append(doc.Raw);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // ---------------- 解析 ----------------

        /// <summary>标准 LRC：一行可有多个时间标签；忽略纯文本/空行。</summary>
        public static LyricsDoc ParseLrc(string text)
        {
            LyricsDoc doc = new LyricsDoc();
            if (string.IsNullOrEmpty(text)) return doc;

            int offset = 0;
            Match om = ReOffset.Match(text);
            if (om.Success) int.TryParse(om.Groups[1].Value, out offset);

            List<LyricLine> lines = new List<LyricLine>();
            string[] raw = text.Replace("\r", "").Split('\n');
            foreach (string row in raw)
            {
                if (row.Length == 0) continue;
                MatchCollection mc = ReLrcTag.Matches(row);
                if (mc.Count == 0) continue;
                string content = ReLrcTag.Replace(row, "").Trim();
                if (content.Length == 0) continue;
                foreach (Match m in mc)
                {
                    int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    int frac = 0;
                    if (m.Groups[3].Success)
                    {
                        string f = m.Groups[3].Value;
                        if (f.Length == 1) frac = int.Parse(f, CultureInfo.InvariantCulture) * 100;
                        else if (f.Length == 2) frac = int.Parse(f, CultureInfo.InvariantCulture) * 10;
                        else frac = int.Parse(f.Substring(0, 3), CultureInfo.InvariantCulture);
                    }
                    LyricLine ln = new LyricLine();
                    ln.StartMs = min * 60000 + sec * 1000 + frac - offset;
                    ln.Text = content;
                    lines.Add(ln);
                }
            }
            lines.Sort(delegate(LyricLine a, LyricLine b) { return a.StartMs.CompareTo(b.StartMs); });

            // 用下一行的起点补出本行时长（逐字兜底也要用）
            for (int i = 0; i < lines.Count; i++)
            {
                if (i + 1 < lines.Count)
                {
                    int d = lines[i + 1].StartMs - lines[i].StartMs;
                    lines[i].DurMs = d > 0 && d < 30000 ? d : 0;
                }
            }
            doc.Lines = lines.ToArray();
            doc.Source = "lrc";
            doc.Raw = text;
            return doc;
        }

        /// <summary>QQ音乐 QRC（逐字）：[行起点,行时长]字(起点,时长)字(起点,时长)...</summary>
        public static LyricsDoc ParseQrc(string text)
        {
            LyricsDoc doc = new LyricsDoc();
            if (string.IsNullOrEmpty(text)) return doc;

            List<LyricLine> lines = new List<LyricLine>();
            string[] raw = text.Replace("\r", "").Split('\n');
            foreach (string row in raw)
            {
                string line = row.Trim();
                Match lm = ReQrcLine.Match(line);
                if (!lm.Success) continue;
                LyricLine ln = new LyricLine();
                ln.StartMs = int.Parse(lm.Groups[1].Value, CultureInfo.InvariantCulture);
                ln.DurMs = int.Parse(lm.Groups[2].Value, CultureInfo.InvariantCulture);
                string body = lm.Groups[3].Value;

                List<LyricWord> words = new List<LyricWord>();
                StringBuilder plain = new StringBuilder();
                foreach (Match wm in ReQrcWord.Matches(body))
                {
                    string t = wm.Groups[1].Value;
                    if (t.Length == 0) continue;
                    LyricWord w = new LyricWord();
                    w.Text = t;
                    w.StartMs = int.Parse(wm.Groups[2].Value, CultureInfo.InvariantCulture);
                    w.DurMs = int.Parse(wm.Groups[3].Value, CultureInfo.InvariantCulture);
                    words.Add(w);
                    plain.Append(t);
                }
                if (plain.Length == 0) plain.Append(body);
                ln.Text = plain.ToString();
                ln.Words = words.Count > 0 ? words.ToArray() : null;
                if (ln.Text.Trim().Length == 0) continue;
                lines.Add(ln);
            }
            lines.Sort(delegate(LyricLine a, LyricLine b) { return a.StartMs.CompareTo(b.StartMs); });
            doc.Lines = lines.ToArray();
            doc.WordTiming = true;
            doc.Source = "qrc";
            doc.Raw = text;
            return doc;
        }

        // ---------------- 抓取：QQ音乐（现行 musicu.fcg 接口，2025-12 实测有效） ----------------
        //
        // 注意：旧的 client_search_cp 搜索接口与 lyric_download.fcg 取 QRC 接口都已被腾讯下线，
        // 这里全部改用 u.y.qq.com/cgi-bin/musicu.fcg 的模块化调用。

        private const string QqFcg = "https://u.y.qq.com/cgi-bin/musicu.fcg";

        private const string UA =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

        /// <summary>
        /// 我们拿不到 songmid（除非注入客户端），因此按「歌名 + 歌手」搜索反查，再申请歌词。
        /// 搜索结果会校验歌名/歌手/时长，避免张冠李戴。
        /// </summary>
        private static LyricsDoc FetchQq(string title, string artist, TimeSpan duration)
        {
            try
            {
                string mid, songId;
                if (!QqSearch(title, artist, duration, out mid, out songId)) return null;

                // 1) QRC（逐字时间轴）
                LyricsDoc qrc = TryQqQrc(mid, songId);
                if (qrc != null && qrc.Ok) return qrc;

                // 2) 退回逐行 LRC
                LyricsDoc lrc = TryQqLrc(mid);
                if (lrc != null && lrc.Ok) return lrc;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>搜索单曲，取出 songmid 与数字 songID。打分 = 标题(必须命中) + 歌手 + 时长。</summary>
        private static bool QqSearch(string title, string artist, TimeSpan duration, out string mid, out string songId)
        {
            mid = null;
            songId = null;
            try
            {
                string q = title + (string.IsNullOrEmpty(artist) ? "" : " " + artist);
                StringBuilder body = new StringBuilder();
                body.Append("{\"comm\":{\"ct\":\"19\",\"cv\":\"1859\",\"uin\":\"0\"},");
                body.Append("\"req\":{\"method\":\"DoSearchForQQMusicDesktop\",\"module\":\"music.search.SearchCgiService\",");
                body.Append("\"param\":{\"num_per_page\":\"10\",\"page_num\":\"1\",\"query\":\"");
                body.Append(JsonEscape(q));
                body.Append("\",\"search_type\":0}}}");

                string json = HttpPostJson(QqFcg, body.ToString());
                if (string.IsNullOrEmpty(json)) return false;

                string list = FindSongList(json);
                if (string.IsNullOrEmpty(list)) return false;
                string[] objs = SplitJsonObjects(list);
                if (objs.Length == 0) return false;

                string ntitle = Norm(title);
                string nartist = Norm(artist);
                double bestScore = -1;
                string bmid = null, bid = null;

                for (int i = 0; i < objs.Length; i++)
                {
                    string o = objs[i];
                    // 先把嵌套层挖掉：song 对象里 album 排在自身字段之前，且 album 自带
                    // mid/id/name/title，直接取会全撞到专辑身上。
                    string flat = FlattenTopLevel(o);
                    string oMid = ExtractString(flat, "mid");
                    if (string.IsNullOrEmpty(oMid)) continue;

                    // 标题必须命中，否则直接排除 —— 搜索接口对无关词也会返回模糊结果
                    double score = 0;
                    string oTitle = ExtractString(flat, "title");
                    if (string.IsNullOrEmpty(oTitle)) oTitle = ExtractString(flat, "name");
                    string nt = Norm(oTitle);
                    if (nt.Length == 0 || ntitle.Length == 0) continue;
                    if (nt == ntitle) score += 4;
                    else if (nt.IndexOf(ntitle, StringComparison.Ordinal) >= 0
                          || ntitle.IndexOf(nt, StringComparison.Ordinal) >= 0) score += 3;
                    if (score < 3) continue;

                    // 歌手
                    string sArr = ExtractArray(o, "singer");
                    if (!string.IsNullOrEmpty(sArr) && nartist.Length > 0)
                    {
                        string[] names = ExtractAll(sArr, "name");
                        for (int k = 0; k < names.Length; k++)
                        {
                            string nn = Norm(names[k]);
                            if (nn.Length == 0) continue;
                            if (nn == nartist || nn.IndexOf(nartist, StringComparison.Ordinal) >= 0
                                || nartist.IndexOf(nn, StringComparison.Ordinal) >= 0) { score += 3; break; }
                        }
                    }

                    // 时长
                    string[] ivs = ExtractAll(flat, "interval");
                    if (ivs.Length > 0 && duration.TotalSeconds > 5)
                    {
                        int iv;
                        if (int.TryParse(ivs[0].Trim(), out iv) && iv > 0)
                        {
                            double d = Math.Abs(iv - duration.TotalSeconds);
                            if (d < 3) score += 2; else if (d < 8) score += 1;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bmid = oMid;
                        bid = FindSongId(flat, oMid);
                    }
                }

                if (bmid == null || string.IsNullOrEmpty(bid)) return false;
                mid = bmid;
                songId = bid;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 取 QRC 逐字歌词。同一个请求顺带要了 trans/roma，但当前只消费 lyric。
        /// 返回空 lyric 的三种情况：无版权(req.code=24001)、有版权无歌词(伴奏)、以及重试失败。
        /// </summary>
        private static LyricsDoc TryQqQrc(string mid, string songId)
        {
            try
            {
                StringBuilder body = new StringBuilder();
                body.Append("{\"comm\":{\"ct\":11,\"cv\":\"1003006\",\"v\":\"1003006\",\"uin\":\"0\",\"format\":\"json\",\"platform\":\"wk_v17\"},");
                body.Append("\"req\":{\"module\":\"music.musichallSong.PlayLyricInfo\",\"method\":\"GetPlayLyricInfo\",");
                body.Append("\"param\":{\"songMID\":\"").Append(JsonEscape(mid));
                body.Append("\",\"songID\":").Append(songId);
                body.Append(",\"format\":\"qrc\",\"qrc\":1,\"trans\":1,\"roma\":1,\"crypt\":1,\"lrc_t\":0,\"interval\":0}}}");

                string json = HttpPostJson(QqFcg, body.ToString());
                if (string.IsNullOrEmpty(json)) return null;

                string hex = ExtractString(json, "lyric");
                if (string.IsNullOrEmpty(hex)) return null;

                string text = QqDes.DecryptQrc(hex);
                if (string.IsNullOrEmpty(text)) return null;

                text = UnwrapQrcXml(text);
                if (string.IsNullOrEmpty(text)) return null;

                LyricsDoc d = ParseQrc(text);
                if (d.Ok)
                {
                    d.Source = "qq-qrc";
                    return d;
                }
                // 纯音乐等降级情况会返回明文 LRC 占位
                d = ParseLrc(text);
                if (d.Ok) d.Source = "qq-lrc";
                return d.Ok ? d : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// QRC 载荷是 XML：&lt;Lyric_1 LyricType="1" LyricContent="[ti:...]...[0,2250]字(0,160)…"/&gt;
        /// 真正的歌词在 LyricContent 属性里且做了实体转义，要抽出来再反转义。
        /// </summary>
        private static string UnwrapQrcXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            const string head = "LyricContent=\"";
            int a = text.IndexOf(head, StringComparison.Ordinal);
            if (a < 0) return text;                       // 不是 XML（例如纯音乐返回的明文 LRC）
            a += head.Length;
            int b = text.IndexOf("\"/>", a, StringComparison.Ordinal);
            if (b < 0) b = text.IndexOf('"', a);
            if (b < 0) return null;
            return WebUtility.HtmlDecode(text.Substring(a, b - a));
        }

        /// <summary>旧的逐行 LRC 接口。实测必须带 Referer: https://y.qq.com/，否则只回 46 字节的 -1310 错误。</summary>
        private static LyricsDoc TryQqLrc(string mid)
        {
            try
            {
                string url = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid="
                    + Uri.EscapeDataString(mid)
                    + "&format=json&nobase64=1&g_tk=5381&loginUin=0&hostUin=0&inCharset=utf8&outCharset=utf-8&notice=0&platform=yqq.json&needNewCode=0";
                string lj = HttpGet(url, "https://y.qq.com/");
                if (string.IsNullOrEmpty(lj)) return null;
                string lyric = ExtractString(lj, "lyric");
                if (string.IsNullOrEmpty(lyric)) return null;
                LyricsDoc doc = ParseLrc(WebUtility.HtmlDecode(lyric));
                if (!doc.Ok) return null;
                doc.Source = "qq-lrc";
                return doc;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------- 抓取：LRCLIB（兜底） ----------------

        private static LyricsDoc FetchLrclib(string title, string artist, string album, TimeSpan duration)
        {
            try
            {
                StringBuilder url = new StringBuilder("https://lrclib.net/api/get?track_name=");
                url.Append(Uri.EscapeDataString(title));
                if (!string.IsNullOrEmpty(artist)) url.Append("&artist_name=").Append(Uri.EscapeDataString(artist));
                if (!string.IsNullOrEmpty(album)) url.Append("&album_name=").Append(Uri.EscapeDataString(album));
                if (duration.TotalSeconds > 5)
                    url.Append("&duration=").Append(((int)Math.Round(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture));

                string json = HttpGet(url.ToString(), "https://lrclib.net/");
                if (string.IsNullOrEmpty(json)) return null;

                string synced = ExtractString(json, "syncedLyrics");
                if (string.IsNullOrEmpty(synced)) return null;      // 没有时间轴就没法同步，直接放弃
                LyricsDoc d = ParseLrc(synced);
                if (!d.Ok) return null;
                d.Source = "lrclib";
                return d;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------- HTTP / JSON 小工具 ----------------

        private static string HttpGet(string url, string referer)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
            }
            catch (Exception) { }

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 5000;
            req.ReadWriteTimeout = 5000;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
            req.Accept = "*/*";
            try { req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
            catch (Exception) { }

            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static string HttpPostJson(string url, string json)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
            }
            catch (Exception) { }

            byte[] data = Encoding.UTF8.GetBytes(json);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.ContentLength = data.Length;
            req.Timeout = 6000;
            req.ReadWriteTimeout = 6000;
            req.UserAgent = UA;
            req.Accept = "*/*";
            try { req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate; }
            catch (Exception) { }

            using (Stream s = req.GetRequestStream())
            {
                s.Write(data, 0, data.Length);
            }
            using (WebResponse resp = req.GetResponse())
            using (Stream rs = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>取出 "key"[ ... ] 的数组内容（不含方括号），做字符串/转义感知的括号配对。</summary>
        private static string ExtractArray(string json, string key)
        {
            return ExtractBracket(json, key, '[');
        }

        /// <summary>取出 "key"{ ... } 的对象内容（不含花括号）。</summary>
        private static string ExtractObject(string json, string key)
        {
            return ExtractBracket(json, key, '{');
        }

        private static string ExtractBracket(string json, string key, char open)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string pat = "\"" + key + "\"";
            int i = 0;
            while (true)
            {
                i = json.IndexOf(pat, i, StringComparison.Ordinal);
                if (i < 0) return null;
                int c = i + pat.Length;
                while (c < json.Length && char.IsWhiteSpace(json[c])) c++;
                if (c < json.Length && json[c] == ':')
                {
                    int p = c + 1;
                    while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                    if (p < json.Length && json[p] == open) return BracketBody(json, p);
                }
                i = i + pat.Length;
            }
        }

        /// <summary>从下标 open 处的开括号开始配对，返回括号之间的内容。</summary>
        private static string BracketBody(string s, int open)
        {
            int depth = 0;
            bool inStr = false;
            for (int k = open; k < s.Length; k++)
            {
                char ch = s[k];
                if (inStr)
                {
                    if (ch == '\\') { k++; continue; }
                    if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"') { inStr = true; continue; }
                if (ch == '[' || ch == '{') depth++;
                else if (ch == ']' || ch == '}')
                {
                    depth--;
                    if (depth == 0) return s.Substring(open + 1, k - open - 1);
                }
            }
            return null;
        }

        /// <summary>
        /// 搜索结果里有多个 list（album / singer / mv / song …），只有 song.list 的元素带 mid。
        /// 所以不能简单取第一个 "list"，要挑出第一个含 "mid" 的。
        /// </summary>
        private static string FindSongList(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string pat = "\"list\"";
            int i = 0;
            while (true)
            {
                i = json.IndexOf(pat, i, StringComparison.Ordinal);
                if (i < 0) return null;
                int c = i + pat.Length;
                while (c < json.Length && char.IsWhiteSpace(json[c])) c++;
                if (c < json.Length && json[c] == ':')
                {
                    int p = c + 1;
                    while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                    if (p < json.Length && json[p] == '[')
                    {
                        string body = BracketBody(json, p);
                        if (body != null && body.IndexOf("\"mid\"", StringComparison.Ordinal) >= 0) return body;
                    }
                }
                i = i + pat.Length;
            }
        }

        /// <summary>
        /// 挖掉嵌套的对象/数组，只保留最外层的标量字段（字符串整段原样搬运）。
        /// 搜索结果里的 song 对象形如 { act, action{...}, album{mid,id,name,title...},
        /// id, mid, name, title, singer[...], interval, ... }：album 排在最前面且字段同名，
        /// 不先挖掉嵌套层就会把专辑的 mid/id/title 当成歌曲的。
        /// </summary>
        private static string FlattenTopLevel(string obj)
        {
            if (string.IsNullOrEmpty(obj)) return obj;
            StringBuilder b = new StringBuilder(obj.Length);
            int depth = 0;
            for (int k = 0; k < obj.Length; k++)
            {
                char ch = obj[k];
                if (ch == '"')
                {
                    if (depth <= 1) b.Append(ch);
                    k++;
                    while (k < obj.Length)
                    {
                        char c2 = obj[k];
                        if (c2 == '\\')
                        {
                            if (depth <= 1) b.Append(c2);
                            k++;
                            if (k < obj.Length)
                            {
                                if (depth <= 1) b.Append(obj[k]);
                                k++;
                            }
                            continue;
                        }
                        if (depth <= 1) b.Append(c2);
                        if (c2 == '"') break;
                        k++;
                    }
                    continue;
                }
                if (ch == '{' || ch == '[')
                {
                    depth++;
                    if (depth <= 1) b.Append(ch);
                    continue;
                }
                if (ch == '}' || ch == ']')
                {
                    if (depth <= 1) b.Append(ch);
                    depth--;
                    continue;
                }
                if (depth <= 1) b.Append(ch);
            }
            return b.ToString();
        }

        /// <summary>把数组正文切成顶层 {...} 对象（忽略嵌套与字符串内的括号）。</summary>
        private static string[] SplitJsonObjects(string arr)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(arr)) return list.ToArray();

            int depth = 0, start = -1;
            bool inStr = false;
            for (int k = 0; k < arr.Length; k++)
            {
                char ch = arr[k];
                if (inStr)
                {
                    if (ch == '\\') { k++; continue; }
                    if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"') { inStr = true; continue; }
                if (ch == '{')
                {
                    if (depth == 0) start = k;
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        list.Add(arr.Substring(start, k - start + 1));
                        start = -1;
                    }
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 找出与指定 songmid 配对的那个顶层 "id"。
        /// 不能直接取第一个 "id" —— 搜索结果里 album.id 常常排在歌曲 id 前面，会取错。
        /// 做法：定位到值等于该 mid 的 "mid": 字段，再往前找最近的 "id"，那个才是同一层的歌曲 id。
        /// </summary>
        private static string FindSongId(string obj, string mid)
        {
            string pat = "\"mid\":";
            int i = 0;
            while (true)
            {
                i = obj.IndexOf(pat, i, StringComparison.Ordinal);
                if (i < 0) return null;
                int p = i + pat.Length;
                while (p < obj.Length && char.IsWhiteSpace(obj[p])) p++;
                if (p < obj.Length && obj[p] == '"' && ReadJsonString(obj, p) == mid)
                {
                    int idPos = obj.LastIndexOf("\"id\":", i, StringComparison.Ordinal);
                    if (idPos >= 0)
                    {
                        int q = idPos + 5;
                        while (q < obj.Length && char.IsWhiteSpace(obj[q])) q++;
                        int e = q;
                        while (e < obj.Length && char.IsDigit(obj[e])) e++;
                        if (e > q) return obj.Substring(q, e - q);
                    }
                    return null;
                }
                i = p;
            }
        }

        /// <summary>从 JSON 里抠出 "key":"value"（带转义处理），避免引入 JSON 序列化库。</summary>
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            string pat = "\"" + key + "\"";
            while (true)
            {
                i = json.IndexOf(pat, i, StringComparison.Ordinal);
                if (i < 0) return null;
                int c = json.IndexOf(':', i + pat.Length);
                if (c < 0) return null;
                int p = c + 1;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p < json.Length && json[p] == '"') return ReadJsonString(json, p);
                if (p < json.Length && (json[p] == 'n')) return null;   // null
                i = c + 1;
            }
        }

        private static string ReadJsonString(string s, int quoteIndex)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = quoteIndex + 1; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\')
                {
                    if (i + 1 >= s.Length) break;
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        case '/': sb.Append('/'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        default: sb.Append(n); break;
                    }
                }
                else if (ch == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        /// <summary>抠出某个 key 出现的所有字符串/数字值（按出现顺序）。</summary>
        public static string[] ExtractAll(string json, string key)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(json)) return list.ToArray();
            string pat = "\"" + key + "\":";
            int i = 0;
            while (true)
            {
                i = json.IndexOf(pat, i, StringComparison.Ordinal);
                if (i < 0) break;
                int p = i + pat.Length;
                while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
                if (p >= json.Length) break;
                if (json[p] == '"')
                {
                    list.Add(ReadJsonString(json, p));
                    int close = p + 1;
                    // 跳过字符串（含转义）以便继续搜索
                    while (close < json.Length)
                    {
                        if (json[close] == '\\') { close += 2; continue; }
                        if (json[close] == '"') break;
                        close++;
                    }
                    i = close + 1;
                }
                else
                {
                    int end = p;
                    while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
                    list.Add(json.Substring(p, end - p).Trim());
                    i = end;
                }
            }
            return list.ToArray();
        }

        private static string ExtractTag(string xml, string tag)
        {
            int a = xml.IndexOf("<" + tag, StringComparison.OrdinalIgnoreCase);
            if (a < 0) return null;
            int gt = xml.IndexOf('>', a);
            if (gt < 0) return null;
            int b = xml.IndexOf("</" + tag + ">", gt, StringComparison.OrdinalIgnoreCase);
            if (b < 0) return null;
            string v = xml.Substring(gt + 1, b - gt - 1).Trim();
            if (v.StartsWith("<![CDATA[") && v.EndsWith("]]>"))
                v = v.Substring(9, v.Length - 12).Trim();
            return v;
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        private static bool SameName(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            return a == b || a.IndexOf(b, StringComparison.Ordinal) >= 0 || b.IndexOf(a, StringComparison.Ordinal) >= 0;
        }
    }
}
