using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

// 目的：验证在「无 Windows SDK / 无 Windows.winmd」的机器上，
// 用 in-box csc.exe 编译的 .NET Framework 程序能否通过纯反射调用 GSMTC。
internal static class SmtcProbe
{
    private static MethodInfo _asTask;

    // WinRT 类型的完整限定名必须带上「契约程序集名」，例如：
    //   Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,
    //   Windows.Media.Control,
    //   ContentType=WindowsRuntime
    // 少了中间那段会被 CLR 当成普通程序集名，抛 FileLoadException 0x80131047。
    private static Type WinRt(string typeName, string contractAssembly)
    {
        string full = typeName + ", " + contractAssembly + ", ContentType=WindowsRuntime";
        Type t = Type.GetType(full);
        if (t == null) throw new Exception("WinRT type not found: " + full);
        return t;
    }

    private static MethodInfo AsTaskGeneric()
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
            if (_asTask == null) throw new Exception("AsTask<T>(IAsyncOperation<T>) not found");
        }
        return _asTask;
    }

    private static object Await(object op, Type resultType)
    {
        MethodInfo g = AsTaskGeneric().MakeGenericMethod(resultType);
        object task = g.Invoke(null, new object[] { op });
        ((Task)task).Wait(-1);
        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static MethodInfo FindAsyncMethod(Type t, string name)
    {
        MethodInfo mi = t.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
        if (mi == null) throw new Exception("method not found: " + name + " on " + t.FullName);
        return mi;
    }

    private static void Main()
    {
        try
        {
            Console.WriteLine("== step1: resolve WinRT types ==");
            Type tMgr = WinRt("Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager", "Windows.Media.Control");
            Console.WriteLine("  manager type = " + tMgr.FullName + "  asm=" + tMgr.Assembly.GetName().Name);

            Console.WriteLine("== step2: RequestAsync ==");
            MethodInfo req = tMgr.GetMethod("RequestAsync", BindingFlags.Public | BindingFlags.Static);
            Type mgrType = req.ReturnType.GetGenericArguments()[0];
            Console.WriteLine("  result type = " + mgrType.FullName);
            object mgr = Await(req.Invoke(null, null), mgrType);
            Console.WriteLine("  manager instance = " + (mgr == null ? "NULL" : "OK"));

            Console.WriteLine("== step3: enumerate sessions ==");
            object sessionsObj = tMgr.GetMethod("GetSessions").Invoke(mgr, null);
            int n = 0;
            foreach (object s in (IEnumerable)sessionsObj)
            {
                n++;
                Type st = s.GetType();
                string appId = (string)st.GetProperty("SourceAppUserModelId").GetValue(s, null);
                Console.WriteLine("  --- session " + n + ": " + appId);
                Console.WriteLine("      projected type = " + st.FullName + "  asm=" + st.Assembly.GetName().Name);

                // playback info
                object pb = st.GetMethod("GetPlaybackInfo").Invoke(s, null);
                object status = pb.GetType().GetProperty("PlaybackStatus").GetValue(pb, null);
                Console.WriteLine("      status = " + status);

                // timeline
                object tl = st.GetMethod("GetTimelineProperties").Invoke(s, null);
                object pos = tl.GetType().GetProperty("Position").GetValue(tl, null);
                object end = tl.GetType().GetProperty("EndTime").GetValue(tl, null);
                Console.WriteLine("      position = " + pos + "   end = " + end);

                // media properties
                MethodInfo mp = FindAsyncMethod(st, "TryGetMediaPropertiesAsync");
                Type mpType = mp.ReturnType.GetGenericArguments()[0];
                object props = Await(mp.Invoke(s, null), mpType);
                if (props != null)
                {
                    Console.WriteLine("      title  = " + props.GetType().GetProperty("Title").GetValue(props, null));
                    Console.WriteLine("      artist = " + props.GetType().GetProperty("Artist").GetValue(props, null));
                    Console.WriteLine("      album  = " + props.GetType().GetProperty("AlbumTitle").GetValue(props, null));

                    // thumbnail -> System.Drawing.Image
                    object thumb = props.GetType().GetProperty("Thumbnail").GetValue(props, null);
                    if (thumb != null)
                    {
                        MethodInfo or = FindAsyncMethod(thumb.GetType(), "OpenReadAsync");
                        Type streamType = or.ReturnType.GetGenericArguments()[0];
                        object winrtStream = Await(or.Invoke(thumb, null), streamType);
                        MethodInfo asStream = null;
                        foreach (MethodInfo m in typeof(WindowsRuntimeStreamExtensions).GetMethods())
                        {
                            if (m.Name == "AsStreamForRead" && m.GetParameters().Length == 1) { asStream = m; break; }
                        }
                        Stream net = (Stream)asStream.Invoke(null, new object[] { winrtStream });
                        using (net)
                        using (Image img = Image.FromStream(net))
                        {
                            Console.WriteLine("      cover  = " + img.Width + "x" + img.Height + " " + img.PixelFormat);
                        }
                    }
                    else
                    {
                        Console.WriteLine("      cover  = (none)");
                    }
                }
            }
            Console.WriteLine("SESSION_COUNT=" + n);
            Console.WriteLine("PROBE_RESULT=SUCCESS");
        }
        catch (Exception ex)
        {
            Console.WriteLine("PROBE_RESULT=FAILED");
            Console.WriteLine(ex.ToString());
            Environment.ExitCode = 1;
        }
    }
}
