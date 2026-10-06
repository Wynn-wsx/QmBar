using System;
using System.Runtime.InteropServices;

namespace QmBar
{
    /// <summary>
    /// 系统主音量控制（Windows Core Audio 的 IAudioEndpointVolume）。
    /// GSMTC 没有音量接口，所以音量只能走 Core Audio。这里控制的是「默认输出设备的主音量」，
    /// 而不是单个播放器的音量——普通用户按任务栏音量键调的就是它。
    /// </summary>
    internal static class Audio
    {
        private static readonly object _lock = new object();
        private static IAudioEndpointVolume _vol;

        private static IAudioEndpointVolume Endpoint()
        {
            lock (_lock)
            {
                if (_vol != null) return _vol;
                try
                {
                    Type t = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                    if (t == null) return null;
                    IMMDeviceEnumerator en = (IMMDeviceEnumerator)Activator.CreateInstance(t);

                    IMMDevice dev;
                    int hr = en.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out dev);
                    if (hr != 0 || dev == null) return null;

                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    object o;
                    hr = dev.Activate(ref iid, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out o);
                    if (hr != 0 || o == null) return null;

                    _vol = o as IAudioEndpointVolume;
                    return _vol;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static bool Available { get { return Endpoint() != null; } }

        /// <summary>返回 0..100，取不到返回 -1。</summary>
        public static int GetVolume()
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return -1;
            try
            {
                float f;
                if (v.GetMasterVolumeLevelScalar(out f) != 0) return -1;
                int p = (int)Math.Round(f * 100f);
                if (p < 0) p = 0;
                if (p > 100) p = 100;
                return p;
            }
            catch (Exception)
            {
                _vol = null;             // 设备换了，下回重新取
                return -1;
            }
        }

        public static bool SetVolume(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try
            {
                Guid ctx = Guid.Empty;
                return v.SetMasterVolumeLevelScalar(percent / 100f, ref ctx) == 0;
            }
            catch (Exception)
            {
                _vol = null;
                return false;
            }
        }

        public static bool GetMute()
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try
            {
                int m;
                if (v.GetMute(out m) != 0) return false;
                return m != 0;
            }
            catch (Exception)
            {
                _vol = null;
                return false;
            }
        }

        public static bool SetMute(bool mute)
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try
            {
                Guid ctx = Guid.Empty;
                return v.SetMute(mute ? 1 : 0, ref ctx) == 0;
            }
            catch (Exception)
            {
                _vol = null;
                return false;
            }
        }

        // ---------------- COM 接口声明（顺序必须与 vtable 完全一致） ----------------

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice(string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out int count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDb, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
            [PreserveSig] int SetMute(int mute, ref Guid ctx);
            [PreserveSig] int GetMute(out int mute);
            [PreserveSig] int GetVolumeStepInfo(out int step, out int stepCount);
            [PreserveSig] int VolumeStepUp(ref Guid ctx);
            [PreserveSig] int VolumeStepDown(ref Guid ctx);
            [PreserveSig] int QueryHardwareSupport(out int mask);
            [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
        }
    }
}
