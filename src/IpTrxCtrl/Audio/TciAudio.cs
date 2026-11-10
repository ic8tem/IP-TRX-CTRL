using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Rig;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// «Scheda audio» TCI del server: l'audio ricevuto arriva dal programma della radio (ExpertSDR, Thetis…)
    /// e quello da trasmettere gli viene mandato sul WebSocket TCI, senza schede né cavi audio virtuali.
    /// Si sceglie come ingresso e uscita del server quando il CAT passa dal TCI.
    /// </summary>
    public sealed class TciAudioBackend : IAudioBackend
    {
        public const string DeviceName = "TCI · audio della radio";

        private readonly object _lock = new();
        private Action<short[]>? _onCapture, _fill;
        private CancellationTokenSource? _cts;

        // ricezione: dal TCI (qualunque frequenza) a blocchi da 320 campioni a 8 kHz
        private StreamResampler? _rxRs;
        private int _rxRate;
        private readonly short[] _rxFrame = new short[AudioEngine.FrameSamples];
        private int _rxFill;

        // trasmissione: audio del client a 8 kHz, tenuto al massimo 300 ms (niente ritardo accumulato)
        private readonly float[] _txRing = new float[AudioEngine.SampleRate];
        private int _txHead, _txCount;
        private const int TxMax = AudioEngine.SampleRate * 300 / 1000;
        private StreamResampler? _txRs;
        private int _txRate;
        private readonly List<float> _txOut = new();

        private readonly Func<int, int, float[]> _txSource;

        public TciAudioBackend() { _txSource = OnTxRequest; }

        public string LastError { get; private set; } = "";

        public List<string> Inputs() => new() { DeviceName };
        public List<string> Outputs() => new() { DeviceName };

        public void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill)
        {
            Stop();
            lock (_lock)
            {
                _onCapture = onCapture;
                _fill = fill;
                _rxRs = null; _rxFill = 0;
                _txRs = null; _txOut.Clear(); _txHead = 0; _txCount = 0;
            }
            LastError = "";
            TciLink.RxAudio += OnRx;
            TciLink.TxAudioSource = _txSource;
            TciLink.SetAudioEnabled(true);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => PaceAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            TciLink.RxAudio -= OnRx;
            if (ReferenceEquals(TciLink.TxAudioSource, _txSource)) TciLink.TxAudioSource = null;
            if (TciLink.AudioEnabled) TciLink.SetAudioEnabled(false);
            lock (_lock) { _onCapture = null; _fill = null; }
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Ogni 40 ms prende un blocco dall'audio del client (come farebbe una scheda) e lo tiene pronto per
        /// le richieste TX_CHRONO del programma della radio; così il buffer non si riempie mentre si riceve.
        /// </summary>
        private async Task PaceAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            long next = 0;
            var frame = new short[AudioEngine.FrameSamples];
            while (!ct.IsCancellationRequested)
            {
                next += AudioEngine.FrameMs;
                long wait = next - sw.ElapsedMilliseconds;
                if (wait > 0) { try { await Task.Delay((int)wait, ct); } catch { break; } }
                else if (wait < -500) next = sw.ElapsedMilliseconds;   // pausa lunga (PC sospeso): riparte
                Action<short[]>? fill;
                lock (_lock) fill = _fill;
                if (fill == null) continue;
                try { fill(frame); } catch { continue; }
                lock (_lock)
                {
                    foreach (short s in frame)
                    {
                        _txRing[(_txHead + _txCount) % _txRing.Length] = s / 32768f;
                        if (_txCount < TxMax) _txCount++;
                        else _txHead = (_txHead + 1) % _txRing.Length;   // troppo audio in attesa: scarta il più vecchio
                    }
                }
            }
        }

        private void OnRx(float[] samples, int count, int rate)
        {
            Action<short[]>? cap;
            var ready = new List<short[]>();
            lock (_lock)
            {
                cap = _onCapture;
                if (cap == null) return;
                if (_rxRs == null || _rxRate != rate) { _rxRs = new StreamResampler(rate, AudioEngine.SampleRate); _rxRate = rate; }
                foreach (float v in _rxRs.Process(samples, count))
                {
                    _rxFrame[_rxFill++] = (short)Math.Clamp(v * 32767f, -32768f, 32767f);
                    if (_rxFill == _rxFrame.Length)
                    {
                        _rxFill = 0;
                        ready.Add((short[])_rxFrame.Clone());
                    }
                }
            }
            foreach (var f in ready) { try { cap(f); } catch { } }
        }

        /// <summary>TX_CHRONO: «frames» campioni alla frequenza del programma della radio.</summary>
        private float[] OnTxRequest(int frames, int rate)
        {
            lock (_lock)
            {
                if (_txRs == null || _txRate != rate) { _txRs = new StreamResampler(AudioEngine.SampleRate, rate); _txRate = rate; _txOut.Clear(); }
                var block = new float[AudioEngine.SampleRate / 100];   // 10 ms a 8 kHz
                while (_txOut.Count < frames)
                {
                    for (int i = 0; i < block.Length; i++)
                    {
                        if (_txCount > 0) { block[i] = _txRing[_txHead]; _txHead = (_txHead + 1) % _txRing.Length; _txCount--; }
                        else block[i] = 0f;
                    }
                    _txOut.AddRange(_txRs.Process(block, block.Length));
                }
                var res = _txOut.GetRange(0, frames).ToArray();
                _txOut.RemoveRange(0, frames);
                return res;
            }
        }
    }

    /// <summary>
    /// Cambio della frequenza di campionamento a flusso continuo (audio vocale): filtro passa basso FIR a
    /// 3,5 kHz sul lato a frequenza più alta e interpolazione lineare. Basta per la fonia e per il CW.
    /// </summary>
    public sealed class StreamResampler
    {
        private readonly int _in, _out;
        private readonly double _step;
        private readonly float[] _taps;
        private readonly float[] _hist;
        private int _hpos;
        private float _last;
        private double _t;

        public StreamResampler(int inRate, int outRate)
        {
            _in = Math.Max(1, inRate);
            _out = Math.Max(1, outRate);
            _step = (double)_in / _out;
            int fs = Math.Max(_in, _out);
            int n = Math.Clamp((int)Math.Round(3.3 * fs / 1200.0) | 1, 15, 161);   // banda di transizione circa 1,2 kHz
            _taps = new float[n];
            double fc = Math.Min(3500.0, 0.45 * Math.Min(_in, _out)) / fs;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double k = i - (n - 1) / 2.0;
                double sinc = k == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * k) / (Math.PI * k);
                double w = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (n - 1));
                _taps[i] = (float)(sinc * w);
                sum += _taps[i];
            }
            for (int i = 0; i < n; i++) _taps[i] = (float)(_taps[i] / sum);
            _hist = new float[n];
        }

        private float Filter(float x)
        {
            _hist[_hpos] = x;
            double acc = 0;
            int idx = _hpos;
            for (int i = 0; i < _taps.Length; i++)
            {
                acc += _taps[i] * _hist[idx];
                idx = idx == 0 ? _hist.Length - 1 : idx - 1;
            }
            _hpos = (_hpos + 1) % _hist.Length;
            return (float)acc;
        }

        public List<float> Process(float[] x, int count)
        {
            var res = new List<float>(count * _out / _in + 4);
            if (_in == _out)
            {
                for (int i = 0; i < count; i++) res.Add(x[i]);
                return res;
            }
            bool down = _in > _out;
            // sequenza: _last, poi i campioni (filtrati prima se si scende di frequenza)
            var c = new float[count + 1];
            c[0] = _last;
            for (int i = 0; i < count; i++) c[i + 1] = down ? Filter(x[i]) : x[i];
            double t = _t;
            while (t < count)
            {
                int i = (int)t;
                double fr = t - i;
                float v = (float)(c[i] * (1 - fr) + c[i + 1] * fr);
                res.Add(down ? v : Filter(v));
                t += _step;
            }
            _t = t - count;
            _last = c[count];
            return res;
        }
    }
}
