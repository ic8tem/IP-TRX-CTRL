using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace IpTrxCtrl.Audio
{
    /// <summary>Windows: driver MME (winmm.dll), 8 kHz 16 bit mono, condiviso. Funziona su 64 e 32 bit.</summary>
    public sealed class WinMmBackend : IAudioBackend
    {
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint WHDR_DONE = 0x00000001;
        private const int Buffers = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WAVEINCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WAVEOUTCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveInGetDevCapsW(UIntPtr id, ref WAVEINCAPS caps, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr h, uint id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr h);
        [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr h);

        [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveOutGetDevCapsW(UIntPtr id, ref WAVEOUTCAPS caps, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr h, uint id, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr h);

        private Thread? _inThread, _outThread;
        private volatile bool _run;
        public string LastError { get; private set; } = "";

        public List<string> Inputs()
        {
            var l = new List<string>();
            try
            {
                uint n = waveInGetNumDevs();
                for (uint i = 0; i < n; i++)
                {
                    var c = new WAVEINCAPS();
                    if (waveInGetDevCapsW((UIntPtr)i, ref c, (uint)Marshal.SizeOf<WAVEINCAPS>()) == 0) l.Add(c.szPname);
                }
            }
            catch { }
            return l;
        }

        public List<string> Outputs()
        {
            var l = new List<string>();
            try
            {
                uint n = waveOutGetNumDevs();
                for (uint i = 0; i < n; i++)
                {
                    var c = new WAVEOUTCAPS();
                    if (waveOutGetDevCapsW((UIntPtr)i, ref c, (uint)Marshal.SizeOf<WAVEOUTCAPS>()) == 0) l.Add(c.szPname);
                }
            }
            catch { }
            return l;
        }

        private static WAVEFORMATEX Format() => new()
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = AudioEngine.SampleRate,
            nAvgBytesPerSec = AudioEngine.SampleRate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0
        };

        public void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill)
        {
            Stop();
            LastError = "";
            _run = true;
            int inIdx = Inputs().IndexOf(input);
            int outIdx = Outputs().IndexOf(output);
            uint inId = inIdx >= 0 ? (uint)inIdx : WAVE_MAPPER;
            uint outId = outIdx >= 0 ? (uint)outIdx : WAVE_MAPPER;
            _inThread = new Thread(() => CaptureLoop(inId, onCapture)) { IsBackground = true, Name = "MME in", Priority = ThreadPriority.AboveNormal };
            _outThread = new Thread(() => PlayLoop(outId, fill)) { IsBackground = true, Name = "MME out", Priority = ThreadPriority.AboveNormal };
            _inThread.Start();
            _outThread.Start();
        }

        private static (IntPtr hdr, IntPtr data)[] AllocHeaders(int bytes)
        {
            var arr = new (IntPtr, IntPtr)[Buffers];
            int hs = Marshal.SizeOf<WAVEHDR>();
            for (int i = 0; i < Buffers; i++)
            {
                IntPtr data = Marshal.AllocHGlobal(bytes);
                IntPtr hdr = Marshal.AllocHGlobal(hs);
                Marshal.StructureToPtr(new WAVEHDR { lpData = data, dwBufferLength = (uint)bytes }, hdr, false);
                arr[i] = (hdr, data);
            }
            return arr;
        }

        private static uint Flags(IntPtr hdr) => (uint)Marshal.ReadInt32(hdr, Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags)).ToInt32());

        private void CaptureLoop(uint id, Action<short[]> onCapture)
        {
            int bytes = AudioEngine.FrameSamples * 2;
            var fmt = Format();
            uint hs = (uint)Marshal.SizeOf<WAVEHDR>();
            if (waveInOpen(out IntPtr h, id, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                LastError = "Impossibile aprire l'ingresso audio";
                return;
            }
            var bufs = AllocHeaders(bytes);
            var frame = new short[AudioEngine.FrameSamples];
            try
            {
                foreach (var b in bufs) { waveInPrepareHeader(h, b.hdr, hs); waveInAddBuffer(h, b.hdr, hs); }
                waveInStart(h);
                int next = 0;
                while (_run)
                {
                    var b = bufs[next];
                    if ((Flags(b.hdr) & WHDR_DONE) == 0) { Thread.Sleep(4); continue; }
                    Marshal.Copy(b.data, frame, 0, AudioEngine.FrameSamples);
                    try { onCapture(frame); } catch { }
                    waveInAddBuffer(h, b.hdr, hs);
                    next = (next + 1) % Buffers;
                }
            }
            finally
            {
                waveInReset(h);
                foreach (var b in bufs) waveInUnprepareHeader(h, b.hdr, hs);
                waveInClose(h);
                foreach (var b in bufs) { Marshal.FreeHGlobal(b.hdr); Marshal.FreeHGlobal(b.data); }
            }
        }

        private void PlayLoop(uint id, Action<short[]> fill)
        {
            int bytes = AudioEngine.FrameSamples * 2;
            var fmt = Format();
            uint hs = (uint)Marshal.SizeOf<WAVEHDR>();
            if (waveOutOpen(out IntPtr h, id, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
            {
                LastError = "Impossibile aprire l'uscita audio";
                return;
            }
            var bufs = AllocHeaders(bytes);
            var frame = new short[AudioEngine.FrameSamples];
            try
            {
                foreach (var b in bufs)
                {
                    waveOutPrepareHeader(h, b.hdr, hs);
                    try { fill(frame); } catch { Array.Clear(frame); }
                    Marshal.Copy(frame, 0, b.data, AudioEngine.FrameSamples);
                    waveOutWrite(h, b.hdr, hs);
                }
                int next = 0;
                while (_run)
                {
                    var b = bufs[next];
                    if ((Flags(b.hdr) & WHDR_DONE) == 0) { Thread.Sleep(4); continue; }
                    try { fill(frame); } catch { Array.Clear(frame); }
                    Marshal.Copy(frame, 0, b.data, AudioEngine.FrameSamples);
                    waveOutWrite(h, b.hdr, hs);
                    next = (next + 1) % Buffers;
                }
            }
            finally
            {
                waveOutReset(h);
                foreach (var b in bufs) waveOutUnprepareHeader(h, b.hdr, hs);
                waveOutClose(h);
                foreach (var b in bufs) { Marshal.FreeHGlobal(b.hdr); Marshal.FreeHGlobal(b.data); }
            }
        }

        public void Stop()
        {
            _run = false;
            _inThread?.Join(1000);
            _outThread?.Join(1000);
            _inThread = null; _outThread = null;
        }

        public void Dispose() => Stop();
    }
}
