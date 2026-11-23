using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Platform
{
    /// <summary>
    /// Ingresso MIDI per i tasti CW (0.3.6): adattatore Vail in modo MIDI, TinyMIDI e simili. Le note si leggono
    /// per ottava: Do (nota 0, 12, 24…) = tasto verticale, Do# (1, 13…) = punto, Re (2, 14…) = linea.
    /// Windows con winmm, Linux e Raspberry Pi con i dispositivi /dev/snd/midiC*D*. Sul Mac non c'è (usa il modo tastiera).
    /// </summary>
    public sealed class MidiInput : IDisposable
    {
        /// <summary>0 = tasto verticale, 1 = punto, 2 = linea; true = giù.</summary>
        public event Action<int, bool>? Contact;

        public static List<string> Devices()
        {
            var res = new List<string>();
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    uint n = midiInGetNumDevs();
                    for (uint i = 0; i < n; i++)
                    {
                        var caps = new MIDIINCAPS();
                        if (midiInGetDevCaps((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<MIDIINCAPS>()) == 0) res.Add(caps.szPname);
                    }
                }
                else if (OperatingSystem.IsLinux() && Directory.Exists("/dev/snd"))
                {
                    foreach (var f in Directory.GetFiles("/dev/snd", "midiC*D*").OrderBy(x => x))
                    {
                        string name = Path.GetFileName(f);
                        try
                        {
                            // /proc/asound/cardN/id: nome breve della scheda
                            string card = name.Substring(5, name.IndexOf('D') - 5);
                            string id = File.ReadAllText($"/proc/asound/card{card}/id").Trim();
                            res.Add($"{name} ({id})");
                        }
                        catch { res.Add(name); }
                    }
                }
            }
            catch { }
            return res;
        }

        private readonly Thread? _thread;
        private volatile bool _run = true;
        private FileStream? _fs;
        private IntPtr _handle;
        private MidiInProc? _proc;

        public MidiInput(string device)
        {
            if (OperatingSystem.IsWindows())
            {
                var list = Devices();
                int idx = list.IndexOf(device);
                if (idx < 0) idx = 0;
                if (list.Count == 0) throw new IOException("nessun dispositivo MIDI");
                _proc = OnWinMidi;
                int r = midiInOpen(out _handle, (UIntPtr)(uint)idx, _proc, IntPtr.Zero, 0x30000 /* CALLBACK_FUNCTION */);
                if (r != 0) throw new IOException($"midiInOpen errore {r}");
                midiInStart(_handle);
            }
            else if (OperatingSystem.IsLinux())
            {
                string name = device.Split(' ')[0];
                if (name.Length == 0) name = Devices().FirstOrDefault()?.Split(' ')[0] ?? "";
                if (name.Length == 0) throw new IOException("nessun dispositivo MIDI");
                _fs = new FileStream("/dev/snd/" + name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
                _thread = new Thread(LinuxLoop) { IsBackground = true, Name = "MIDI", Priority = ThreadPriority.Highest };
                _thread.Start();
            }
            else throw new PlatformNotSupportedException("MIDI non disponibile su questo sistema: usa il modo tastiera (Ctrl sinistro e destro)");
        }

        private void Note(byte status, byte note, byte vel)
        {
            int kind = status & 0xF0;
            if (kind != 0x90 && kind != 0x80) return;
            bool down = kind == 0x90 && vel > 0;
            int what = note % 12;
            if (what > 2) return;
            try { Contact?.Invoke(what, down); } catch { }
        }

        private void LinuxLoop()
        {
            byte status = 0;
            var data = new List<byte>();
            var buf = new byte[1];
            while (_run)
            {
                int n;
                try { n = _fs!.Read(buf, 0, 1); } catch { break; }
                if (n <= 0) { Thread.Sleep(5); continue; }
                byte b = buf[0];
                if (b >= 0xF8) continue;               // messaggi di tempo reale
                if ((b & 0x80) != 0) { status = b; data.Clear(); continue; }
                data.Add(b);
                if (data.Count == 2 && (status & 0xF0) is 0x80 or 0x90) { Note(status, data[0], data[1]); data.Clear(); }
                else if (data.Count >= 2) data.Clear();
            }
        }

        private void OnWinMidi(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            if (msg != 0x3C3 /* MIM_DATA */) return;
            uint v = (uint)p1.ToInt64();
            Note((byte)(v & 0xFF), (byte)((v >> 8) & 0x7F), (byte)((v >> 16) & 0x7F));
        }

        public void Dispose()
        {
            _run = false;
            try { _fs?.Dispose(); } catch { }
            if (_handle != IntPtr.Zero)
            {
                try { midiInStop(_handle); midiInReset(_handle); midiInClose(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
            SessionLog.Write("MIDI tasto chiuso");
        }

        // ---- winmm
        private delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MIDIINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwSupport;
        }

        [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetDevCapsW")] private static extern int midiInGetDevCaps(UIntPtr uDeviceID, ref MIDIINCAPS caps, uint cbMidiInCaps);
        [DllImport("winmm.dll")] private static extern int midiInOpen(out IntPtr lphMidiIn, UIntPtr uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, int dwFlags);
        [DllImport("winmm.dll")] private static extern int midiInStart(IntPtr hMidiIn);
        [DllImport("winmm.dll")] private static extern int midiInStop(IntPtr hMidiIn);
        [DllImport("winmm.dll")] private static extern int midiInReset(IntPtr hMidiIn);
        [DllImport("winmm.dll")] private static extern int midiInClose(IntPtr hMidiIn);
    }
}
