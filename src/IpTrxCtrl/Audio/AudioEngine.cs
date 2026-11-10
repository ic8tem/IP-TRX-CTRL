using System;
using System.Collections.Generic;
using System.Threading;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Keyer dei messaggi WAV (da WavPTT): coda di messaggi, ritardo dopo il PTT, 50 ms di pausa
    /// tra un messaggio e il successivo senza rilasciare il PTT. Mentre suona, il microfono è escluso.
    /// </summary>
    public sealed class WavKeyer
    {
        private readonly object _lock = new();
        private readonly Queue<(int index, short[] clip)> _queue = new();
        private short[]? _clip;
        private int _pos, _wait;
        private bool _active;

        public int DelayMs = 100;
        public int CurrentIndex { get; private set; } = -1;
        public int Queued { get { lock (_lock) return _queue.Count; } }
        public bool Active { get { lock (_lock) return _active; } }

        /// <summary>true = serve il PTT, false = rilascialo.</summary>
        public event Action<bool>? PttRequest;
        public event Action? Changed;

        public void Enqueue(int index, short[] clip)
        {
            bool start;
            lock (_lock)
            {
                _queue.Enqueue((index, clip));
                start = !_active;
                if (start)
                {
                    _active = true;
                    _clip = null;
                    _wait = Math.Max(0, DelayMs) * AudioEngine.SampleRate / 1000;
                }
            }
            if (start) PttRequest?.Invoke(true);
            Changed?.Invoke();
        }

        public void Stop()
        {
            bool was;
            lock (_lock)
            {
                was = _active;
                _queue.Clear();
                _clip = null;
                _active = false;
                CurrentIndex = -1;
            }
            if (was) PttRequest?.Invoke(false);
            Changed?.Invoke();
        }

        /// <summary>Chiamato dalla cattura: se attivo riempie il blocco con il messaggio (o silenzio) e restituisce true.</summary>
        internal bool Fill(short[] frame)
        {
            bool finished = false, changed = false;
            lock (_lock)
            {
                if (!_active) return false;
                for (int i = 0; i < frame.Length; i++)
                {
                    if (_wait > 0) { _wait--; frame[i] = 0; continue; }
                    if (_clip == null)
                    {
                        if (_queue.Count == 0)
                        {
                            frame[i] = 0;
                            if (!finished) { finished = true; _active = false; CurrentIndex = -1; }
                            continue;
                        }
                        var next = _queue.Dequeue();
                        _clip = next.clip; _pos = 0; CurrentIndex = next.index;
                        changed = true;
                    }
                    frame[i] = _pos < _clip.Length ? _clip[_pos++] : (short)0;
                    if (_pos >= _clip.Length)
                    {
                        _clip = null;
                        changed = true;
                        if (_queue.Count > 0) _wait = AudioEngine.SampleRate * 50 / 1000;   // 50 ms tra i messaggi
                    }
                }
            }
            if (finished) PttRequest?.Invoke(false);
            if (finished || changed) Changed?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// Cattura e riproduzione a 8 kHz, 16 bit, mono. Tutta l'elaborazione (keyer, rete, spettro,
    /// decodificatore CW) lavora a 8 kHz; il sistema operativo converte dalla frequenza della scheda.
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        public const int SampleRate = 8000;
        public const int FrameMs = 40;
        public const int FrameSamples = SampleRate * FrameMs / 1000;   // 320

        private readonly IAudioBackend _native = AudioBackends.Create();
        private readonly TciAudioBackend _tci = new();
        private IAudioBackend? _active;
        /// <summary>Server con il CAT via TCI: negli elenchi delle schede compare anche «TCI».</summary>
        public bool OfferTci { get; set; }
        private readonly short[] _rxCopy = new short[FrameSamples];
        private readonly object _localLock = new();
        private short[]? _local;
        private int _localPos;

        public JitterBuffer Playback { get; } = new();
        public WavKeyer Keyer { get; } = new();
        public volatile float TxGain = 1.0f;
        public volatile float RxGain = 1.0f;
        public float TxPeak, RxPeak;
        public string LastError => (_active ?? _native).LastError;

        /// <summary>Blocco PCM catturato (16 bit LE), con guadagno o messaggio WAV già applicato.</summary>
        public event Action<byte[], int>? Captured;
        /// <summary>Audio ricevuto prima del volume, per cascata e decodificatori CW.</summary>
        public event Action<short[], int>? RxSamples;

        public List<string> Inputs() => WithTci(_native.Inputs());
        public List<string> Outputs() => WithTci(_native.Outputs());

        private List<string> WithTci(List<string> list)
        {
            if (OfferTci && !list.Contains(TciAudioBackend.DeviceName)) list.Insert(0, TciAudioBackend.DeviceName);
            return list;
        }

        public void Start(string input, string output)
        {
            Playback.Reset();
            // «TCI» come ingresso o uscita: tutto l'audio passa dal TCI (niente schede né cavi virtuali)
            bool tci = input == TciAudioBackend.DeviceName || output == TciAudioBackend.DeviceName;
            _active = tci ? _tci : _native;
            _active.Start(input, output, OnCapture, OnFill);
        }

        public void Stop() { _native.Stop(); _tci.Stop(); }

        private void OnCapture(short[] frame)
        {
            byte[] buf = new byte[frame.Length * 2];
            float peak = 0;
            if (Keyer.Fill(frame))
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    short s = frame[i];
                    buf[2 * i] = (byte)s; buf[2 * i + 1] = (byte)(s >> 8);
                    float a = Math.Abs(s / 32768f);
                    if (a > peak) peak = a;
                }
            }
            else
            {
                float g = TxGain;
                for (int i = 0; i < frame.Length; i++)
                {
                    float v = frame[i] * g;
                    if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                    short s = (short)v;
                    buf[2 * i] = (byte)s; buf[2 * i + 1] = (byte)(s >> 8);
                    float a = Math.Abs(v) / 32768f;
                    if (a > peak) peak = a;
                }
            }
            TxPeak = peak;
            Captured?.Invoke(buf, buf.Length);
        }

        private void OnFill(short[] frame)
        {
            Playback.Read(frame, frame.Length);
            Array.Copy(frame, _rxCopy, frame.Length);
            try { RxSamples?.Invoke(_rxCopy, _rxCopy.Length); } catch { }
            float g = RxGain, peak = 0;
            lock (_localLock)
            {
                for (int i = 0; i < frame.Length; i++)
                {
                    float v = frame[i] * g;
                    if (_local != null)
                    {
                        v += _local[_localPos++];
                        if (_localPos >= _local.Length) _local = null;
                    }
                    if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                    frame[i] = (short)v;
                    float a = Math.Abs(v) / 32768f;
                    if (a > peak) peak = a;
                }
            }
            RxPeak = peak;
        }

        /// <summary>Ascolto locale di prova (Play dei WAV, Prova delle macro CW), senza PTT.</summary>
        public void PlayLocal(short[] samples)
        {
            lock (_localLock) { _local = samples.Length > 0 ? samples : null; _localPos = 0; }
        }

        public void StopLocal()
        {
            lock (_localLock) _local = null;
        }

        public bool LocalPlaying { get { lock (_localLock) return _local != null; } }

        public void Dispose() { _native.Dispose(); _tci.Dispose(); }

        /// <summary>Guadagno = volume (0–100 %) × (1 + boost oltre soglia in %).</summary>
        public static float GainFrom(int volumePercent, int boostPercent)
            => Math.Max(0, volumePercent) / 100f * (1f + Math.Max(0, boostPercent) / 100f);

        /// <summary>Genera il CW di prova (sidetone) per un testo, alla velocità e al pitch indicati.</summary>
        public static short[] Sidetone(string text, int wpm, int pitch)
        {
            var morse = new Dictionary<char, string>
            {
                ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.", ['G'] = "--.", ['H'] = "....",
                ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.",
                ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
                ['Y'] = "-.--", ['Z'] = "--..", ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
                ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.", ['?'] = "..--..", ['/'] = "-..-.",
                ['.'] = ".-.-.-", [','] = "--..--", ['='] = "-...-", ['+'] = ".-.-.", ['-'] = "-....-",
            };
            int dot = SampleRate * 1200 / Math.Clamp(wpm, 5, 60) / 1000;
            var outp = new List<short>();
            int ramp = SampleRate * 5 / 1000;
            void Tone(int n, bool on)
            {
                for (int i = 0; i < n; i++)
                {
                    if (!on) { outp.Add(0); continue; }
                    double env = Math.Min(1, Math.Min(i, n - 1 - i) / (double)ramp);
                    outp.Add((short)(Math.Sin(2 * Math.PI * pitch * i / SampleRate) * 9000 * env));
                }
            }
            foreach (char c in (text ?? "").ToUpperInvariant())
            {
                if (c == ' ') { Tone(dot * 4, false); continue; }
                if (!morse.TryGetValue(c, out string? code)) continue;
                foreach (char s in code) { Tone(s == '-' ? dot * 3 : dot, true); Tone(dot, false); }
                Tone(dot * 2, false);
            }
            return outp.ToArray();
        }
    }
}
