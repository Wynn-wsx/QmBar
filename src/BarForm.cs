using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QmBar
{
    internal enum MediaCommand { None, Play, Pause, Toggle, Next, Prev, Seek, Refresh }

    /// <summary>
    /// 后台工作线程：轮询 GSMTC、抓歌词、执行控制命令。
    /// 所有阻塞调用（WinRT Task.Wait、网络）都在这里，UI 线程永不被阻塞。
    /// </summary>
    internal sealed class MediaWorker
    {
        private struct Job
        {
            public MediaCommand Cmd;
            public TimeSpan Arg;
        }

        private readonly Smtc _smtc = new Smtc();
        private readonly object _lock = new object();
        private readonly Queue<Job> _jobs = new Queue<Job>();
        private Thread _thread;
        private volatile bool _stop;
        private string _sourceAppId = "";

        private volatile MediaInfo _info;
        private volatile LyricsDoc _lyric;
        private List<MediaSource> _sources = new List<MediaSource>();
        private object _session;
        private string _trackKey = "";
        private Image _cover;
        private Image _coverRetired;
        private string _coverKey = "";

        public bool Ready { get { return _smtc.Ready; } }
        public string LastError { get { return _smtc.LastError; } }
        public MediaInfo Info { get { return _info; } }
        public LyricsDoc Lyric { get { return _lyric; } }

        public List<MediaSource> Sources
        {
            get { lock (_lock) { return new List<MediaSource>(_sources); } }
        }

        public string SourceAppId
        {
            get { lock (_lock) { return _sourceAppId; } }
            set { lock (_lock) { _sourceAppId = value == null ? "" : value; } }
        }

        public void Init() { _smtc.Init(); }

        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "QmBar.Media";
            _thread.Start();
        }

        public void Stop() { _stop = true; }

        public void Post(MediaCommand cmd) { Post(cmd, TimeSpan.Zero); }

        public void Post(MediaCommand cmd, TimeSpan arg)
        {
            lock (_lock)
            {
                Job j = new Job();
                j.Cmd = cmd;
                j.Arg = arg;
                _jobs.Enqueue(j);
            }
        }

        private void Loop()
        {
            while (!_stop)
            {
                int interval = 1000;
                try
                {
                    interval = Poll();
                }
                catch (Exception ex)
                {
                    MediaInfo bad = new MediaInfo();
                    bad.Error = Log.Describe(ex);
                    _info = bad;
                    interval = 1500;
                }
                // 分段睡眠，保证 Stop/命令能及时响应
                int slept = 0;
                while (slept < interval && !_stop)
                {
                    Thread.Sleep(50);
                    slept += 50;
                    lock (_lock) { if (_jobs.Count > 0) break; }
                }
            }
        }

        private int Poll()
        {
            DrainJobs();
            List<MediaSource> all = _smtc.List();
            lock (_lock) { _sources = all; }

            MediaSource pick = _smtc.Pick(SourceAppId, all);
            _session = pick == null ? null : pick.Session;

            MediaInfo info = _smtc.Read(pick, true, _coverKey, _cover);
            if (info.Cover != null && !object.ReferenceEquals(info.Cover, _cover))
            {
                // 保留两代封面，避免 UI 正在画的时候被释放
                if (_coverRetired != null) { try { _coverRetired.Dispose(); } catch (Exception) { } }
                _coverRetired = _cover;
                _cover = info.Cover;
                _coverKey = info.CoverKey;
            }

            if (pick != null && info.Title.Length > 0)
            {
                if (!string.Equals(info.TrackKey, _trackKey, StringComparison.Ordinal))
                {
                    _trackKey = info.TrackKey;
                    LyricsDoc d = null;
                    try { d = Lyrics.Get(info.Title, info.Artist, info.Album, info.Duration); }
                    catch (Exception) { }
                    _lyric = d;
                }
            }
            else
            {
                _trackKey = "";
                _lyric = null;
            }

            _info = info;
            DrainJobs();
            return 400;
        }

        private void DrainJobs()
        {
            while (true)
            {
                Job j;
                lock (_lock)
                {
                    if (_jobs.Count == 0) return;
                    j = _jobs.Dequeue();
                }
                object s = _session;
                try
                {
                    switch (j.Cmd)
                    {
                        case MediaCommand.Play: _smtc.Play(s); break;
                        case MediaCommand.Pause: _smtc.Pause(s); break;
                        case MediaCommand.Toggle: _smtc.Toggle(s); break;
                        case MediaCommand.Next: _smtc.Next(s); break;
                        case MediaCommand.Prev: _smtc.Prev(s); break;
                        case MediaCommand.Seek: _smtc.Seek(s, j.Arg); break;
                        case MediaCommand.Refresh: break;
                    }
                }
                catch (Exception ex) { Log.Write("命令 " + j.Cmd + " 失败: " + Log.Describe(ex)); }
                // 命令后立刻回读一次，UI 反馈更快
                if (j.Cmd != MediaCommand.Refresh && j.Cmd != MediaCommand.None)
                {
                    try
                    {
                        MediaInfo fresh = _smtc.Read(_smtc.Pick(SourceAppId, Sources), false, _coverKey, _cover);
                        if (fresh != null && fresh.HasSession) _info = fresh;
                    }
                    catch (Exception) { }
                }
            }
        }
    }

    internal sealed class BarForm : Form
    {
        private const int Pad = 6;
        private const int TextGap = 10;

        private readonly Config _cfg;
        private readonly MediaWorker _worker;

        private MediaInfo _info = new MediaInfo();
        private LyricsDoc _lyric;
        private int _lyricIndex = -1;

        private IntPtr _trayWnd = IntPtr.Zero;
        private bool _embedded;
        private bool _overlay;
        private bool _layered;                // 毛玻璃：分层窗口 + UpdateLayeredWindow
        private Bitmap _frame;                // 分层模式下的离屏帧
        private int _anchorLeft = -1;         // 展开态面板左边缘（任务栏坐标），用于「向左收缩」
        private int _layerSeq;
        private int _height = 38;
        private int _coverSize = 30;
        private int _radius = 12;

        // 音量
        private int _volPercent = -1;         // -1 = 还没读过
        private bool _volMuted;
        private bool _volDrag;
        private int _volDragX;
        private int _volDragStart;
        private bool _volDragMoved;
        private int _volShowUntil;            // 音量读数显示到什么时候（Environment.TickCount）
        private SettingsForm _settings;
        private bool _openSettings;           // 命令行 --settings：启动后直接打开设置页

        private Font _fTitle;
        private Font _fSub;
        private Color _bg = Color.FromArgb(32, 32, 32);
        private Color _fg = Color.FromArgb(244, 244, 244);
        private Color _fg2 = Color.FromArgb(168, 168, 168);
        private Color _accent = Color.FromArgb(49, 194, 124);
        private bool _light;

        private Rectangle _textRect = Rectangle.Empty;
        private Rectangle[] _btns = new Rectangle[5];

        private int _hoverBtn = -1;
        private bool _hoverProgress;
        private int _pressedBtn = -1;
        private bool _dragging;
        private Point _dragOrigin;
        private int _dragStartOffset;
        private bool _scrubbing;
        private double _scrubRatio;
        private bool _swallowClick;

        private int _mTitle;
        private int _mSub;
        private int _paintSeq;

        private System.Windows.Forms.Timer _uiTimer;
        private System.Windows.Forms.Timer _slowTimer;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private bool _hiddenBySystem;
        private int _errorShown;

        public BarForm(Config cfg, MediaWorker worker)
        {
            _cfg = cfg;
            _worker = worker;

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == "--settings") _openSettings = true;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "QmBar";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            MakeFonts();
            AttachTaskbar();

            BuildMenu();
            BuildTrayIcon();

            _uiTimer = new System.Windows.Forms.Timer();
            _uiTimer.Interval = 33;
            _uiTimer.Tick += OnUiTick;

            _slowTimer = new System.Windows.Forms.Timer();
            _slowTimer.Interval = 1500;
            _slowTimer.Tick += OnSlowTick;

            RefreshColors();
            ApplySize();
        }

        // ---------------- 宿主 / 布局 ----------------

        private void AttachTaskbar()
        {
            TaskbarInfo ti = Taskbar.Probe();
            bool haveTray = ti.Handle != IntPtr.Zero && ti.Valid;
            if (haveTray && _cfg.Embed)
            {
                _embedded = true;
                _overlay = false;
                _trayWnd = ti.Handle;
                _height = Math.Max(36, Math.Min(64, ti.Tray.Height));
            }
            else if (haveTray)
            {
                // Win11 任务栏的 XAML 岛（Windows.UI.Composition.DesktopWindowContentBridge）带
                // WS_EX_NOREDIRECTIONBITMAP，是 DirectComposition 直接合成的一层，不受同级 Win32
                // z 序约束。实测：作为 Shell_TrayWnd 子窗口嵌进去，PrintWindow 能画出完整控件，
                // 但屏幕上整片被任务栏自己的图标盖住（连 SetWindowPos(HWND_TOP) 也压不过）。
                // 所以默认改为「置顶叠加窗」：精确落在任务栏空白槽位上，视觉一致，且能正常收鼠标消息。
                _embedded = false;
                _overlay = true;
                _trayWnd = IntPtr.Zero;
                // 不铺满整个任务栏高度，上下各留 5px，做成一条「浮在任务栏里的胶囊」
                _height = Math.Max(30, Math.Min(64, ti.Tray.Height - 10));
            }
            else
            {
                _embedded = false;
                _overlay = false;
                _trayWnd = IntPtr.Zero;
                _height = 38;
            }
            _coverSize = _height - 8;
            _layered = _cfg.Glass && !_embedded;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                if (_embedded)
                {
                    // 关键：SetParent 不会自动加 WS_CHILD，必须在创建时就带上，否则子窗口收不到鼠标消息
                    cp.Parent = _trayWnd;
                    cp.Style |= Native.WS_CHILD;
                    cp.Style &= ~Native.WS_POPUP;
                    cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                }
                else
                {
                    cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                    // 毛玻璃要靠分层窗口做逐像素 alpha；嵌入模式（子窗口）用不了这条路
                    if (_layered) cp.ExStyle |= Native.WS_EX_LAYERED;
                }
                return cp;
            }
        }

        /// <summary>展开态宽度，自动收敛到任务栏可用槽位，避免盖住任务按钮。</summary>
        private int EffectiveWidth(TaskbarInfo ti)
        {
            int want = _cfg.Collapsed ? _cfg.CollapsedWidth : _cfg.Width;
            if ((_embedded || _overlay) && ti != null && ti.Valid && _cfg.AutoPlace)
            {
                int room = ti.Slot.Width - 8;
                if (room < 96) return 0;                 // 实在放不下就不显示
                if (want > room) want = room;
                if (want < 96) want = 96;
            }
            return want;
        }

        /// <summary>展开态应有的宽度（用于算「向左收缩」时的左边缘）。</summary>
        private int FullWidth(TaskbarInfo ti)
        {
            int want = _cfg.Width;
            if ((_embedded || _overlay) && ti != null && ti.Valid)
            {
                int room = ti.Slot.Width - 8;
                if (want > room) want = room;
            }
            if (want < 96) want = 96;
            return want;
        }

        private void ApplySize()
        {
            TaskbarInfo ti = Taskbar.Probe();
            int w = EffectiveWidth(ti);
            if (w <= 0) w = _cfg.Collapsed ? _cfg.CollapsedWidth : _cfg.Width;
            LayoutBar(w);
            if (Width != w || Height != _height) Size = new Size(w, _height);
            Place(ti);
        }

        private void LayoutBar(int w)
        {
            int right = w - Pad;
            _btns[3] = new Rectangle(right - 16, 0, 16, _height);      // 收起/展开（整高可点）
            right -= 16 + 2;

            if (!_cfg.Collapsed)
            {
                int bs = _height - 14;
                _btns[2] = new Rectangle(right - bs, (_height - bs) / 2, bs, bs);   // 下一首
                right -= bs + 2;
                int pb = _height - 10;
                _btns[1] = new Rectangle(right - pb, (_height - pb) / 2, pb, pb);   // 播放/暂停
                right -= pb + 2;
                _btns[0] = new Rectangle(right - bs, (_height - bs) / 2, bs, bs);   // 上一首
                right -= bs + 2;
                if (_cfg.ShowVolume)
                {
                    _btns[4] = new Rectangle(right - bs, (_height - bs) / 2, bs, bs);   // 音量
                    right -= bs + 4;
                }
                else
                {
                    _btns[4] = Rectangle.Empty;
                    right -= 4;
                }
            }
            else
            {
                int pb = _height - 10;
                _btns[1] = new Rectangle(right - pb, (_height - pb) / 2, pb, pb);
                right -= pb + 4;
                _btns[0] = Rectangle.Empty;
                _btns[2] = Rectangle.Empty;
                _btns[4] = Rectangle.Empty;
            }

            int left = Pad + _coverSize + TextGap;
            int avail = right - left;
            _textRect = _cfg.Collapsed || avail < 40
                ? Rectangle.Empty
                : new Rectangle(left, 0, avail, _height);
        }

        private void Place(TaskbarInfo ti)
        {
            if (ti == null) ti = Taskbar.Probe();
            int w = Width;

            if ((_embedded || _overlay) && ti.Valid && ti.Handle != IntPtr.Zero)
            {
                // 统一用「相对任务栏左边缘」的坐标算，再换算成子窗口/屏幕坐标
                int lo, hi;                       // 允许摆放的范围（相对任务栏左边缘）
                if (_cfg.AutoPlace)
                {
                    lo = ti.Slot.Left - ti.Tray.Left + 4;
                    hi = ti.Slot.Right - ti.Tray.Left - w - 4;
                }
                else
                {
                    // 手动模式：整条任务栏都能放
                    lo = 0;
                    hi = ti.Tray.Width - w;
                }
                if (hi < lo) hi = lo;

                int left;
                if (!_cfg.AutoPlace)
                {
                    left = _cfg.PosX;
                }
                else if (_cfg.Collapsed && _anchorLeft >= 0)
                {
                    // 「向左收缩」：收起时左边缘不动，右边缘向左收回
                    left = _anchorLeft;
                }
                else
                {
                    left = (ti.Slot.Right - ti.Tray.Left) - 8 - FullWidth(ti);
                    _anchorLeft = left;
                }
                left += _cfg.OffsetX;
                if (left < lo) left = lo;
                if (left > hi) left = hi;

                int y = ti.Tray.Top + (ti.Tray.Height - _height) / 2 + _cfg.OffsetY;
                if (y < ti.Tray.Top) y = ti.Tray.Top;
                if (y > ti.Tray.Bottom - _height) y = ti.Tray.Bottom - _height;

                if (_embedded)
                {
                    // 子窗口坐标是相对父窗口客户区的。Shell_TrayWnd 无边框无菜单，
                    // 客户区原点 == 窗口左上角，所以直接传 x/y，不能再减 Tray.Left/Top。
                    Native.SetWindowPos(Handle, IntPtr.Zero,
                        left, y - ti.Tray.Top, w, _height,
                        Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                }
                else
                {
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                        ti.Tray.Left + left, y, w, _height,
                        Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                }
            }
            else
            {
                Screen scr = Screen.PrimaryScreen;
                int x = scr.WorkingArea.Right - w - 12 + _cfg.OffsetX;
                int y = scr.WorkingArea.Bottom - _height - 12 + _cfg.OffsetY;
                Native.SetWindowPos(Handle, IntPtr.Zero, x, y, w, _height,
                    Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshColors();
            ApplySize();
            Log.Write("OnHandleCreated: hwnd=" + Handle.ToInt64()
                + " style=0x" + ((uint)Native.GetWindowLong(Handle, -16)).ToString("X8")
                + " ex=0x" + ((uint)Native.GetWindowLong(Handle, -20)).ToString("X8")
                + " parent=" + Native.GetParent(Handle).ToInt64()
                + " tray=" + _trayWnd.ToInt64() + " embedded=" + _embedded);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AttachToTaskbar();
            RefreshColors();
            ApplySize();
            _uiTimer.Start();
            _slowTimer.Start();
            if (_overlay)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Log.Write("OnShown: visible=" + Visible + " bounds=" + Bounds
                + " parent=" + Native.GetParent(Handle).ToInt64()
                + " embedded=" + _embedded + " overlay=" + _overlay);

            if (_openSettings) BeginInvoke(new MethodInvoker(ShowSettings));
        }

        /// <summary>
        /// WinForms 在 Show() 期间会把“在 CreateParams 里指定了 Parent”的窗体重新挂到自己内部的
        /// 隐藏宿主窗口上（实测 parent 由 65844 变成 WinForms 私有窗口），Parent 属性被推翻。
        /// 所以显示之后必须再显式 SetParent 一次：先置 WS_CHILD，再 SetParent，最后才定位置。
        /// </summary>
        private void AttachToTaskbar()
        {
            if (!_embedded || _trayWnd == IntPtr.Zero) return;

            int style = Native.GetWindowLong(Handle, Native.GWL_STYLE);
            Native.SetWindowLong(Handle, Native.GWL_STYLE, (style | Native.WS_CHILD) & ~Native.WS_POPUP);

            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE,
                ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

            IntPtr old = Native.SetParent(Handle, _trayWnd);
            Log.Write("AttachToTaskbar: oldParent=" + old.ToInt64()
                + " newParent=" + Native.GetParent(Handle).ToInt64()
                + " style=0x" + ((uint)Native.GetWindowLong(Handle, Native.GWL_STYLE)).ToString("X8"));
        }

        // ---------------- 字体 / 配色 ----------------

        private void MakeFonts()
        {
            float s = _cfg.FontSize;
            if (s < 7f) s = 7f;
            if (s > 16f) s = 16f;
            Font oldTitle = _fTitle;
            Font oldSub = _fSub;
            _fTitle = new Font("Microsoft YaHei UI", s, FontStyle.Bold, GraphicsUnit.Point);
            _fSub = new Font("Microsoft YaHei UI", s - 1f < 7f ? 7f : s - 1f, FontStyle.Regular, GraphicsUnit.Point);
            if (oldTitle != null) oldTitle.Dispose();
            if (oldSub != null) oldSub.Dispose();
        }

        private void RefreshColors()
        {
            // 叠加模式下面板就压在任务栏上，所以底色一律取任务栏空白处那一像素：
            // 面板颜色跟着任务栏/壁纸走，才不会出现「主体偏灰」的塑料感。
            _bg = Taskbar.SampleBackground();
            if (_bg.A < 255) _bg = Color.FromArgb(255, _bg.R, _bg.G, _bg.B);
            int lum = (_bg.R * 299 + _bg.G * 587 + _bg.B * 114) / 1000;
            _light = lum > 140;
            _fg = _light ? Color.FromArgb(24, 24, 24) : Color.FromArgb(244, 244, 244);
            _fg2 = _light ? Color.FromArgb(100, 100, 100) : Color.FromArgb(170, 170, 170);
            _accent = Color.FromArgb(49, 194, 124);      // QQ音乐绿
            BackColor = _bg;
        }

        private static Color Mix(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // ---------------- 定时器 ----------------

        private void OnUiTick(object sender, EventArgs e)
        {
            MediaInfo latest = _worker.Info;
            if (!object.ReferenceEquals(latest, _info) && latest != null)
            {
                _info = latest;
                _lyric = _worker.Lyric;
            }
            if (_info == null) _info = new MediaInfo();

            if (_info.Error != null && _info.Error.Length > 0 && _errorShown < 3)
            {
                _errorShown++;
                Log.Write("读取媒体会话出错: " + _info.Error);
            }

            bool playing = _info.IsPlaying;
            bool anim = playing || _scrubbing || _mTitle != 0 || _mSub != 0;
            if (anim || (_paintSeq & 7) == 0) Invalidate();
            // 分层窗口（毛玻璃）拿不到可靠的 WM_PAINT，所以由定时器直接驱动合成
            if (_layered) RenderLayered();
        }

        private void OnSlowTick(object sender, EventArgs e)
        {
            try
            {
                // 资源管理器重启 → 任务栏句柄变了 → 自重启找回嵌入
                if (_embedded && !Native.IsWindow(_trayWnd))
                {
                    Log.Write("检测到任务栏窗口失效（资源管理器可能重启），自重启接管");
                    RestartSelf();
                    return;
                }

                TaskbarInfo ti = Taskbar.Probe();
                if (_embedded && ti.Handle != IntPtr.Zero && ti.Handle != _trayWnd && !Native.IsWindow(_trayWnd))
                {
                    RestartSelf();
                    return;
                }

                bool hide = false;
                string why = "";
                if (!_cfg.Show) { hide = true; why = "cfg.Show=false"; }
                else if ((_embedded || _overlay) && (!ti.Valid || ti.AutoHidden)) { hide = true; why = "ti.Valid=" + ti.Valid + " AutoHidden=" + ti.AutoHidden; }
                else if (_cfg.HideOnFullscreen && Taskbar.IsForegroundFullscreen(Handle)) { hide = true; why = "前台全屏"; }

                if (hide != _hiddenBySystem)
                {
                    _hiddenBySystem = hide;
                    Visible = !hide;
                    Log.Write("可见性切换: hide=" + hide + " 原因=" + why + " Visible=" + Visible);
                }
                if (!hide)
                {
                    if (!_volDrag) SyncVolume();
                    RefreshColors();
                    ApplySize();
                    // 保持置顶：全屏退出、其他置顶窗抢上来之后要压回去
                    Native.SetWindowPos(Handle, _overlay ? Native.HWND_TOPMOST : IntPtr.Zero, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    UpdateTrayText();
                    Invalidate();
                }
            }
            catch (Exception ex)
            {
                Log.Write("定时器异常: " + Log.Describe(ex));
            }
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            string t = "QmBar";
            if (_info != null && _info.HasSession && _info.Title.Length > 0)
            {
                t = "QmBar — " + _info.Title;
                if (_info.Artist.Length > 0) t += " · " + _info.Artist;
            }
            else if (!_worker.Ready)
            {
                t = "QmBar — 无法读取系统媒体会话";
            }
            if (t.Length > 62) t = t.Substring(0, 62);
            try { _tray.Text = t; } catch (Exception) { }
        }

        private void RestartSelf()
        {
            try { _cfg.Save(); } catch (Exception) { }
            try
            {
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo(Config.ExePath(), "--replace");
                psi.UseShellExecute = false;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("自重启失败: " + Log.Describe(ex)); }
            Environment.Exit(0);
        }

        // ---------------- 绘制 ----------------

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            _paintSeq++;
            if (_paintSeq < 4) Log.Write("OnPaint #" + _paintSeq + " layered=" + _layered + " w=" + Width);

            if (_layered)
            {
                RenderLayered();
                return;
            }

            Compose(e.Graphics, true);
        }

        /// <summary>
        /// 毛玻璃合成：把整帧画进一张 32bpp 预乘 alpha 位图，再交给 UpdateLayeredWindow 贴到屏幕上。
        /// 注意：不能只靠 WM_PAINT —— 分层窗口在第一次贴图之前不参与绘制，容易变成「永远不画」，
        /// 所以这个方法同时由 33ms 定时器驱动。
        /// </summary>
        private void RenderLayered()
        {
            if (!_layered || Handle == IntPtr.Zero || !Visible) return;
            if (Width <= 0 || _height <= 0) return;

            if (_frame == null || _frame.Width != Width || _frame.Height != _height)
            {
                if (_frame != null) _frame.Dispose();
                _frame = Glass.NewFrame(Width, _height);
            }

            bool ok = false;
            try
            {
                using (Graphics g2 = Graphics.FromImage(_frame))
                {
                    g2.Clear(Color.Transparent);
                    Compose(g2, false);
                }
                RECT rc;
                int px = Left, py = Top;
                if (Native.GetWindowRect(Handle, out rc)) { px = rc.Left; py = rc.Top; }
                ok = Glass.Present(Handle, _frame, px, py);

                // UpdateLayeredWindow 会改动窗口位置，可能把窗口挤到置顶带的下层，
                // 所以每次贴图后都把「置顶」这个属性重新压一遍。
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

                _layerSeq++;
                if (_layerSeq == 1)
                    Log.Write("毛玻璃已贴图: ok=" + ok + " at=" + px + "," + py + " size=" + _frame.Width + "x" + _frame.Height);
            }
            catch (Exception ex)
            {
                Log.Write("毛玻璃绘制失败: " + Log.Describe(ex));
            }

            if (!ok)
            {
                Log.Write("毛玻璃不可用，回退到不透明面板");
                DisableLayered();
                Invalidate();
            }
        }

        /// <summary>关掉分层窗口样式，退回普通窗口绘制。</summary>
        private void DisableLayered()
        {
            _layered = false;
            if (_frame != null) { _frame.Dispose(); _frame = null; }
            if (Handle == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_LAYERED);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER
                | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        /// <summary>把所有内容画到给定的 Graphics 上。opaque=true 表示画在普通窗口上（不透明底衬）。</summary>
        private void Compose(Graphics g, bool opaque)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = opaque ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;

            DrawPanel(g, opaque);
            DrawCover(g);
            DrawTextBlock(g);
            DrawButtons(g);
            DrawProgress(g);

            // 未锁定位置时给个可拖拽的视觉提示
            if (!_cfg.Locked && (_hoverBtn < 0) && _hoverProgress)
            {
                using (Pen p = new Pen(Mix(_bg, _fg, 0.25f), 1f))
                using (GraphicsPath path = Round(new Rectangle(0, 0, Width - 1, _height - 1), _radius))
                    g.DrawPath(p, path);
            }
        }

        /// <summary>面板底衬。分层模式下是半透明毛玻璃（圆角 + 顶部高光 + 细边框），否则整块不透明填充。</summary>
        private void DrawPanel(Graphics g, bool opaque)
        {
            bool hi = (_hoverBtn >= 0) || _hoverProgress;

            if (opaque)
            {
                Color bg = _bg;
                if (hi) bg = Mix(_bg, _light ? Color.Black : Color.White, 0.04f);
                using (SolidBrush b = new SolidBrush(bg))
                    g.FillRectangle(b, ClientRectangle);
                return;
            }

            // 磨砂玻璃：底色直接由任务栏空白处的采样色（_bg）推导 —— 只做「加深」不做「掺灰」，
            // 所以背后是什么色调，面板就是什么色调；再靠低不透明度 + 顶部柔光 + 颗粒出玻璃质感。
            Color tint = _light ? Mix(_bg, Color.White, 0.50f) : Mix(_bg, Color.Black, 0.45f);
            if (hi) tint = Mix(tint, _light ? Color.White : Color.FromArgb(150, 158, 176), 0.50f);

            int alpha = _cfg.Opacity * 255 / 100;
            if (alpha < 15) alpha = 15;

            Rectangle rc = new Rectangle(0, 0, Width, _height);
            using (GraphicsPath path = Round(rc, _radius))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, tint)))
                    g.FillPath(b, path);

                GraphicsState st = g.Save();
                g.SetClip(path, CombineMode.Intersect);

                // 顶部反光：模拟玻璃面上的那一层柔光（只覆盖上 3/5，到中下部就完全消失，避免整体泛白）
                Rectangle top = new Rectangle(1, 1, Width - 2, Math.Max(4, _height * 3 / 5));
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new Rectangle(top.X, top.Y, top.Width, top.Height),
                    Color.FromArgb(_light ? 88 : 20, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    LinearGradientMode.Vertical))
                    g.FillRectangle(lb, top);

                // 磨砂颗粒
                using (TextureBrush tb = new TextureBrush(Glass.Noise(), WrapMode.Tile))
                    g.FillPath(tb, path);

                g.Restore(st);
            }

            // 内圈高光 + 极淡的外边框，让边缘「收」得住但又不出现硬线
            using (Pen p = new Pen(Color.FromArgb(_light ? 130 : 18, 255, 255, 255), 1f))
            using (GraphicsPath path = Round(new Rectangle(0, 0, Width - 1, _height - 1), _radius))
                g.DrawPath(p, path);
        }

        private void DrawCover(Graphics g)
        {
            Rectangle r = new Rectangle(Pad, (_height - _coverSize) / 2, _coverSize, _coverSize);
            if (_info != null && _info.Cover != null)
            {
                GraphicsState st = g.Save();
                using (GraphicsPath p = Round(r, 4))
                {
                    g.SetClip(p, CombineMode.Intersect);
                    g.DrawImage(_info.Cover, r);
                }
                g.Restore(st);
            }
            else
            {
                using (GraphicsPath p = Round(r, 4))
                using (SolidBrush b = new SolidBrush(Mix(_bg, _fg, 0.10f)))
                    g.FillPath(b, p);
                DrawNote(g, r, Mix(_bg, _fg, 0.45f));
            }
        }

        private static void DrawNote(Graphics g, Rectangle r, Color c)
        {
            float w = r.Width, h = r.Height;
            float cx = r.X + w * 0.42f, cy = r.Y + h * 0.68f;
            float rx = w * 0.15f, ry = h * 0.105f;
            using (SolidBrush b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - rx, cy - ry, rx * 2f, ry * 2f);
                float x = cx + rx * 0.7f;
                float sw = Math.Max(1.5f, w * 0.05f);
                g.FillRectangle(b, x, r.Y + h * 0.20f, sw, h * 0.50f);
                g.FillRectangle(b, x, r.Y + h * 0.20f, w * 0.26f, Math.Max(1.5f, h * 0.055f));
            }
        }

        private void DrawTextBlock(Graphics g)
        {
            if (_textRect.Width <= 20) return;

            int half = _height / 2;
            Rectangle titleRect = new Rectangle(_textRect.X, 1, _textRect.Width, half);
            Rectangle subRect = new Rectangle(_textRect.X, 1 + half, _textRect.Width, _height - half - 1);

            // 拖音量时整块文字区让给音量读数
            if (_volDrag || _volShowUntil > Environment.TickCount)
            {
                int vp = _volPercent < 0 ? 0 : _volPercent;
                string s1 = _volMuted ? "已静音" : "音量 " + vp.ToString() + "%";
                DrawClipped(g, s1, _fTitle, _fg, titleRect, false, 0);
                string s2 = _volDrag ? "左右拖动调节" : "滚轮调节 · 单击音量图标静音";
                DrawClipped(g, s2, _fSub, _fg2, subRect, false, 0);
                return;
            }

            bool has = _info != null && _info.HasSession;
            string title = has ? _info.Title : (string.IsNullOrEmpty(_worker.LastError) ? "未检测到正在播放的媒体" : "媒体会话不可用");
            if (title.Length == 0) title = "（未知曲目）";
            if (has && !string.IsNullOrEmpty(_info.Artist))
                title = title + " - " + _info.Artist;

            if (!has)
            {
                DrawClipped(g, title, _fTitle, _fg2, titleRect, false, 0);
                if (!string.IsNullOrEmpty(_worker.LastError))
                    DrawClipped(g, _worker.LastError, _fSub, _fg2, subRect, false, 0);
                return;
            }

            bool over = TextWidth(g, title, _fTitle) > titleRect.Width;
            if (over) _mTitle += 1; else _mTitle = 0;
            if (_mTitle > 1000000) _mTitle = 0;
            DrawClipped(g, title, _fTitle, _fg, titleRect, over, over ? (_mTitle / 2) % (TextWidth(g, title, _fTitle) + 40) : 0);

            // 第二行：拖拽进度时显示时间；否则优先歌词；再否则歌手 · 专辑
            if (_scrubbing && _info.Duration > TimeSpan.Zero)
            {
                double pos = _scrubRatio * _info.Duration.TotalSeconds;
                string t = Fmt(TimeSpan.FromSeconds(pos)) + " / " + Fmt(_info.Duration);
                _mSub = 0;
                DrawClipped(g, t, _fSub, _accent, subRect, false, 0);
                return;
            }

            LyricLine line = CurrentLyricLine();
            if (line != null && line.Text.Length > 0)
            {
                bool wide = TextWidth(g, line.Text, _fSub) > subRect.Width;
                if (wide || !_cfg.Karaoke || !_lyric.WordTiming)
                {
                    if (wide) _mSub += 1; else _mSub = 0;
                    int off = wide ? (_mSub / 2) % (TextWidth(g, line.Text, _fSub) + 40) : 0;
                    DrawClipped(g, line.Text, _fSub, over || wide ? _fg2 : _accent, subRect, wide, off);
                }
                else
                {
                    _mSub = 0;
                    DrawKaraoke(g, line, subRect);
                }
                return;
            }

            string sub = _info.Album;
            if (sub.Length == 0) sub = _info.Artist;
            if (sub.Length == 0) sub = _cfg.Lyrics ? "（暂无歌词）" : "";
            bool w2 = TextWidth(g, sub, _fSub) > subRect.Width;
            if (w2) _mSub += 1; else _mSub = 0;
            int off2 = w2 ? (_mSub / 2) % (TextWidth(g, sub, _fSub) + 40) : 0;
            DrawClipped(g, sub, _fSub, _fg2, subRect, w2, off2);
        }

        private LyricLine CurrentLyricLine()
        {
            if (!_cfg.Lyrics || _lyric == null || !_lyric.Ok) return null;
            int ms = (int)(_info.EstimatedPosition().TotalMilliseconds);
            int idx = _lyric.IndexAt(ms);
            if (idx < 0) return null;
            _lyricIndex = idx;
            LyricLine line = _lyric.Lines[idx];
            // 行已唱完很久（比如纯音乐段落）就不再显示，避免残留
            if (line.EndMs > 0 && ms > line.EndMs + 6000) return null;
            return line;
        }

        private void DrawKaraoke(Graphics g, LyricLine line, Rectangle r)
        {
            int ms = (int)(_info.EstimatedPosition().TotalMilliseconds);
            float cur = _lyric.CursorPos(line, ms);
            int chars = (int)Math.Floor(cur);
            if (chars < 0) chars = 0;
            if (chars > line.Text.Length) chars = line.Text.Length;

            DrawClipped(g, line.Text, _fSub, _fg2, r, false, 0);

            int played = TextWidth(g, line.Text.Substring(0, chars), _fSub);
            if (chars < line.Text.Length)
            {
                int cw = TextWidth(g, line.Text.Substring(chars, 1), _fSub);
                played += (int)(cw * (cur - chars));
            }
            if (played <= 0) return;

            int cw2 = Math.Min(played, r.Width);
            if (cw2 <= 0) return;

            if (_layered)
            {
                // 分层模式：真身就是一张 32bpp 位图，GDI+ 的 SetClip 能正常裁剪
                GraphicsState st = g.Save();
                g.SetClip(new Rectangle(r.X, r.Y, cw2, r.Height), CombineMode.Intersect);
                using (SolidBrush b = new SolidBrush(_accent))
                    g.DrawString(line.Text, _fSub, b, new RectangleF(r.X, r.Y, r.Width, r.Height), SfText());
                g.Restore(st);
                return;
            }

            // 注意：TextRenderer 走的是 GDI，它**不认 Graphics.SetClip**（实测走马灯会画到封面上去）。
            // 所以用一张 _bg 色的离屏位图当裁剪区，画完再贴回去。
            using (Bitmap tmp = new Bitmap(cw2, r.Height))
            {
                using (Graphics g2 = Graphics.FromImage(tmp))
                {
                    g2.Clear(_bg);
                    TextRenderer.DrawText(g2, line.Text, _fSub,
                        new Rectangle(0, 0, r.Width, r.Height), _accent, TextFlags());
                }
                g.DrawImageUnscaled(tmp, r.X, r.Y);
            }
        }

        internal static TextFormatFlags TextFlags()
        {
            return TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        }

        private static StringFormat _sfText;

        /// <summary>GDI+ 单行、不换行、垂直居中、不带排版留白（GenericTypographic）。</summary>
        private static StringFormat SfText()
        {
            if (_sfText == null)
            {
                StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
                sf.FormatFlags = (sf.FormatFlags | StringFormatFlags.NoWrap) & ~StringFormatFlags.NoClip;
                sf.Trimming = StringTrimming.None;
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                _sfText = sf;
            }
            return _sfText;
        }

        /// <summary>量一行文本的像素宽。分层模式用 GDI+ 量，保证与绘制同一套度量。</summary>
        private int TextWidth(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (_layered)
            {
                SizeF sz = g.MeasureString(s, f, new PointF(0f, 0f), SfText());
                return (int)Math.Ceiling(sz.Width);
            }
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, 1000),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        private void DrawClipped(Graphics g, string text, Font f, Color c, Rectangle r, bool scroll, int offset)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 0) return;

            if (!scroll)
            {
                if (_layered)
                {
                    using (SolidBrush b = new SolidBrush(c))
                        g.DrawString(text, f, b, new RectangleF(r.X, r.Y, r.Width, r.Height), SfText());
                }
                else
                {
                    TextRenderer.DrawText(g, text, f,
                        new Rectangle(r.X, r.Y, Math.Min(r.Width, TextWidth(g, text, f) + 2), r.Height),
                        c, TextFlags());
                }
                return;
            }

            int w = TextWidth(g, text, f);

            if (_layered)
            {
                int x = r.X - offset;
                GraphicsState st = g.Save();
                g.SetClip(r, CombineMode.Intersect);
                using (SolidBrush b = new SolidBrush(c))
                {
                    g.DrawString(text, f, b, new RectangleF(x, r.Y, w + 2, r.Height), SfText());
                    g.DrawString(text, f, b, new RectangleF(x + w + 40, r.Y, w + 2, r.Height), SfText());
                }
                g.Restore(st);
                return;
            }

            // TextRenderer 走 GDI，**不认 Graphics.SetClip**（实测走马灯会画到封面上去）。
            // 所以用一张 _bg 色的离屏位图当裁剪区，画完原样贴回来。
            int xx = -offset;
            using (Bitmap tmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g2 = Graphics.FromImage(tmp))
                {
                    g2.Clear(_bg);
                    TextRenderer.DrawText(g2, text, f, new Rectangle(xx, 0, w + 2, r.Height), c, TextFlags());
                    TextRenderer.DrawText(g2, text, f, new Rectangle(xx + w + 40, 0, w + 2, r.Height), c, TextFlags());
                }
                g.DrawImageUnscaled(tmp, r.X, r.Y);
            }
        }

        private void DrawButtons(Graphics g)
        {
            for (int i = 0; i < 5; i++)
            {
                if (_btns[i].Width <= 0 || _btns[i].Height <= 0) continue;
                bool hi = (_hoverBtn == i);
                if (hi)
                {
                    Rectangle hr = _btns[i];
                    Color hc = _layered ? Color.FromArgb(52, _light ? Color.Black : Color.White)
                                        : Mix(_bg, _light ? Color.Black : Color.White, 0.10f);
                    using (GraphicsPath p = Round(hr, 5))
                    using (SolidBrush b = new SolidBrush(hc))
                        g.FillPath(b, p);
                }
                bool disabled = false;
                if (i == 0 && !_info.CanPrev) disabled = true;
                if (i == 2 && !_info.CanNext) disabled = true;
                if (i == 1 && !_info.HasSession) disabled = true;

                Color c;
                if (disabled) c = _layered ? Color.FromArgb(90, _fg) : Mix(_bg, _fg, 0.25f);
                else if (hi) c = _fg;
                else c = _layered ? Color.FromArgb(205, _fg) : Mix(_bg, _fg, 0.85f);
                DrawGlyph(g, i, _btns[i], c);
            }
        }

        private void DrawGlyph(Graphics g, int index, Rectangle r, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float s = Math.Min(r.Width, r.Height) * 0.28f;
            if (index == 3) s = Math.Min(r.Width, r.Height) * 0.22f;
            if (index == 4) s = Math.Min(r.Width, r.Height) * 0.30f;

            using (SolidBrush b = new SolidBrush(c))
            {
                switch (index)
                {
                    case 0:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(cx - s * 0.55f, cy),
                            new PointF(cx + s * 0.55f, cy - s),
                            new PointF(cx + s * 0.55f, cy + s) });
                        g.FillRectangle(b, cx - s * 1.45f, cy - s, s * 0.32f, s * 2f);
                        break;
                    case 1:
                        if (_info.IsPlaying)
                        {
                            g.FillRectangle(b, cx - s * 0.70f, cy - s, s * 0.55f, s * 2f);
                            g.FillRectangle(b, cx + s * 0.15f, cy - s, s * 0.55f, s * 2f);
                        }
                        else
                        {
                            g.FillPolygon(b, new PointF[] {
                                new PointF(cx - s * 0.55f, cy - s),
                                new PointF(cx - s * 0.55f, cy + s),
                                new PointF(cx + s * 0.85f, cy) });
                        }
                        break;
                    case 2:
                        g.FillPolygon(b, new PointF[] {
                            new PointF(cx - s * 0.55f, cy - s),
                            new PointF(cx - s * 0.55f, cy + s),
                            new PointF(cx + s * 0.55f, cy) });
                        g.FillRectangle(b, cx + s * 1.13f, cy - s, s * 0.32f, s * 2f);
                        break;
                    case 3:
                        float dir = _cfg.Collapsed ? -1f : 1f;
                        float ax = cx + dir * s * 0.30f;
                        using (Pen p = new Pen(c, Math.Max(1.5f, s * 0.26f)))
                        {
                            p.StartCap = LineCap.Round;
                            p.EndCap = LineCap.Round;
                            p.LineJoin = LineJoin.Round;
                            g.DrawLines(p, new PointF[] {
                                new PointF(ax + dir * s * 0.45f, cy - s * 0.75f),
                                new PointF(ax - dir * s * 0.15f, cy),
                                new PointF(ax + dir * s * 0.45f, cy + s * 0.75f) });
                        }
                        break;
                    case 4:
                    {
                        // 喇叭
                        g.FillPolygon(b, new PointF[] {
                            new PointF(cx - s * 0.95f, cy - s * 0.32f),
                            new PointF(cx - s * 0.34f, cy - s * 0.32f),
                            new PointF(cx + s * 0.22f, cy - s * 0.85f),
                            new PointF(cx + s * 0.22f, cy + s * 0.85f),
                            new PointF(cx - s * 0.34f, cy + s * 0.32f),
                            new PointF(cx - s * 0.95f, cy + s * 0.32f) });
                        if (_volMuted)
                        {
                            using (Pen p = new Pen(c, Math.Max(1.3f, s * 0.22f)))
                            {
                                p.StartCap = LineCap.Round;
                                p.EndCap = LineCap.Round;
                                g.DrawLine(p, cx + s * 0.45f, cy - s * 0.62f, cx + s * 1.05f, cy + s * 0.62f);
                                g.DrawLine(p, cx + s * 1.05f, cy - s * 0.62f, cx + s * 0.45f, cy + s * 0.62f);
                            }
                        }
                        else
                        {
                            using (Pen p = new Pen(c, Math.Max(1.1f, s * 0.18f)))
                            {
                                p.StartCap = LineCap.Round;
                                p.EndCap = LineCap.Round;
                                g.DrawArc(p, cx - s * 0.15f, cy - s * 0.50f, s * 0.80f, s * 1.00f, -55f, 110f);
                                g.DrawArc(p, cx - s * 0.15f, cy - s * 0.90f, s * 1.60f, s * 1.80f, -55f, 110f);
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void DrawProgress(Graphics g)
        {
            if (!_cfg.ShowProgress) return;
            double frac = 0;
            if (_scrubbing) frac = _scrubRatio;
            else if (_info != null && _info.Duration > TimeSpan.Zero)
                frac = _info.EstimatedPosition().TotalSeconds / _info.Duration.TotalSeconds;
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;

            int h = _hoverProgress || _scrubbing ? 4 : 3;
            int y = _height - h;
            using (SolidBrush b = new SolidBrush(Mix(_bg, _fg, 0.20f)))
                g.FillRectangle(b, 0, y, Width, h);
            int pw = (int)Math.Round(Width * frac);
            if (pw > 0)
            {
                using (SolidBrush b = new SolidBrush(_accent))
                    g.FillRectangle(b, 0, y, pw, h);
            }
            if (_scrubbing)
            {
                using (SolidBrush b = new SolidBrush(_fg))
                    g.FillEllipse(b, pw - 4, y - 3, 8, h + 6);
            }
        }

        private static string Fmt(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }

        // ---------------- 交互 ----------------

        private int HitButton(Point p)
        {
            for (int i = 0; i < 5; i++)
                if (_btns[i].Width > 0 && _btns[i].Contains(p)) return i;
            return -1;
        }

        private bool ProgressHit(Point p)
        {
            // 只有播放器自己声明支持定位（SMTC 的 IsPlaybackPositionEnabled）才让进度条可拖。
            // QQ 音乐实测 CanSeek=False：TryChangePlaybackPositionAsync 返回 true 但位置不变，
            // 所以此时把这条 8px 让给「拖动窗口」，避免点了没反应。
            bool can = _info == null || _info.CanSeek;
            return _cfg.ShowProgress && can && p.Y >= _height - 8;
        }

        private void UpdateScrub(int x)
        {
            double f = (double)x / Math.Max(1, Width);
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            _scrubRatio = f;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                int dx = e.X - _dragOrigin.X;
                int dy = e.Y - _dragOrigin.Y;
                _dragOrigin = e.Location;
                _cfg.PosX += dx;
                _cfg.OffsetY += dy;
                TaskbarInfo ti = Taskbar.Probe();
                Place(ti);
                return;
            }
            if (_volDrag)
            {
                int d = (e.X - _volDragX) / 2;
                if (Math.Abs(e.X - _volDragX) >= 3) _volDragMoved = true;
                int v = _volDragStart + d;
                if (v < 0) v = 0;
                if (v > 100) v = 100;
                if (v != _volPercent)
                {
                    if (_volMuted && v > 0) { Audio.SetMute(false); _volMuted = false; }
                    Audio.SetVolume(v);
                    _volPercent = v;
                    _cfg.Volume = v;
                }
                _volShowUntil = Environment.TickCount + 1500;
                Invalidate();
                return;
            }
            if (_scrubbing) { UpdateScrub(e.X); Invalidate(); return; }

            int b = HitButton(e.Location);
            bool pr = ProgressHit(e.Location);
            if (b != _hoverBtn || pr != _hoverProgress)
            {
                _hoverBtn = b;
                _hoverProgress = pr;
                UpdateCursor();
                Invalidate();
            }
        }

        private void UpdateCursor()
        {
            try
            {
                Cursor = (_hoverBtn >= 0 || _hoverProgress) ? Cursors.Hand
                    : (_cfg.Locked ? Cursors.Default : Cursors.SizeAll);
            }
            catch (Exception) { }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging || _scrubbing) return;
            _hoverBtn = -1;
            _hoverProgress = false;
            UpdateCursor();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                ShowMenu();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_hiddenBySystem) return;

            int b = HitButton(e.Location);
            if (b >= 0)
            {
                if (b == 4)
                {
                    // 音量键：拖动调音量，单击静音
                    _pressedBtn = -1;
                    _volDrag = true;
                    _volDragMoved = false;
                    _volDragX = e.X;
                    _volDragStart = _volPercent < 0 ? 50 : _volPercent;
                    _swallowClick = true;
                    try { Capture = true; } catch (Exception) { }
                    _volShowUntil = Environment.TickCount + 1500;
                    Invalidate();
                    return;
                }
                _pressedBtn = b;
                Invalidate();
                return;
            }
            if (ProgressHit(e.Location))
            {
                _swallowClick = true;
                _scrubbing = true;
                UpdateScrub(e.X);
                Invalidate();
                return;
            }
            if (!_cfg.Locked)
            {
                // 从自动定位切到手动定位：把当前实际位置固化成 PosX
                TaskbarInfo ti = Taskbar.Probe();
                if (_cfg.AutoPlace && ti.Valid && Handle != IntPtr.Zero)
                {
                    RECT rc;
                    if (Native.GetWindowRect(Handle, out rc))
                    {
                        _cfg.AutoPlace = false;
                        _cfg.PosX = rc.Left - ti.Tray.Left;
                        _cfg.OffsetX = 0;
                        _cfg.OffsetY = 0;
                        Log.Write("切换到手动定位: PosX=" + _cfg.PosX);
                    }
                }
                _dragging = true;
                _dragOrigin = e.Location;
                _dragStartOffset = _cfg.OffsetX;
                _swallowClick = true;
                try { Capture = true; } catch (Exception) { }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (_volDrag)
            {
                _volDrag = false;
                _swallowClick = false;
                try { Capture = false; } catch (Exception) { }
                if (!_volDragMoved) { ToggleMute(); return; }
                _volShowUntil = Environment.TickCount + 1200;
                _cfg.Muted = _volMuted;
                _cfg.Save();
                Invalidate();
                return;
            }
            if (_dragging)
            {
                _dragging = false;
                _swallowClick = false;
                try { Capture = false; } catch (Exception) { }
                _cfg.Save();
                PersistMenuChecks();
                return;
            }
            if (_scrubbing)
            {
                _scrubbing = false;
                _swallowClick = false;
                MediaInfo info = _info;
                if (info != null && info.Duration > TimeSpan.Zero)
                {
                    double ms = _scrubRatio * info.Duration.TotalMilliseconds;
                    _worker.Post(MediaCommand.Seek, TimeSpan.FromMilliseconds(ms));
                }
                Invalidate();
                return;
            }
            int b = HitButton(e.Location);
            if (_pressedBtn >= 0 && b == _pressedBtn) DoButton(b);
            _pressedBtn = -1;
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            ToggleCollapse();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // 滚轮：调音量（2% 一档）
            int v = _volPercent < 0 ? Audio.GetVolume() : _volPercent;
            if (v < 0) v = 50;
            v += (e.Delta > 0 ? 2 : -2);
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            if (_volMuted && v > 0) { Audio.SetMute(false); _volMuted = false; }
            Audio.SetVolume(v);
            _volPercent = v;
            _cfg.Volume = v;
            _cfg.Muted = _volMuted;
            _volShowUntil = Environment.TickCount + 1500;
            _cfg.Save();
            Invalidate();
        }

        private void DoButton(int index)
        {
            switch (index)
            {
                case 0: _worker.Post(MediaCommand.Prev); break;
                case 1: _worker.Post(MediaCommand.Toggle); break;
                case 2: _worker.Post(MediaCommand.Next); break;
                case 3: ToggleCollapse(); break;
                case 4: ToggleMute(); break;
            }
        }

        private void ToggleMute()
        {
            _volMuted = !_volMuted;
            Audio.SetMute(_volMuted);
            if (!_volMuted && _volPercent <= 0)
            {
                _volPercent = _cfg.Volume > 0 ? _cfg.Volume : 60;
                Audio.SetVolume(_volPercent);
            }
            _cfg.Muted = _volMuted;
            _volShowUntil = Environment.TickCount + 1500;
            _cfg.Save();
            Invalidate();
        }

        /// <summary>把当前音量状态从系统里读回来（慢速定时器调用）。</summary>
        private void SyncVolume()
        {
            int v = Audio.GetVolume();
            if (v >= 0) _volPercent = v;
            _volMuted = Audio.GetMute();
        }

        private void ToggleCollapse()
        {
            _cfg.Collapsed = !_cfg.Collapsed;
            _mTitle = 0;
            _mSub = 0;
            ApplySize();
            _cfg.Save();
            PersistMenuChecks();
            Invalidate();
        }

        // ---------------- 菜单 / 托盘 ----------------

        private ToolStripMenuItem _miSource;
        private ToolStripMenuItem _miLyrics;
        private ToolStripMenuItem _miKaraoke;
        private ToolStripMenuItem _miProgress;
        private ToolStripMenuItem _miCollapse;
        private ToolStripMenuItem _miLock;
        private ToolStripMenuItem _miEmbed;
        private ToolStripMenuItem _miFullscreen;
        private ToolStripMenuItem _miAutoStart;
        private ToolStripMenuItem _miShow;
        private ToolStripMenuItem _miWidth;
        private ToolStripMenuItem _miFont;
        private ToolStripMenuItem _miGlass;
        private ToolStripMenuItem _miVolume;

        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            _menu.Items.Add(new ToolStripMenuItem("设置…", null, delegate { ShowSettings(); }));
            _menu.Items.Add(new ToolStripSeparator());

            _miSource = new ToolStripMenuItem("播放源");
            _menu.Items.Add(_miSource);
            _menu.Items.Add(new ToolStripSeparator());

            _miShow = new ToolStripMenuItem("显示控件", null, delegate { SetShow(!_cfg.Show); });
            _miCollapse = new ToolStripMenuItem("收起（迷你模式）", null, delegate { ToggleCollapse(); });
            _menu.Items.Add(_miShow);
            _menu.Items.Add(_miCollapse);

            _miWidth = new ToolStripMenuItem("宽度");
            int[] widths = new int[] { 240, 300, 345, 420, 520, 620, 760 };
            foreach (int w in widths)
            {
                int v = w;
                _miWidth.DropDownItems.Add(new ToolStripMenuItem(v + " px", null, delegate
                {
                    _cfg.Width = v;
                    if (_cfg.Collapsed) _cfg.Collapsed = false;
                    ApplySize();
                    _cfg.Save();
                    PersistMenuChecks();
                    Invalidate();
                }));
            }
            _menu.Items.Add(_miWidth);

            _miFont = new ToolStripMenuItem("字号");
            float[] sizes = new float[] { 8f, 9f, 10f, 11f, 12f };
            foreach (float s in sizes)
            {
                float v = s;
                _miFont.DropDownItems.Add(new ToolStripMenuItem(v + " pt", null, delegate
                {
                    _cfg.FontSize = (int)v;
                    MakeFonts();
                    ApplySize();
                    _cfg.Save();
                    PersistMenuChecks();
                    Invalidate();
                }));
            }
            _menu.Items.Add(_miFont);
            _menu.Items.Add(new ToolStripSeparator());

            _miLyrics = new ToolStripMenuItem("显示歌词", null, delegate { _cfg.Lyrics = !_cfg.Lyrics; _cfg.Save(); PersistMenuChecks(); Invalidate(); });
            _miKaraoke = new ToolStripMenuItem("逐字高亮", null, delegate { _cfg.Karaoke = !_cfg.Karaoke; _cfg.Save(); PersistMenuChecks(); Invalidate(); });
            _miProgress = new ToolStripMenuItem("显示进度条", null, delegate { _cfg.ShowProgress = !_cfg.ShowProgress; _cfg.Save(); PersistMenuChecks(); Invalidate(); });
            _miVolume = new ToolStripMenuItem("显示音量", null, delegate
            {
                _cfg.ShowVolume = !_cfg.ShowVolume;
                _cfg.Save();
                ApplySize();
                PersistMenuChecks();
                Invalidate();
            });
            _menu.Items.Add(_miLyrics);
            _menu.Items.Add(_miKaraoke);
            _menu.Items.Add(_miProgress);
            _menu.Items.Add(_miVolume);
            _menu.Items.Add(new ToolStripSeparator());

            _miGlass = new ToolStripMenuItem("毛玻璃材质（重启生效）", null, delegate
            {
                _cfg.Glass = !_cfg.Glass;
                _cfg.Save();
                PersistMenuChecks();
            });
            _menu.Items.Add(_miGlass);

            _miLock = new ToolStripMenuItem("锁定位置", null, delegate { _cfg.Locked = !_cfg.Locked; _cfg.Save(); PersistMenuChecks(); UpdateCursor(); Invalidate(); });
            _miEmbed = new ToolStripMenuItem("嵌入任务栏（实验，重启生效）", null, delegate
            {
                _cfg.Embed = !_cfg.Embed;
                _cfg.Save();
                AttachTaskbar();
                ApplySize();
                PersistMenuChecks();
                Invalidate();
            });
            _miFullscreen = new ToolStripMenuItem("全屏时自动隐藏", null, delegate { _cfg.HideOnFullscreen = !_cfg.HideOnFullscreen; _cfg.Save(); PersistMenuChecks(); });
            _menu.Items.Add(_miLock);
            _menu.Items.Add(_miEmbed);
            _menu.Items.Add(_miFullscreen);
            _menu.Items.Add(new ToolStripMenuItem("恢复默认位置", null, delegate
            {
                _cfg.OffsetX = 0; _cfg.OffsetY = 0; _cfg.Save(); ApplySize(); Invalidate();
            }));
            _menu.Items.Add(new ToolStripSeparator());

            _miAutoStart = new ToolStripMenuItem("开机自动启动", null, delegate
            {
                _cfg.AutoStart = !_cfg.AutoStart;
                Config.SetAutoStart(_cfg.AutoStart);
                _cfg.Save();
                PersistMenuChecks();
            });
            _menu.Items.Add(_miAutoStart);

            _menu.Items.Add(new ToolStripMenuItem("打开 QQ音乐", null, delegate { OpenQqMusic(); }));
            _menu.Items.Add(new ToolStripMenuItem("打开配置文件", null, delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", Config.FilePath); }
                catch (Exception ex) { Log.Write("打开配置失败: " + Log.Describe(ex)); }
            }));
            _menu.Items.Add(new ToolStripMenuItem("刷新", null, delegate { _worker.Post(MediaCommand.Refresh); Invalidate(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("退出", null, delegate { QuitApp(); }));

            _menu.Opening += delegate { RebuildSourceMenu(); PersistMenuChecks(); };
            ContextMenuStrip = _menu;
        }

        private void SetShow(bool on)
        {
            _cfg.Show = on;
            _hiddenBySystem = !on;
            Visible = on;
            _cfg.Save();
            PersistMenuChecks();
        }

        private void RebuildSourceMenu()
        {
            _miSource.DropDownItems.Clear();
            bool auto = string.IsNullOrEmpty(_cfg.SourceAppId);
            _miSource.DropDownItems.Add(new ToolStripMenuItem("自动（跟随正在播放的）", null, delegate
            {
                _cfg.SourceAppId = "";
                _worker.SourceAppId = "";
                _cfg.Save();
                _worker.Post(MediaCommand.Refresh);
                PersistMenuChecks();
            })
            { Checked = auto });

            List<MediaSource> srcs = _worker.Sources;
            if (srcs.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("（暂无正在播放的会话）");
                none.Enabled = false;
                _miSource.DropDownItems.Add(none);
                return;
            }
            foreach (MediaSource s in srcs)
            {
                string appId = s.AppId;
                string label = s.ToString();
                if (label.Length > 60) label = label.Substring(0, 60);
                _miSource.DropDownItems.Add(new ToolStripMenuItem(label + (s.IsPlaying ? "  ▶" : ""), null, delegate
                {
                    _cfg.SourceAppId = appId;
                    _worker.SourceAppId = appId;
                    _cfg.Save();
                    _worker.Post(MediaCommand.Refresh);
                    PersistMenuChecks();
                })
                { Checked = !auto && string.Equals(appId, _cfg.SourceAppId, StringComparison.OrdinalIgnoreCase) });
            }
        }

        private void PersistMenuChecks()
        {
            if (_miLyrics == null) return;
            _miLyrics.Checked = _cfg.Lyrics;
            _miKaraoke.Checked = _cfg.Karaoke;
            _miProgress.Checked = _cfg.ShowProgress;
            _miCollapse.Checked = _cfg.Collapsed;
            _miLock.Checked = _cfg.Locked;
            _miEmbed.Checked = _cfg.Embed;
            _miFullscreen.Checked = _cfg.HideOnFullscreen;
            _miShow.Checked = _cfg.Show;
            if (_miGlass != null) _miGlass.Checked = _cfg.Glass;
            if (_miVolume != null) _miVolume.Checked = _cfg.ShowVolume;
            _cfg.AutoStart = Config.GetAutoStart();
            _miAutoStart.Checked = _cfg.AutoStart;
            foreach (ToolStripItem it in _miWidth.DropDownItems)
                if (it is ToolStripMenuItem) ((ToolStripMenuItem)it).Checked = (it.Text == _cfg.Width + " px");
            foreach (ToolStripItem it in _miFont.DropDownItems)
                if (it is ToolStripMenuItem) ((ToolStripMenuItem)it).Checked = (it.Text == _cfg.FontSize + " pt");
        }

        private void ShowMenu()
        {
            RebuildSourceMenu();
            PersistMenuChecks();
            Rectangle screen = Screen.FromHandle(Handle).Bounds;
            int x = Math.Min(Cursor.Position.X, screen.Right - _menu.Width);
            int y = Cursor.Position.Y - _menu.Height - 8;
            if (y < screen.Top) y = Cursor.Position.Y + 12;
            _menu.Show(new Point(x, y));
        }

        /// <summary>打开设置页（单例）。</summary>
        private void ShowSettings()
        {
            try
            {
                if (_settings == null || _settings.IsDisposed)
                {
                    _settings = new SettingsForm(_cfg, this);
                    _settings.FormClosed += delegate { _settings = null; };
                    _settings.Show();
                }
                else
                {
                    _settings.Activate();
                }
            }
            catch (Exception ex)
            {
                Log.Write("打开设置失败: " + Log.Describe(ex));
            }
        }

        /// <summary>设置页改了参数之后调这个：重新量字体、重算尺寸与位置、重画。</summary>
        internal void ApplyLive()
        {
            try
            {
                MakeFonts();
                AttachTaskbar();
                _anchorLeft = -1;
                ApplySize();
                Place(Taskbar.Probe());
                RefreshColors();
                PersistMenuChecks();
                Invalidate();
            }
            catch (Exception ex)
            {
                Log.Write("ApplyLive 失败: " + Log.Describe(ex));
            }
        }

        private void BuildTrayIcon()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "QmBar";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { SetShow(!_cfg.Show); };
        }

        private static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(49, 194, 124)))
                        g.FillEllipse(b, 0, 0, 31, 31);
                    DrawNote(g, new Rectangle(4, 4, 24, 24), Color.White);
                }
                IntPtr h = bmp.GetHicon();
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                return ic;
            }
        }

        private void OpenQqMusic()
        {
            string[] candidates = new string[]
            {
                @"C:\Program Files (x86)\Tencent\QQMusic\QQMusic.exe",
                @"C:\Program Files\Tencent\QQMusic\QQMusic.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Tencent\QQMusic\QQMusic.exe")
            };
            foreach (string p in candidates)
            {
                try
                {
                    if (System.IO.File.Exists(p)) { System.Diagnostics.Process.Start(p); return; }
                }
                catch (Exception) { }
            }
            try { System.Diagnostics.Process.Start("QQMusic.exe"); }
            catch (Exception ex) { Log.Write("打开 QQ音乐失败: " + Log.Describe(ex)); }
        }

        private void QuitApp()
        {
            try { _cfg.Save(); } catch (Exception) { }
            try { _worker.Stop(); } catch (Exception) { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch (Exception) { }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _cfg.Save(); } catch (Exception) { }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null) _uiTimer.Dispose();
                if (_slowTimer != null) _slowTimer.Dispose();
                if (_fTitle != null) _fTitle.Dispose();
                if (_fSub != null) _fSub.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
