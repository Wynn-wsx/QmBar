using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace QmBar
{
    /// <summary>轻量会话描述（用于「播放源」选择列表）。</summary>
    internal sealed class MediaSource
    {
        public object Session;
        public string AppId = "";
        public string Title = "";
        public bool IsPlaying;

        public override string ToString()
        {
            string name = Smtc.FriendlyName(AppId);
            if (Title != null && Title.Length > 0) return name + " — " + Title;
            return name;
        }
    }

    /// <summary>某一时刻的完整播放快照。由后台线程生成，UI 线程只读。</summary>
    internal sealed class MediaInfo
    {
        public bool HasSession;
        public string AppId = "";
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public bool IsPlaying;
        public TimeSpan Position;
        public TimeSpan Duration;
        public bool CanPlay = true;
        public bool CanPause = true;
        public bool CanNext = true;
        public bool CanPrev = true;
        public bool CanSeek = true;
        public Image Cover;
        public string CoverKey = "";
        public int StampMs;      // 采集时刻 Environment.TickCount，用于插值
        public string Error = "";

        public string TrackKey
        {
            get { return AppId + "\n" + Title + "\n" + Artist + "\n" + Album; }
        }

        /// <summary>按本地时钟插值出当前播放位置，避免每秒跳一格。</summary>
        public TimeSpan EstimatedPosition()
        {
            TimeSpan p = Position;
            if (IsPlaying && StampMs != 0 && Duration > TimeSpan.Zero)
            {
                int elapsed = unchecked(Environment.TickCount - StampMs);
                if (elapsed > 0 && elapsed < 10000) p = p + TimeSpan.FromMilliseconds(elapsed);
                if (p > Duration) p = Duration;
            }
            return p;
        }

        public MediaInfo Clone()
        {
            MediaInfo m = (MediaInfo)MemberwiseClone();
            m.Cover = null;
            return m;
        }
    }

    /// <summary>
    /// 纯反射调用 Windows GSMTC（GlobalSystemMediaTransportControls），
    /// 因此在没有 Windows SDK / Windows.winmd 的机器上也能编译运行。
    /// 注意：所有方法都会阻塞（内部 Task.Wait），只能从后台线程调用。
    /// </summary>
    internal sealed class Smtc
    {
        private object _mgr;
        private MethodInfo _asTask;
        private string _lastError = "";

        public string LastError { get { return _lastError; } }
        public bool Ready { get { return _mgr != null; } }

        private static Type WinRt(string typeName, string contractAssembly)
        {
            string full = typeName + ", " + contractAssembly + ", ContentType=WindowsRuntime";
            Type t = Type.GetType(full);
            if (t == null) throw new Exception("找不到 WinRT 类型：" + full);
            return t;
        }

        private MethodInfo AsTaskGeneric()
        {
            if (_asTask == null)
            {
                Type ext = typeof(System.WindowsRuntimeSystemExtensions);
                foreach (MethodInfo m in ext.GetMethods())
                {
                    if (m.Name == "AsTask" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1")
                    {
                        _asTask = m;
                        break;
                    }
                }
                if (_asTask == null) throw new Exception("未找到 AsTask<T>(IAsyncOperation<T>)");
            }
            return _asTask;
        }

        /// <summary>等一个 IAsyncOperation&lt;T&gt; 完成并取出结果。</summary>
        private object Await(object op, Type resultType)
        {
            if (op == null) return null;
            MethodInfo g = AsTaskGeneric().MakeGenericMethod(resultType);
            object task = g.Invoke(null, new object[] { op });
            try
            {
                ((Task)task).Wait(8000);
            }
            catch (AggregateException aex)
            {
                throw aex.InnerException != null ? aex.InnerException : aex;
            }
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        public bool Init()
        {
            try
            {
                Type tMgr = WinRt("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager", "Windows.Media.Control");
                MethodInfo req = tMgr.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static);
                Type mgrType = req.ReturnType.GetGenericArguments()[0];
                _mgr = Await(req.Invoke(null, null), mgrType);
                if (_mgr == null) { _lastError = "RequestAsync 返回 null"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                _lastError = Describe(ex);
                _mgr = null;
                return false;
            }
        }

        private static string Describe(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        /// <summary>枚举当前所有媒体会话（便宜，可高频调用）。</summary>
        public List<MediaSource> List()
        {
            List<MediaSource> list = new List<MediaSource>();
            if (_mgr == null) return list;
            try
            {
                object sessions = _mgr.GetType().GetMethod("GetSessions").Invoke(_mgr, null);
                foreach (object s in (IEnumerable)sessions)
                {
                    MediaSource ms = new MediaSource();
                    ms.Session = s;
                    ms.AppId = Str(s, "SourceAppUserModelId");
                    ms.Title = ReadTitle(s);
                    ms.IsPlaying = ReadPlaying(s);
                    list.Add(ms);
                }
            }
            catch (Exception ex)
            {
                _lastError = Describe(ex);
            }
            return list;
        }

        private static string Str(object o, string prop)
        {
            try
            {
                object v = o.GetType().GetProperty(prop).GetValue(o, null);
                return v == null ? "" : v.ToString();
            }
            catch (Exception) { return ""; }
        }

        private static string ReadTitle(object session)
        {
            try
            {
                MethodInfo mp = session.GetType().GetMethod("TryGetMediaPropertiesAsync");
                Type mt = mp.ReturnType.GetGenericArguments()[0];
                object props = AwaitStatic(mp, session, mt);
                if (props == null) return "";
                return (string)props.GetType().GetProperty("Title").GetValue(props, null);
            }
            catch (Exception) { return ""; }
        }

        private static bool ReadPlaying(object session)
        {
            try
            {
                object pb = session.GetType().GetMethod("GetPlaybackInfo").Invoke(session, null);
                object st = pb.GetType().GetProperty("PlaybackStatus").GetValue(pb, null);
                return st.ToString() == "Playing";
            }
            catch (Exception) { return false; }
        }

        private static object AwaitStatic(MethodInfo asyncMethod, object instance, Type resultType)
        {
            // 借用同一个 Smtc 实例的 AsTask 查找逻辑：这里直接内联一份，保持静态可用
            Type ext = typeof(System.WindowsRuntimeSystemExtensions);
            MethodInfo asTask = null;
            foreach (MethodInfo m in ext.GetMethods())
            {
                if (m.Name == "AsTask" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1")
                {
                    asTask = m;
                    break;
                }
            }
            if (asTask == null) throw new Exception("未找到 AsTask");
            object op = asyncMethod.Invoke(instance, null);
            if (op == null) return null;
            MethodInfo g = asTask.MakeGenericMethod(resultType);
            object task = g.Invoke(null, new object[] { op });
            try { ((Task)task).Wait(8000); }
            catch (AggregateException aex) { throw aex.InnerException != null ? aex.InnerException : aex; }
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        /// <summary>
        /// 挑一个会话：sourceAppId 非空则精确匹配（找不到就返回 null，避免张冠李戴）；
        /// 否则自动优先级 = 正在播放的 > QQ音乐 > 第一个有 AppId 的。
        /// </summary>
        public MediaSource Pick(string sourceAppId, List<MediaSource> all)
        {
            if (all == null || all.Count == 0) return null;

            if (sourceAppId != null && sourceAppId.Length > 0)
            {
                foreach (MediaSource m in all)
                    if (string.Equals(m.AppId, sourceAppId, StringComparison.OrdinalIgnoreCase))
                        return m;
                return null;
            }

            foreach (MediaSource m in all)
                if (m.IsPlaying) return m;

            foreach (MediaSource m in all)
                if (IsQqMusic(m.AppId)) return m;

            foreach (MediaSource m in all)
                if (m.AppId != null && m.AppId.Length > 0) return m;

            return all[0];
        }

        public static bool IsQqMusic(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return false;
            string s = appId.ToLowerInvariant();
            return s.IndexOf("qqmusic") >= 0;
        }

        public static string FriendlyName(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return "（无）";
            string s = appId.ToLowerInvariant();
            if (s.IndexOf("qqmusic") >= 0) return "QQ音乐";
            if (s.IndexOf("cloudmusic") >= 0 || s.IndexOf("netease") >= 0) return "网易云音乐";
            if (s.IndexOf("kugou") >= 0) return "酷狗音乐";
            if (s.IndexOf("kuwo") >= 0) return "酷我音乐";
            if (s.IndexOf("spotify") >= 0) return "Spotify";
            if (s.IndexOf("douyin") >= 0) return "抖音";
            if (s.IndexOf("vlc") >= 0) return "VLC";
            if (s.IndexOf("potplayer") >= 0) return "PotPlayer";
            if (s.IndexOf("chrome") >= 0) return "Chrome";
            if (s.IndexOf("msedge") >= 0) return "Edge";
            if (s.IndexOf("firefox") >= 0) return "Firefox";
            int i = appId.LastIndexOf('\\');
            if (i < 0) i = appId.LastIndexOf('.');
            return i >= 0 && i + 1 < appId.Length ? appId.Substring(i + 1) : appId;
        }

        /// <summary>读取完整快照。fetchCover=false 时沿用上一次的封面（用 coverCache 传入）。</summary>
        public MediaInfo Read(MediaSource src, bool fetchCover, string lastCoverKey, Image lastCover)
        {
            MediaInfo info = new MediaInfo();
            info.StampMs = Environment.TickCount;
            if (src == null || src.Session == null) return info;

            object s = src.Session;
            Type st = s.GetType();
            info.HasSession = true;
            info.AppId = src.AppId;

            try
            {
                object pb = st.GetMethod("GetPlaybackInfo").Invoke(s, null);
                object status = pb.GetType().GetProperty("PlaybackStatus").GetValue(pb, null);
                info.IsPlaying = status != null && status.ToString() == "Playing";

                object ctl = pb.GetType().GetProperty("Controls").GetValue(pb, null);
                if (ctl != null)
                {
                    info.CanPlay = Bool(ctl, "IsPlayEnabled");
                    info.CanPause = Bool(ctl, "IsPauseEnabled");
                    info.CanNext = Bool(ctl, "IsNextEnabled");
                    info.CanPrev = Bool(ctl, "IsPreviousEnabled");
                    info.CanSeek = Bool(ctl, "IsPlaybackPositionEnabled");
                }
            }
            catch (Exception ex) { info.Error = Describe(ex); }

            try
            {
                object tl = st.GetMethod("GetTimelineProperties").Invoke(s, null);
                info.Position = ToTimeSpan(tl.GetType().GetProperty("Position").GetValue(tl, null));
                info.Duration = ToTimeSpan(tl.GetType().GetProperty("EndTime").GetValue(tl, null));
                TimeSpan start = ToTimeSpan(tl.GetType().GetProperty("StartTime").GetValue(tl, null));
                if (start > TimeSpan.Zero)
                {
                    info.Position = info.Position - start;
                    info.Duration = info.Duration - start;
                }
                if (info.Position < TimeSpan.Zero) info.Position = TimeSpan.Zero;
                if (info.Duration < TimeSpan.Zero) info.Duration = TimeSpan.Zero;
            }
            catch (Exception ex) { if (info.Error.Length == 0) info.Error = Describe(ex); }

            try
            {
                MethodInfo mp = st.GetMethod("TryGetMediaPropertiesAsync");
                Type mt = mp.ReturnType.GetGenericArguments()[0];
                object props = AwaitStatic(mp, s, mt);
                if (props != null)
                {
                    Type pt = props.GetType();
                    info.Title = Nz((string)pt.GetProperty("Title").GetValue(props, null));
                    info.Artist = Nz((string)pt.GetProperty("Artist").GetValue(props, null));
                    info.Album = Nz((string)pt.GetProperty("AlbumTitle").GetValue(props, null));
                    if (info.Artist.Length == 0)
                        info.Artist = Nz((string)pt.GetProperty("AlbumArtist").GetValue(props, null));

                    string key = info.TrackKey;
                    if (lastCover != null && string.Equals(lastCoverKey, key, StringComparison.Ordinal))
                    {
                        info.Cover = lastCover;          // 同一首歌：直接复用，不重复解码
                        info.CoverKey = key;
                    }
                    else if (fetchCover)
                    {
                        object thumb = pt.GetProperty("Thumbnail").GetValue(props, null);
                        Image img = DecodeCover(thumb);
                        if (img != null)
                        {
                            info.Cover = img;
                            info.CoverKey = key;
                        }
                        else
                        {
                            // 解码失败：保留旧封面和旧 key，下一轮再试
                            info.Cover = lastCover;
                            info.CoverKey = lastCoverKey;
                        }
                    }
                    else
                    {
                        info.Cover = lastCover;
                        info.CoverKey = lastCoverKey;
                    }
                }
            }
            catch (Exception ex) { if (info.Error.Length == 0) info.Error = Describe(ex); }

            if (info.Title.Length == 0) info.Title = src.Title;
            return info;
        }

        private static string Nz(string s) { return s == null ? "" : s; }

        private static bool Bool(object o, string prop)
        {
            try
            {
                object v = o.GetType().GetProperty(prop).GetValue(o, null);
                return v is bool ? (bool)v : true;
            }
            catch (Exception) { return true; }
        }

        private static TimeSpan ToTimeSpan(object v)
        {
            if (v is TimeSpan) return (TimeSpan)v;
            return TimeSpan.Zero;
        }

        private static Image DecodeCover(object thumbnail)
        {
            if (thumbnail == null) return null;
            try
            {
                Type tt = thumbnail.GetType();
                MethodInfo or = tt.GetMethod("OpenReadAsync");
                Type streamType = or.ReturnType.GetGenericArguments()[0];
                object winrtStream = AwaitStatic(or, thumbnail, streamType);
                if (winrtStream == null) return null;

                MethodInfo asStream = null;
                foreach (MethodInfo m in typeof(WindowsRuntimeStreamExtensions).GetMethods())
                {
                    if (m.Name == "AsStreamForRead" && m.GetParameters().Length == 1) { asStream = m; break; }
                }
                if (asStream == null) return null;

                using (Stream net = (Stream)asStream.Invoke(null, new object[] { winrtStream }))
                {
                    using (Image raw = Image.FromStream(net))
                    {
                        // 拷一份脱离流，避免流关闭后失效
                        Bitmap bmp = new Bitmap(raw.Width, raw.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.DrawImage(raw, new Rectangle(0, 0, raw.Width, raw.Height));
                        }
                        return bmp;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- 控制命令 ----
        public bool Play(object s) { return InvokeBool(s, "TryPlayAsync"); }
        public bool Pause(object s) { return InvokeBool(s, "TryPauseAsync"); }
        public bool Toggle(object s) { return InvokeBool(s, "TryTogglePlayPauseAsync"); }
        public bool Next(object s) { return InvokeBool(s, "TrySkipNextAsync"); }
        public bool Prev(object s) { return InvokeBool(s, "TrySkipPreviousAsync"); }

        public bool Seek(object s, TimeSpan pos)
        {
            try
            {
                MethodInfo mi = s.GetType().GetMethod("TryChangePlaybackPositionAsync");
                object op = mi.Invoke(s, new object[] { pos.Ticks });
                object res = Await(op, mi.ReturnType.GetGenericArguments()[0]);
                return res is bool ? (bool)res : true;
            }
            catch (Exception ex) { _lastError = Describe(ex); return false; }
        }

        private bool InvokeBool(object s, string name)
        {
            try
            {
                MethodInfo mi = s.GetType().GetMethod(name);
                if (mi == null) return false;
                object op = mi.Invoke(s, null);
                object res = Await(op, mi.ReturnType.GetGenericArguments()[0]);
                return res is bool ? (bool)res : true;
            }
            catch (Exception ex) { _lastError = Describe(ex); return false; }
        }
    }
}
