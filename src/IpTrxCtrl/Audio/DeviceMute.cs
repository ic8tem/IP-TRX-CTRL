using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Mute e volume di sistema delle schede audio.
    /// Windows: Core Audio (IAudioEndpointVolume). macOS: CoreAudio. Linux/Raspberry: pactl (PipeWire/PulseAudio)
    /// per il dispositivo predefinito, amixer (ALSA) per le schede scelte per nome.
    /// </summary>
    public static class DeviceMute
    {
        /// <summary>Restituisce mute (-1 sconosciuto, 0 no, 1 sì) e volume 0..1 (NaN se sconosciuto).</summary>
        public static (int mute, float volume) Get(string device, bool input)
        {
            try
            {
                if (OperatingSystem.IsWindows()) return WinCoreAudio.Get(device, input);
                if (OperatingSystem.IsMacOS())
                {
                    uint id = MacCoreAudio.FindDevice(device, input);
                    return id == 0 ? (-1, float.NaN) : (MacCoreAudio.GetMute(id, input), MacCoreAudio.GetVolume(id, input));
                }
                return LinuxGet(device, input);
            }
            catch { return (-1, float.NaN); }
        }

        public static bool SetMute(string device, bool input, bool mute)
        {
            try
            {
                if (OperatingSystem.IsWindows()) return WinCoreAudio.SetMute(device, input, mute);
                if (OperatingSystem.IsMacOS())
                {
                    uint id = MacCoreAudio.FindDevice(device, input);
                    return id != 0 && MacCoreAudio.SetMute(id, input, mute);
                }
                return LinuxSetMute(device, input, mute);
            }
            catch { return false; }
        }

        public static bool SetVolume(string device, bool input, float v)
        {
            try
            {
                v = Math.Clamp(v, 0f, 1f);
                if (OperatingSystem.IsWindows()) return WinCoreAudio.SetVolume(device, input, v);
                if (OperatingSystem.IsMacOS())
                {
                    uint id = MacCoreAudio.FindDevice(device, input);
                    return id != 0 && MacCoreAudio.SetVolume(id, input, v);
                }
                return LinuxSetVolume(device, input, v);
            }
            catch { return false; }
        }

        /// <summary>Avviso sulla privacy del microfono (Windows), o stringa vuota.</summary>
        public static string PrivacyNote()
        {
            if (!OperatingSystem.IsWindows()) return "";
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone");
                string v = k?.GetValue("Value") as string ?? "";
                return v.Equals("Deny", StringComparison.OrdinalIgnoreCase)
                    ? "Windows nega l'accesso al microfono: sbloccalo in Impostazioni › Privacy › Microfono sul server."
                    : "";
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ Linux
        private static string? Card(string device)
        {
            var m = Regex.Match(device ?? "", @"CARD=([^,]+)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static (int, float) LinuxGet(string device, bool input)
        {
            string? card = Card(device);
            if (card == null)
            {
                string kind = input ? "source" : "sink", target = input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@";
                string mo = Run("pactl", $"get-{kind}-mute {target}");
                string vo = Run("pactl", $"get-{kind}-volume {target}");
                int mute = mo.Contains("yes") ? 1 : mo.Contains("no") ? 0 : -1;
                var pm = Regex.Match(vo, @"(\d+)%");
                return (mute, pm.Success ? int.Parse(pm.Groups[1].Value) / 100f : float.NaN);
            }
            string ctl = input ? "Capture" : "Master";
            string o = Run("amixer", $"-c {card} get {ctl}");
            if (!input && o.Length == 0) o = Run("amixer", $"-c {card} get PCM");
            int mu = o.Contains("[off]") ? 1 : o.Contains("[on]") ? 0 : -1;
            var p = Regex.Match(o, @"\[(\d+)%\]");
            return (mu, p.Success ? int.Parse(p.Groups[1].Value) / 100f : float.NaN);
        }

        private static bool LinuxSetMute(string device, bool input, bool mute)
        {
            string? card = Card(device);
            if (card == null)
            {
                string kind = input ? "source" : "sink", target = input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@";
                Run("pactl", $"set-{kind}-mute {target} {(mute ? 1 : 0)}");
                return true;
            }
            if (input) Run("amixer", $"-c {card} set Capture {(mute ? "nocap" : "cap")}");
            else { Run("amixer", $"-c {card} set Master {(mute ? "mute" : "unmute")}"); Run("amixer", $"-c {card} set PCM {(mute ? "mute" : "unmute")}"); }
            return true;
        }

        private static bool LinuxSetVolume(string device, bool input, float v)
        {
            string pct = ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";
            string? card = Card(device);
            if (card == null)
            {
                string kind = input ? "source" : "sink", target = input ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@";
                Run("pactl", $"set-{kind}-volume {target} {pct}");
                return true;
            }
            Run("amixer", $"-c {card} set {(input ? "Capture" : "Master")} {pct}");
            return true;
        }

        private static string Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return p.ExitCode == 0 ? o : "";
            }
            catch { return ""; }
        }
    }

    /// <summary>Core Audio di Windows tramite COM.</summary>
    internal static class WinCoreAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr ptr;
        }

        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant pv);

        private static readonly Guid IidEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
        private const uint ClsctxAll = 23, StateActive = 1;

        private static string FriendlyName(IMMDevice d)
        {
            if (d.OpenPropertyStore(0, out var store) != 0) return "";
            var key = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            if (store.GetValue(ref key, out var pv) != 0) return "";
            string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.ptr) ?? "" : "";
            PropVariantClear(ref pv);
            return s;
        }

        private static IAudioEndpointVolume? Endpoint(string mmeName, bool input)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            int flow = input ? 1 : 0;
            IMMDevice? found = null;
            if (!string.IsNullOrEmpty(mmeName) && en.EnumAudioEndpoints(flow, StateActive, out var col) == 0 && col.GetCount(out uint n) == 0)
            {
                for (uint i = 0; i < n && found == null; i++)
                {
                    if (col.Item(i, out var d) != 0) continue;
                    string fn = FriendlyName(d);
                    if (fn.Length > 0 && (fn == mmeName || fn.StartsWith(mmeName, StringComparison.OrdinalIgnoreCase))) found = d;
                }
            }
            if (found == null && en.GetDefaultAudioEndpoint(flow, 0, out var def) == 0) found = def;
            if (found == null) return null;
            var iid = IidEndpointVolume;
            return found.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object o) == 0 ? (IAudioEndpointVolume)o : null;
        }

        public static (int, float) Get(string name, bool input)
        {
            var ep = Endpoint(name, input);
            if (ep == null) return (-1, float.NaN);
            int mute = ep.GetMute(out bool m) == 0 ? (m ? 1 : 0) : -1;
            float vol = ep.GetMasterVolumeLevelScalar(out float v) == 0 ? v : float.NaN;
            return (mute, vol);
        }

        public static bool SetMute(string name, bool input, bool mute)
        {
            var ep = Endpoint(name, input);
            var ctx = Guid.Empty;
            return ep != null && ep.SetMute(mute, ref ctx) == 0;
        }

        public static bool SetVolume(string name, bool input, float v)
        {
            var ep = Endpoint(name, input);
            var ctx = Guid.Empty;
            return ep != null && ep.SetMasterVolumeLevelScalar(v, ref ctx) == 0;
        }
    }
}
