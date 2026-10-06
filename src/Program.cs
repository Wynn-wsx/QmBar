using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QmBar
{
    internal static class Log
    {
        private static readonly object Lock = new object();

        public static string LogPath
        {
            get { return Path.Combine(Config.Dir, "QmBar.log"); }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(Config.Dir);
                    if (File.Exists(LogPath))
                    {
                        FileInfo fi = new FileInfo(LogPath);
                        if (fi.Length > 256 * 1024) File.Delete(LogPath);
                    }
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch (Exception) { }
        }

        public static string Describe(Exception ex)
        {
            if (ex == null) return "(null)";
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }
    }

    internal static class Program
    {
        private const string MutexName = "QmBar_SingleInstance_7F2A4C";

        [STAThread]
        private static void Main(string[] args)
        {
            bool replace = false;
            foreach (string a in args)
                if (string.Equals(a, "--replace", StringComparison.OrdinalIgnoreCase)) replace = true;

            Native.EnableDpiAwareness();

            // 单实例：资源管理器重启后自重启时，等旧进程退出把锁让出来
            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                int waited = 0;
                while (waited < 6000 && !created)
                {
                    try
                    {
                        if (mutex.WaitOne(300)) created = true;
                    }
                    catch (AbandonedMutexException) { created = true; }
                    waited += 300;
                }
                if (!created)
                {
                    if (!replace) Log.Write("已有实例在运行，本次启动退出");
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Write("UI 线程异常: " + e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("未处理异常: " + Log.Describe(e.ExceptionObject as Exception));
            };

            Config cfg = Config.Load();
            cfg.AutoStart = Config.GetAutoStart();

            Log.Write("启动 QmBar v1.0  (" + Environment.OSVersion.Version + ", " +
                (Environment.Is64BitProcess ? "x64" : "x86") + ", DPI=" + cfg.FontSize + ")");

            TaskbarInfo ti = Taskbar.Probe();
            Log.Write("任务栏: " + ti);

            MediaWorker worker = new MediaWorker();
            worker.Init();
            worker.SourceAppId = cfg.SourceAppId;
            if (!worker.Ready) Log.Write("GSMTC 初始化失败（将显示提示）: " + worker.LastError);
            worker.Start();

            BarForm form = new BarForm(cfg, worker);
            Application.Run(form);

            worker.Stop();
            try { mutex.ReleaseMutex(); } catch (Exception) { }
        }
    }
}
