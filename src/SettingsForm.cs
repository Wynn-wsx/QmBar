using System;
using System.Drawing;
using System.Windows.Forms;

namespace QmBar
{
    /// <summary>
    /// 设置页。所有改动即时生效并写入 config.ini（没有「确定/取消」，
    /// 因为控件在任务栏上实时可见，所见即所得更直观）。
    /// 位置这一项可以横跨整条任务栏：关掉「自动贴靠任务栏」后，
    /// 左边缘 X 的取值范围就是整个任务栏宽度。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly Config _cfg;
        private readonly BarForm _owner;

        private CheckBox _cbGlass;
        private TrackBar _tbOpacity;
        private Label _lbOpacity;
        private TrackBar _tbWidth;
        private Label _lbWidth;
        private TrackBar _tbFont;
        private Label _lbFont;
        private CheckBox _cbVolume;
        private CheckBox _cbLyrics;
        private CheckBox _cbKaraoke;
        private CheckBox _cbProgress;
        private CheckBox _cbFullscreen;
        private CheckBox _cbLock;
        private CheckBox _cbAutoStart;
        private CheckBox _cbAutoPlace;
        private TrackBar _tbPos;
        private Label _lbPos;
        private NumericUpDown _numY;
        private bool _loading = true;

        public SettingsForm(Config cfg, BarForm owner)
        {
            _cfg = cfg;
            _owner = owner;

            Text = "QmBar 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(410, 516);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(250, 250, 252);

            int y = 10;

            Head("外观", y); y += 22;
            _cbGlass = Check("毛玻璃材质（重启后生效）", 20, y, _cfg.Glass, delegate
            {
                _cfg.Glass = _cbGlass.Checked;
                Apply();
            });
            y += 28;
            _tbOpacity = Slider("面板不透明度", y, 15, 100, _cfg.Opacity, out _lbOpacity, delegate
            {
                _cfg.Opacity = _tbOpacity.Value;
                _lbOpacity.Text = _tbOpacity.Value.ToString() + "%";
                Apply();
            });
            y += 42;
            _tbWidth = Slider("面板宽度", y, 200, 900, _cfg.Width, out _lbWidth, delegate
            {
                _cfg.Width = _tbWidth.Value;
                if (_cfg.Collapsed) _cfg.Collapsed = false;
                _lbWidth.Text = _tbWidth.Value.ToString() + " px";
                Apply();
            });
            y += 42;
            _tbFont = Slider("字号", y, 7, 16, _cfg.FontSize, out _lbFont, delegate
            {
                _cfg.FontSize = _tbFont.Value;
                _lbFont.Text = _tbFont.Value.ToString() + " pt";
                Apply();
            });
            y += 42;
            _cbVolume = Check("显示音量图标", 20, y, _cfg.ShowVolume, delegate
            {
                _cfg.ShowVolume = _cbVolume.Checked;
                Apply();
            });
            y += 32;

            Head("位置", y); y += 22;
            _cbAutoPlace = Check("自动贴靠任务栏托盘左侧", 20, y, _cfg.AutoPlace, delegate
            {
                _cfg.AutoPlace = _cbAutoPlace.Checked;
                if (_cfg.AutoPlace) _cfg.OffsetX = 0;
                _tbPos.Enabled = !_cfg.AutoPlace;
                Apply();
            });
            y += 28;

            TaskbarInfo ti = Taskbar.Probe();
            int maxX = ti.Valid ? Math.Max(200, ti.Tray.Width) : 1920;
            int curX = _cfg.PosX;
            if (curX < 0) curX = 0;
            if (curX > maxX) curX = maxX;
            _tbPos = Slider("左边缘 X", y, 0, maxX, curX, out _lbPos, delegate
            {
                _cfg.AutoPlace = false;
                _cbAutoPlace.Checked = false;
                _tbPos.Enabled = true;
                _cfg.PosX = _tbPos.Value;
                _lbPos.Text = _tbPos.Value.ToString() + " px";
                Apply();
            });
            _tbPos.Enabled = !_cfg.AutoPlace;
            y += 34;

            Label hint = new Label();
            hint.Text = "范围：0 ~ " + maxX + " px（整条任务栏，共 " + (ti.Valid ? ti.Tray.Width : 0) + " px）";
            hint.Location = new Point(120, y);
            hint.Size = new Size(280, 18);
            hint.ForeColor = Color.FromArgb(120, 120, 128);
            Controls.Add(hint);
            y += 22;

            Label ycap = new Label();
            ycap.Text = "垂直微调 Y";
            ycap.Location = new Point(20, y + 3);
            ycap.Size = new Size(96, 20);
            Controls.Add(ycap);
            _numY = new NumericUpDown();
            _numY.Minimum = -30;
            _numY.Maximum = 30;
            _numY.Value = Math.Max(-30, Math.Min(30, _cfg.OffsetY));
            _numY.Location = new Point(120, y);
            _numY.Size = new Size(70, 24);
            _numY.ValueChanged += delegate
            {
                _cfg.OffsetY = (int)_numY.Value;
                Apply();
            };
            Controls.Add(_numY);
            y += 34;

            Head("功能", y); y += 22;
            _cbLyrics = Check("显示歌词", 20, y, _cfg.Lyrics, delegate { _cfg.Lyrics = _cbLyrics.Checked; Apply(); });
            _cbKaraoke = Check("逐字高亮", 210, y, _cfg.Karaoke, delegate { _cfg.Karaoke = _cbKaraoke.Checked; Apply(); });
            y += 26;
            _cbProgress = Check("显示进度条", 20, y, _cfg.ShowProgress, delegate { _cfg.ShowProgress = _cbProgress.Checked; Apply(); });
            _cbFullscreen = Check("全屏时自动隐藏", 210, y, _cfg.HideOnFullscreen, delegate { _cfg.HideOnFullscreen = _cbFullscreen.Checked; Apply(); });
            y += 26;
            _cbLock = Check("锁定位置", 20, y, _cfg.Locked, delegate { _cfg.Locked = _cbLock.Checked; Apply(); });
            _cbAutoStart = Check("开机自动启动", 210, y, Config.GetAutoStart(), delegate
            {
                Config.SetAutoStart(_cbAutoStart.Checked);
                _cfg.AutoStart = _cbAutoStart.Checked;
                Apply();
            });
            y += 34;

            Button bReset = new Button();
            bReset.Text = "恢复默认";
            bReset.Location = new Point(20, y);
            bReset.Size = new Size(90, 28);
            bReset.Click += delegate { Reset(); };
            Controls.Add(bReset);

            Button bCfg = new Button();
            bCfg.Text = "打开配置文件";
            bCfg.Location = new Point(120, y);
            bCfg.Size = new Size(104, 28);
            bCfg.Click += delegate
            {
                try { System.Diagnostics.Process.Start("notepad.exe", Config.FilePath); }
                catch (Exception ex) { Log.Write("打开配置失败: " + Log.Describe(ex)); }
            };
            Controls.Add(bCfg);

            Button bClose = new Button();
            bClose.Text = "关闭";
            bClose.Location = new Point(300, y);
            bClose.Size = new Size(90, 28);
            bClose.Click += delegate { Close(); };
            Controls.Add(bClose);

            _loading = false;
        }

        private Label Head(string text, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(12, y);
            l.Size = new Size(380, 20);
            l.Font = new Font(Font, FontStyle.Bold);
            l.ForeColor = Color.FromArgb(70, 70, 78);
            Controls.Add(l);
            return l;
        }

        private CheckBox Check(string text, int x, int y, bool val, EventHandler on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = new Point(x, y);
            c.Size = new Size(186, 22);
            c.Checked = val;
            c.CheckedChanged += on;
            Controls.Add(c);
            return c;
        }

        private TrackBar Slider(string caption, int y, int min, int max, int val, out Label valueLabel, EventHandler on)
        {
            Label cap = new Label();
            cap.Text = caption;
            cap.Location = new Point(20, y + 6);
            cap.Size = new Size(96, 20);
            Controls.Add(cap);

            TrackBar tb = new TrackBar();
            tb.Minimum = min;
            tb.Maximum = max;
            if (val < min) val = min;
            if (val > max) val = max;
            tb.Value = val;
            tb.TickFrequency = Math.Max(1, (max - min) / 8);
            tb.SmallChange = 1;
            tb.LargeChange = Math.Max(1, (max - min) / 10);
            tb.Location = new Point(118, y);
            tb.Size = new Size(206, 30);
            Controls.Add(tb);

            Label lb = new Label();
            lb.Text = val.ToString();
            lb.Location = new Point(328, y + 6);
            lb.Size = new Size(70, 20);
            Controls.Add(lb);

            valueLabel = lb;
            tb.ValueChanged += on;
            return tb;
        }

        /// <summary>把改动推给主控件并落盘。</summary>
        private void Apply()
        {
            if (_loading) return;
            try
            {
                _cfg.Save();
                _owner.ApplyLive();
            }
            catch (Exception ex)
            {
                Log.Write("应用设置失败: " + Log.Describe(ex));
            }
        }

        private void Reset()
        {
            _loading = true;
            _cfg.OffsetX = 0;
            _cfg.OffsetY = 0;
            _cfg.AutoPlace = true;
            _cfg.PosX = 0;
            _cfg.Glass = true;
            _cfg.Opacity = 35;
            _cfg.Width = 345;
            _cfg.FontSize = 9;
            _cfg.ShowVolume = true;
            _cfg.Lyrics = true;
            _cfg.Karaoke = true;
            _cfg.ShowProgress = true;
            _cfg.HideOnFullscreen = true;
            _cfg.Locked = false;
            _loading = false;

            _cbGlass.Checked = true;
            _tbOpacity.Value = 35;
            _tbWidth.Value = 345;
            _tbFont.Value = 9;
            _cbVolume.Checked = true;
            _cbAutoPlace.Checked = true;
            _tbPos.Value = 0;
            _tbPos.Enabled = false;
            _numY.Value = 0;
            _cbLyrics.Checked = true;
            _cbKaraoke.Checked = true;
            _cbProgress.Checked = true;
            _cbFullscreen.Checked = true;
            _cbLock.Checked = false;

            try
            {
                Audio.SetVolume(100);
                Audio.SetMute(false);
            }
            catch (Exception) { }

            Apply();
        }
    }
}
