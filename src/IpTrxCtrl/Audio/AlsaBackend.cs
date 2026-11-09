using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Linux e Raspberry Pi: ALSA (libasound.so.2). Con il dispositivo "default" l'audio passa da
    /// PipeWire o PulseAudio, quindi resta condiviso con gli altri programmi; il ricampionamento a 8 kHz
    /// è fatto dalla libreria.
    /// </summary>
    public sealed class AlsaBackend : IAudioBackend
    {
        private const string Lib = "libasound.so.2";
        private const int SND_PCM_STREAM_PLAYBACK = 0, SND_PCM_STREAM_CAPTURE = 1;
        private const int SND_PCM_FORMAT_S16_LE = 2;
        private const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;

        [DllImport(Lib)] private static extern int snd_pcm_open(out IntPtr pcm, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int stream, int mode);
        [DllImport(Lib)] private static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels, uint rate, int softResample, uint latencyUs);
        [DllImport(Lib)] private static extern nint snd_pcm_readi(IntPtr pcm, short[] buffer, nuint frames);
        [DllImport(Lib)] private static extern nint snd_pcm_writei(IntPtr pcm, short[] buffer, nuint frames);
        [DllImport(Lib)] private static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);
        [DllImport(Lib)] private static extern int snd_pcm_prepare(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_pcm_drop(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_pcm_close(IntPtr pcm);
        [DllImport(Lib)] private static extern int snd_device_name_hint(int card, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, out IntPtr hints);
        [DllImport(Lib)] private static extern IntPtr snd_device_name_get_hint(IntPtr hint, [MarshalAs(UnmanagedType.LPUTF8Str)] string id);
        [DllImport(Lib)] private static extern int snd_device_name_free_hint(IntPtr hints);

        private Thread? _inThread, _outThread;
        private volatile bool _run;
        public string LastError { get; private set; } = "";

        private static List<string> List(string ioid)
        {
            var l = new List<string> { "default" };
            try
            {
                if (snd_device_name_hint(-1, "pcm", out IntPtr hints) != 0) return l;
                try
                {
                    for (int i = 0; ; i++)
                    {
                        IntPtr h = Marshal.ReadIntPtr(hints, i * IntPtr.Size);
                        if (h == IntPtr.Zero) break;
                        string name = TakeString(snd_device_name_get_hint(h, "NAME"));
                        string io = TakeString(snd_device_name_get_hint(h, "IOID"));
                        if (name.Length == 0 || name == "null" || name == "default") continue;
                        if (io.Length > 0 && io != ioid) continue;
                        bool keep = name == "pulse" || name == "pipewire" || name.StartsWith("plughw:") || name.StartsWith("sysdefault:");
                        if (keep && !l.Contains(name)) l.Add(name);
                    }
                }
                finally { snd_device_name_free_hint(hints); }
            }
            catch { }
            return l;
        }

        private static string TakeString(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            return Marshal.PtrToStringUTF8(p) ?? "";   // stringa piccola, liberata alla chiusura
        }

        public List<string> Inputs() => List("Input");
        public List<string> Outputs() => List("Output");

        public void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill)
        {
            Stop();
            LastError = "";
            _run = true;
            string inDev = string.IsNullOrWhiteSpace(input) ? "default" : input;
            string outDev = string.IsNullOrWhiteSpace(output) ? "default" : output;
            _inThread = new Thread(() => Loop(inDev, true, onCapture)) { IsBackground = true, Name = "ALSA in", Priority = ThreadPriority.AboveNormal };
            _outThread = new Thread(() => Loop(outDev, false, fill)) { IsBackground = true, Name = "ALSA out", Priority = ThreadPriority.AboveNormal };
            _inThread.Start();
            _outThread.Start();
        }

        private void Loop(string device, bool capture, Action<short[]> cb)
        {
            IntPtr pcm;
            try
            {
                int r = snd_pcm_open(out pcm, device, capture ? SND_PCM_STREAM_CAPTURE : SND_PCM_STREAM_PLAYBACK, 0);
                if (r < 0) { LastError = $"ALSA: impossibile aprire {device} ({r})"; return; }
                r = snd_pcm_set_params(pcm, SND_PCM_FORMAT_S16_LE, SND_PCM_ACCESS_RW_INTERLEAVED, 1, AudioEngine.SampleRate, 1, 120000);
                if (r < 0) { LastError = $"ALSA: formato 8 kHz non accettato da {device} ({r})"; snd_pcm_close(pcm); return; }
            }
            catch (Exception ex) { LastError = "ALSA non disponibile: " + ex.Message; return; }

            var frame = new short[AudioEngine.FrameSamples];
            try
            {
                while (_run)
                {
                    if (capture)
                    {
                        nint n = snd_pcm_readi(pcm, frame, (nuint)frame.Length);
                        if (n < 0) { snd_pcm_recover(pcm, (int)n, 1); continue; }
                        if (n < frame.Length) Array.Clear(frame, (int)n, frame.Length - (int)n);
                        try { cb(frame); } catch { }
                    }
                    else
                    {
                        try { cb(frame); } catch { Array.Clear(frame); }
                        nint n = snd_pcm_writei(pcm, frame, (nuint)frame.Length);
                        if (n < 0) { snd_pcm_recover(pcm, (int)n, 1); }
                    }
                }
            }
            finally
            {
                try { snd_pcm_drop(pcm); snd_pcm_close(pcm); } catch { }
            }
        }

        public void Stop()
        {
            _run = false;
            _inThread?.Join(1500);
            _outThread?.Join(1500);
            _inThread = null; _outThread = null;
        }

        public void Dispose() => Stop();
    }
}
