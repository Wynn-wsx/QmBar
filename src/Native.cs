using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace QmBar
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }

        public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }

        public override string ToString()
        {
            return Left + "," + Top + " - " + Right + "," + Bottom + " (" + Width + "x" + Height + ")";
        }
    }

    internal static class Native
    {
        // ---- 窗口样式 ----
        public const int WS_CHILD = 0x40000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_EX_TOOLWINDOW = 0x00000080;   // 不进 Alt+Tab
        public const int WS_EX_NOACTIVATE = 0x08000000;   // 点击不抢焦点
        public const int WS_EX_TOPMOST = 0x00000008;

        // ---- SetWindowPos ----
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;

        // SetWindowPos 的 hWndInsertAfter 特殊值：IntPtr.Zero == HWND_TOP，-1 == HWND_TOPMOST
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        // 分层窗口（毛玻璃逐像素 alpha）
        public const int WS_EX_LAYERED = 0x00080000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWNA = 8;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern int GetCurrentProcessId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public const uint GW_HWNDPREV = 3;
        public const uint GW_HWNDNEXT = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // Win10 1703+：PerMonitorV2，必须在创建任何窗口前调用
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("shcore.dll")]
        public static extern int SetProcessDpiAwareness(int value);

        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return;
            }
            catch (Exception) { }
            try { SetProcessDpiAwareness(2); }   // PROCESS_PER_MONITOR_DPI_AWARE
            catch (Exception) { }
        }
    }

    /// <summary>任务栏宿主信息：句柄、任务栏矩形、可用的空白槽位（全部为屏幕坐标）。</summary>
    internal sealed class TaskbarInfo
    {
        public IntPtr Handle = IntPtr.Zero;
        public RECT Tray;
        public RECT Slot;
        public bool Valid;
        public bool AutoHidden;

        public override string ToString()
        {
            return "tray=" + Handle + " [" + Tray + "] slot=[" + Slot + "] valid=" + Valid;
        }
    }

    internal static class Taskbar
    {
        private static IntPtr _cachedTray = IntPtr.Zero;
        private static Native.EnumWindowsProc _enumProc;   // 防止委托被 GC

        public static IntPtr Handle
        {
            get
            {
                if (_cachedTray == IntPtr.Zero || !Native.IsWindow(_cachedTray))
                {
                    _cachedTray = Native.FindWindow("Shell_TrayWnd", null);
                    if (_cachedTray == IntPtr.Zero)
                        _cachedTray = Native.FindWindow("Shell_SecondaryTrayWnd", null);
                }
                return _cachedTray;
            }
        }

        /// <summary>
        /// 探测任务栏布局，算出留给我们的空白槽位。
        /// 思路：槽位 = [任务列表右边缘 .. 托盘区左边缘]，避开覆盖整条任务栏的 XAML 岛。
        /// </summary>
        public static TaskbarInfo Probe()
        {
            TaskbarInfo ti = new TaskbarInfo();
            IntPtr tray = Handle;
            ti.Handle = tray;
            if (tray == IntPtr.Zero) return ti;

            RECT tr;
            if (!Native.GetWindowRect(tray, out tr)) return ti;
            ti.Tray = tr;

            System.Windows.Forms.Screen scr = System.Windows.Forms.Screen.FromHandle(tray);
            ti.AutoHidden = (tr.Top >= scr.Bounds.Bottom - 2) || (tr.Bottom <= scr.Bounds.Top + 2);

            // 托盘区左边缘 = 槽位右边界
            int slotRight = tr.Right - 8;
            IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT nr;
            if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out nr))
                slotRight = nr.Left;

            // 槽位左边界 = 任务栏里最右侧的“左侧内容”（任务列表）的右边缘
            int usedRight = tr.Left;
            int selfPid = Native.GetCurrentProcessId();
            _enumProc = delegate(IntPtr h, IntPtr p)
            {
                if (h == IntPtr.Zero) return true;

                // 关键：插件自己也是任务栏的子窗口。不排除自己的话，下一轮探测会把
                // 自己算成“已占用区域”→ 槽位宽度变 0 → Valid=False → 隐藏 → 再探测又有效，
                // 形成 1.5 秒一次的显示/隐藏振荡。
                int wpid;
                Native.GetWindowThreadProcessId(h, out wpid);
                if (wpid == selfPid) return true;

                StringBuilder sb = new StringBuilder(256);
                Native.GetClassName(h, sb, 256);
                string cn = sb.ToString();
                // XAML 岛覆盖整条任务栏、托盘区是右边界，两者都要排除
                if (cn == "Windows.UI.Composition.DesktopWindowContentBridge") return true;
                if (cn == "TrayNotifyWnd") return true;
                if (!Native.IsWindowVisible(h)) return true;

                RECT r;
                if (!Native.GetWindowRect(h, out r)) return true;
                if (r.IsEmpty) return true;
                if (r.Left < tr.Left - 2) return true;
                if (r.Right > slotRight) return true;          // 跨过托盘区的一律忽略
                if (r.Top < tr.Top - 2 || r.Bottom > tr.Bottom + 2) return true;
                if (r.Right > usedRight) usedRight = r.Right;
                return true;
            };
            Native.EnumChildWindows(tray, _enumProc, IntPtr.Zero);
            _enumProc = null;

            RECT slot;
            slot.Left = usedRight + 6;
            slot.Top = tr.Top;
            slot.Right = slotRight - 6;
            slot.Bottom = tr.Bottom;
            ti.Slot = slot;
            ti.Valid = (!ti.AutoHidden) && (!slot.IsEmpty) && (slot.Width >= 120);
            return ti;
        }

        /// <summary>抓任务栏空白处的一个像素作为背景色，让控件真正“融”进任务栏（跟随深浅色主题）。</summary>
        public static Color SampleBackground()
        {
            try
            {
                TaskbarInfo ti = Probe();
                if (ti.Handle == IntPtr.Zero || ti.AutoHidden) return Color.FromArgb(32, 32, 32);
                int x = ti.Slot.Left + 4;
                int y = ti.Tray.Top + ti.Tray.Height / 2;
                if (x >= ti.Slot.Right) x = ti.Tray.Left + ti.Tray.Width / 2;
                if (y < 0) y = 0;
                using (Bitmap bmp = new Bitmap(1, 1))
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                    }
                    Color c = bmp.GetPixel(0, 0);
                    // new Bitmap 初值是 A=0 的全透明像素：如果 CopyFromScreen 没真正落笔，
                    // 这里拿到的 alpha 会是 0，而 Control.BackColor 不接受透明色（会抛 ArgumentException）。
                    if (c.A < 255) return Color.FromArgb(32, 32, 32);
                    return Color.FromArgb(255, c.R, c.G, c.B);
                }
            }
            catch (Exception)
            {
                return Color.FromArgb(32, 32, 32);
            }
        }

        /// <summary>前台窗口是否铺满整个显示器（全屏游戏/视频）→ 此时应当隐藏自己。</summary>
        public static bool IsForegroundFullscreen(IntPtr self)
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                if (fg == self || fg == Native.GetShellWindow() || fg == Native.GetDesktopWindow()) return false;
                if (!Native.IsWindowVisible(fg)) return false;
                // 输入法/前台锁之类的辅助窗（ForegroundStaging、IME、wetype 等）经常是
                // 整屏大小的工具窗，会把「全屏」判成假阳性，必须排掉。
                if ((Native.GetWindowLong(fg, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) != 0) return false;

                RECT r;
                if (!Native.GetWindowRect(fg, out r)) return false;

                IntPtr mon = Native.MonitorFromWindow(fg, 2 /*MONITOR_DEFAULTTONEAREST*/);
                Native.MONITORINFO mi = new Native.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
                if (!Native.GetMonitorInfo(mon, ref mi)) return false;

                return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
                    && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
