using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace QmBar
{
    internal sealed class Config
    {
        // ---- 位置与外观 ----
        public int OffsetX = 0;               // 相对“贴紧托盘区左侧”的横向微调（拖拽会改这里）
        public int OffsetY = 0;
        public int Width = 345;               // 展开态宽度
        public int CollapsedWidth = 100;      // 收起态宽度（几何下限：封面 30 + 播放 28 + 尖角 16 + 边距）
        public bool Collapsed = false;
        public int Opacity = 35;              // 毛玻璃面板不透明度（15=很透，100=不透明）
        public bool Glass = true;             // 毛玻璃材质（分层窗口 + 逐像素 alpha）
        public bool Locked = false;           // 锁定位置，防止误拖
        // ---- 位置 ----
        // AutoPlace=true：自动贴靠任务栏空白槽位右端；false：用 PosX 手动摆放（PosX 是相对任务栏左边缘）
        public bool AutoPlace = true;
        public int PosX = 0;
        // 默认 false：Win11 任务栏的 XAML 岛是 DirectComposition 层，不受同级 Win32 z 序约束，
        // 子窗口嵌进去会被整片盖住（PrintWindow 有内容、屏幕上看不到）。true 仅供实验。
        public bool Embed = false;

        // ---- 行为 ----
        public string SourceAppId = "";       // "" = 自动挑选播放源
        public bool Lyrics = true;
        public bool Karaoke = true;           // 逐字高亮
        public bool HideOnFullscreen = true;
        public bool AutoStart = false;
        public bool ShowProgress = true;
        public bool ShowVolume = true;        // 显示音量按钮
        public int Volume = 100;              // 上次音量（0..100），仅用于显示与恢复
        public bool Muted = false;
        public bool Show = true;
        public int FontSize = 9;
        public int PollMs = 1000;

        public static string Dir
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(baseDir, "QmBar"), "");
            }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    c.Set(k, v);
                }
            }
            catch (Exception) { }
            return c;
        }

        private void Set(string k, string v)
        {
            string key = k.ToLowerInvariant();
            switch (key)
            {
                case "offsetx": OffsetX = Clamp(Int(v, OffsetX), -2000, 2000); break;
                case "offsety": OffsetY = Clamp(Int(v, OffsetY), -30, 30); break;

                case "width": Width = Clamp(Int(v, Width), 200, 1400); break;
                case "collapsedwidth": CollapsedWidth = Clamp(Int(v, CollapsedWidth), 88, 400); break;
                case "collapsed": Collapsed = Bool(v, Collapsed); break;
                case "opacity": Opacity = Clamp(Int(v, Opacity), 15, 100); break;
                case "glass": Glass = Bool(v, Glass); break;
                case "autoplace": AutoPlace = Bool(v, AutoPlace); break;
                case "posx": PosX = Int(v, PosX); break;
                case "locked": Locked = Bool(v, Locked); break;
                case "embed": Embed = Bool(v, Embed); break;
                case "sourceappid": SourceAppId = v; break;
                case "lyrics": Lyrics = Bool(v, Lyrics); break;
                case "karaoke": Karaoke = Bool(v, Karaoke); break;
                case "hideonfullscreen": HideOnFullscreen = Bool(v, HideOnFullscreen); break;
                case "autostart": AutoStart = Bool(v, AutoStart); break;
                case "showprogress": ShowProgress = Bool(v, ShowProgress); break;
                case "showvolume": ShowVolume = Bool(v, ShowVolume); break;
                case "volume": Volume = Clamp(Int(v, Volume), 0, 100); break;
                case "muted": Muted = Bool(v, Muted); break;
                case "show": Show = Bool(v, Show); break;
                case "fontsize": FontSize = Clamp(Int(v, FontSize), 7, 16); break;
                case "pollms": PollMs = Clamp(Int(v, PollMs), 200, 5000); break;
            }
        }

        private static int Int(string s, int def)
        {
            int r;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return def;
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        private static bool Bool(string s, bool def)
        {
            if (s == null) return def;
            string t = s.Trim().ToLowerInvariant();
            if (t == "1" || t == "true" || t == "yes" || t == "on") return true;
            if (t == "0" || t == "false" || t == "no" || t == "off") return false;
            return def;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# QmBar 配置文件（改动后重启插件生效；拖拽移动会即时写回）");
                sb.AppendLine("# 位置：AutoPlace=1 自动贴靠任务栏空白区；AutoPlace=0 时用 PosX（相对任务栏左边缘的像素）");
                sb.AppendLine("# OffsetX/OffsetY 是叠加在自动位置上的微调");
                sb.AppendLine("AutoPlace=" + (AutoPlace ? "1" : "0"));
                sb.AppendLine("PosX=" + PosX);
                sb.AppendLine("OffsetX=" + OffsetX);
                sb.AppendLine("OffsetY=" + OffsetY);
                sb.AppendLine("Width=" + Width);
                sb.AppendLine("CollapsedWidth=" + CollapsedWidth);
                sb.AppendLine("Collapsed=" + (Collapsed ? "1" : "0"));
                sb.AppendLine("# Opacity：毛玻璃面板不透明度，30 很透 / 100 不透明");
                sb.AppendLine("Opacity=" + Opacity);
                sb.AppendLine("Glass=" + (Glass ? "1" : "0"));
                sb.AppendLine("Locked=" + (Locked ? "1" : "0"));
                sb.AppendLine("Embed=" + (Embed ? "1" : "0"));
                sb.AppendLine("SourceAppId=" + SourceAppId);
                sb.AppendLine("Lyrics=" + (Lyrics ? "1" : "0"));
                sb.AppendLine("Karaoke=" + (Karaoke ? "1" : "0"));
                sb.AppendLine("HideOnFullscreen=" + (HideOnFullscreen ? "1" : "0"));
                sb.AppendLine("AutoStart=" + (AutoStart ? "1" : "0"));
                sb.AppendLine("ShowProgress=" + (ShowProgress ? "1" : "0"));
                sb.AppendLine("ShowVolume=" + (ShowVolume ? "1" : "0"));
                sb.AppendLine("Volume=" + Volume);
                sb.AppendLine("Muted=" + (Muted ? "1" : "0"));
                sb.AppendLine("Show=" + (Show ? "1" : "0"));
                sb.AppendLine("FontSize=" + FontSize);
                sb.AppendLine("PollMs=" + PollMs);

                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception) { }
        }

        // ---- 开机自启 ----
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "QmBar";

        public static string ExePath()
        {
            try
            {
                System.Reflection.Assembly a = System.Reflection.Assembly.GetEntryAssembly();
                if (a != null && a.Location != null && a.Location.Length > 0) return a.Location;
            }
            catch (Exception) { }
            return System.Windows.Forms.Application.ExecutablePath;
        }

        public static bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(RunName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        public static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + ExePath() + "\" --autostart");
                    else if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
                }
            }
            catch (Exception) { }
        }
    }
}
