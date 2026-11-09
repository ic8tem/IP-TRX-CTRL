using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace IpTrxCtrl.Audio
{
    /// <summary>Funzioni CoreAudio/CoreFoundation di macOS usate per elenco schede, mute e volume.</summary>
    internal static class MacCoreAudio
    {
        private const string CA = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
        private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        public const uint SystemObject = 1;
        public const uint Utf8 = 0x08000100;

        public static uint FourCC(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
        public static readonly uint ScopeGlobal = FourCC("glob"), ScopeInput = FourCC("inpt"), ScopeOutput = FourCC("outp");

        [StructLayout(LayoutKind.Sequential)]
        public struct Address { public uint Selector, Scope, Element; }

        [DllImport(CA)] public static extern int AudioObjectGetPropertyDataSize(uint id, ref Address a, uint qs, IntPtr q, out uint size);
        [DllImport(CA)] public static extern int AudioObjectGetPropertyData(uint id, ref Address a, uint qs, IntPtr q, ref uint size, IntPtr data);
        [DllImport(CA)] public static extern int AudioObjectSetPropertyData(uint id, ref Address a, uint qs, IntPtr q, uint size, IntPtr data);
        [DllImport(CA)] public static extern byte AudioObjectHasProperty(uint id, ref Address a);

        [DllImport(CF)] public static extern IntPtr CFStringCreateWithCString(IntPtr alloc, [MarshalAs(UnmanagedType.LPUTF8Str)] string s, uint enc);
        [DllImport(CF)] public static extern byte CFStringGetCString(IntPtr cf, byte[] buf, nint size, uint enc);
        [DllImport(CF)] public static extern void CFRelease(IntPtr cf);

        public sealed record Device(uint Id, string Name, string Uid, bool HasInput, bool HasOutput);

        public static string CfToString(IntPtr cf)
        {
            if (cf == IntPtr.Zero) return "";
            var buf = new byte[512];
            string s = CFStringGetCString(cf, buf, buf.Length, Utf8) != 0 ? Encoding.UTF8.GetString(buf).TrimEnd('\0') : "";
            CFRelease(cf);
            return s;
        }

        private static string StringProp(uint id, string sel)
        {
            var a = new Address { Selector = FourCC(sel), Scope = ScopeGlobal, Element = 0 };
            uint size = (uint)IntPtr.Size;
            IntPtr p = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                if (AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, p) != 0) return "";
                return CfToString(Marshal.ReadIntPtr(p));
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        private static bool HasStreams(uint id, uint scope)
        {
            var a = new Address { Selector = FourCC("stm#"), Scope = scope, Element = 0 };
            return AudioObjectGetPropertyDataSize(id, ref a, 0, IntPtr.Zero, out uint size) == 0 && size > 0;
        }

        public static List<Device> Devices()
        {
            var list = new List<Device>();
            try
            {
                var a = new Address { Selector = FourCC("dev#"), Scope = ScopeGlobal, Element = 0 };
                if (AudioObjectGetPropertyDataSize(SystemObject, ref a, 0, IntPtr.Zero, out uint size) != 0 || size == 0) return list;
                IntPtr p = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (AudioObjectGetPropertyData(SystemObject, ref a, 0, IntPtr.Zero, ref size, p) != 0) return list;
                    int n = (int)size / 4;
                    for (int i = 0; i < n; i++)
                    {
                        uint id = (uint)Marshal.ReadInt32(p, i * 4);
                        list.Add(new Device(id, StringProp(id, "lnam"), StringProp(id, "uid "), HasStreams(id, ScopeInput), HasStreams(id, ScopeOutput)));
                    }
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
            return list;
        }

        public static uint DefaultDevice(bool input)
        {
            var a = new Address { Selector = FourCC(input ? "dIn " : "dOut"), Scope = ScopeGlobal, Element = 0 };
            uint size = 4;
            IntPtr p = Marshal.AllocHGlobal(4);
            try { return AudioObjectGetPropertyData(SystemObject, ref a, 0, IntPtr.Zero, ref size, p) == 0 ? (uint)Marshal.ReadInt32(p) : 0; }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static uint FindDevice(string name, bool input)
        {
            foreach (var d in Devices())
                if (d.Name == name && (input ? d.HasInput : d.HasOutput)) return d.Id;
            return DefaultDevice(input);
        }

        public static int GetMute(uint dev, bool input)
        {
            var a = new Address { Selector = FourCC("mute"), Scope = input ? ScopeInput : ScopeOutput, Element = 0 };
            if (AudioObjectHasProperty(dev, ref a) == 0) return -1;
            uint size = 4;
            IntPtr p = Marshal.AllocHGlobal(4);
            try { return AudioObjectGetPropertyData(dev, ref a, 0, IntPtr.Zero, ref size, p) == 0 ? (Marshal.ReadInt32(p) != 0 ? 1 : 0) : -1; }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static bool SetMute(uint dev, bool input, bool mute)
        {
            var a = new Address { Selector = FourCC("mute"), Scope = input ? ScopeInput : ScopeOutput, Element = 0 };
            IntPtr p = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(p, mute ? 1 : 0);
                return AudioObjectSetPropertyData(dev, ref a, 0, IntPtr.Zero, 4, p) == 0;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static float GetVolume(uint dev, bool input)
        {
            foreach (uint el in new uint[] { 0, 1 })
            {
                var a = new Address { Selector = FourCC("volm"), Scope = input ? ScopeInput : ScopeOutput, Element = el };
                if (AudioObjectHasProperty(dev, ref a) == 0) continue;
                uint size = 4;
                IntPtr p = Marshal.AllocHGlobal(4);
                try
                {
                    if (AudioObjectGetPropertyData(dev, ref a, 0, IntPtr.Zero, ref size, p) == 0)
                        return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p));
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            return float.NaN;
        }

        public static bool SetVolume(uint dev, bool input, float v)
        {
            bool ok = false;
            foreach (uint el in new uint[] { 0, 1, 2 })
            {
                var a = new Address { Selector = FourCC("volm"), Scope = input ? ScopeInput : ScopeOutput, Element = el };
                if (AudioObjectHasProperty(dev, ref a) == 0) continue;
                IntPtr p = Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(p, BitConverter.SingleToInt32Bits(Math.Clamp(v, 0f, 1f)));
                    ok |= AudioObjectSetPropertyData(dev, ref a, 0, IntPtr.Zero, 4, p) == 0;
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            return ok;
        }
    }

    /// <summary>macOS: AudioQueue (AudioToolbox) a 8 kHz 16 bit mono; la conversione dalla scheda la fa il sistema.</summary>
    public sealed class MacBackend : IAudioBackend
    {
        private const string AT = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

        [StructLayout(LayoutKind.Sequential)]
        private struct ASBD
        {
            public double SampleRate;
            public uint FormatID, FormatFlags, BytesPerPacket, FramesPerPacket, BytesPerFrame, ChannelsPerFrame, BitsPerChannel, Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AQBuffer
        {
            public uint Capacity;
            public IntPtr Data;
            public uint ByteSize;
            public IntPtr UserData;
            public uint PacketDescriptionCapacity;
            public IntPtr PacketDescriptions;
            public uint PacketDescriptionCount;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InputCallback(IntPtr user, IntPtr aq, IntPtr buffer, IntPtr startTime, uint numPackets, IntPtr descs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OutputCallback(IntPtr user, IntPtr aq, IntPtr buffer);

        [DllImport(AT)] private static extern int AudioQueueNewInput(ref ASBD fmt, InputCallback cb, IntPtr user, IntPtr runLoop, IntPtr mode, uint flags, out IntPtr aq);
        [DllImport(AT)] private static extern int AudioQueueNewOutput(ref ASBD fmt, OutputCallback cb, IntPtr user, IntPtr runLoop, IntPtr mode, uint flags, out IntPtr aq);
        [DllImport(AT)] private static extern int AudioQueueAllocateBuffer(IntPtr aq, uint bytes, out IntPtr buffer);
        [DllImport(AT)] private static extern int AudioQueueEnqueueBuffer(IntPtr aq, IntPtr buffer, uint numDescs, IntPtr descs);
        [DllImport(AT)] private static extern int AudioQueueStart(IntPtr aq, IntPtr startTime);
        [DllImport(AT)] private static extern int AudioQueueStop(IntPtr aq, byte immediate);
        [DllImport(AT)] private static extern int AudioQueueDispose(IntPtr aq, byte immediate);
        [DllImport(AT)] private static extern int AudioQueueSetProperty(IntPtr aq, uint id, ref IntPtr data, uint size);

        private IntPtr _inQ, _outQ;
        private InputCallback? _inCb;
        private OutputCallback? _outCb;
        private Action<short[]>? _onCapture, _fill;
        private readonly short[] _inFrame = new short[AudioEngine.FrameSamples];
        private readonly short[] _outFrame = new short[AudioEngine.FrameSamples];
        private static readonly int DataSizeOffset = Marshal.OffsetOf<AQBuffer>(nameof(AQBuffer.ByteSize)).ToInt32();
        public string LastError { get; private set; } = "";

        public List<string> Inputs()
        {
            var l = new List<string>();
            foreach (var d in MacCoreAudio.Devices()) if (d.HasInput && d.Name.Length > 0) l.Add(d.Name);
            return l;
        }

        public List<string> Outputs()
        {
            var l = new List<string>();
            foreach (var d in MacCoreAudio.Devices()) if (d.HasOutput && d.Name.Length > 0) l.Add(d.Name);
            return l;
        }

        private static ASBD Format() => new()
        {
            SampleRate = AudioEngine.SampleRate,
            FormatID = MacCoreAudio.FourCC("lpcm"),
            FormatFlags = 4 | 8,  // signed integer | packed
            BytesPerPacket = 2, FramesPerPacket = 1, BytesPerFrame = 2, ChannelsPerFrame = 1, BitsPerChannel = 16
        };

        private static void SetDevice(IntPtr aq, string name, bool input)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (var d in MacCoreAudio.Devices())
            {
                if (d.Name != name || (input ? !d.HasInput : !d.HasOutput) || d.Uid.Length == 0) continue;
                IntPtr cf = MacCoreAudio.CFStringCreateWithCString(IntPtr.Zero, d.Uid, MacCoreAudio.Utf8);
                AudioQueueSetProperty(aq, MacCoreAudio.FourCC("aqcd"), ref cf, (uint)IntPtr.Size);
                MacCoreAudio.CFRelease(cf);
                return;
            }
        }

        public void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill)
        {
            Stop();
            _onCapture = onCapture;
            _fill = fill;
            _inCb = OnInput;
            _outCb = OnOutput;
            uint bytes = AudioEngine.FrameSamples * 2;
            var fmt = Format();
            try
            {
                if (AudioQueueNewInput(ref fmt, _inCb, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _inQ) == 0)
                {
                    SetDevice(_inQ, input, true);
                    for (int i = 0; i < 4; i++)
                        if (AudioQueueAllocateBuffer(_inQ, bytes, out IntPtr b) == 0) AudioQueueEnqueueBuffer(_inQ, b, 0, IntPtr.Zero);
                    if (AudioQueueStart(_inQ, IntPtr.Zero) != 0) LastError = "Microfono non disponibile (permesso negato?)";
                }
                else LastError = "Impossibile aprire l'ingresso audio";

                fmt = Format();
                if (AudioQueueNewOutput(ref fmt, _outCb, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _outQ) == 0)
                {
                    SetDevice(_outQ, output, false);
                    for (int i = 0; i < 4; i++)
                        if (AudioQueueAllocateBuffer(_outQ, bytes, out IntPtr b) == 0) OnOutput(IntPtr.Zero, _outQ, b);
                    AudioQueueStart(_outQ, IntPtr.Zero);
                }
                else LastError = "Impossibile aprire l'uscita audio";
            }
            catch (Exception ex) { LastError = "CoreAudio: " + ex.Message; }
        }

        private void OnInput(IntPtr user, IntPtr aq, IntPtr buffer, IntPtr startTime, uint numPackets, IntPtr descs)
        {
            try
            {
                var b = Marshal.PtrToStructure<AQBuffer>(buffer);
                int n = Math.Min((int)b.ByteSize / 2, _inFrame.Length);
                Marshal.Copy(b.Data, _inFrame, 0, n);
                if (n < _inFrame.Length) Array.Clear(_inFrame, n, _inFrame.Length - n);
                _onCapture?.Invoke(_inFrame);
            }
            catch { }
            if (_inQ != IntPtr.Zero) AudioQueueEnqueueBuffer(aq, buffer, 0, IntPtr.Zero);
        }

        private void OnOutput(IntPtr user, IntPtr aq, IntPtr buffer)
        {
            try { _fill?.Invoke(_outFrame); } catch { Array.Clear(_outFrame); }
            try
            {
                var b = Marshal.PtrToStructure<AQBuffer>(buffer);
                Marshal.Copy(_outFrame, 0, b.Data, _outFrame.Length);
                Marshal.WriteInt32(buffer, DataSizeOffset, _outFrame.Length * 2);
                AudioQueueEnqueueBuffer(aq, buffer, 0, IntPtr.Zero);
            }
            catch { }
        }

        public void Stop()
        {
            var i = _inQ; _inQ = IntPtr.Zero;
            var o = _outQ; _outQ = IntPtr.Zero;
            try { if (i != IntPtr.Zero) { AudioQueueStop(i, 1); AudioQueueDispose(i, 1); } } catch { }
            try { if (o != IntPtr.Zero) { AudioQueueStop(o, 1); AudioQueueDispose(o, 1); } } catch { }
        }

        public void Dispose() => Stop();
    }
}
