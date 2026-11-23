using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Riduzione del rumore con una rete neurale (dalla 0.3.7): HamNoise di e04 (licenza MIT,
    /// https://github.com/e04/HamNoise), la stessa che usa PiRO di VK3TR con il pulsante «HamNoise».
    /// Due modelli addestrati su segnali HF: fonia (SSB, AM, FM) e CW. Qui il motore «classic» (GRU da 64 unità),
    /// leggero anche per un Raspberry Pi, riscritto in C# riga per riga da core/src/denoise_gru.c e denoise_audio.c;
    /// i pesi sono quelli di core/generated, convertiti in file binari senza modifiche.
    /// Il modello lavora a 9600 Hz con blocchi FFT da 256 campioni e passo 144: l'audio a 8 kHz del programma viene
    /// ricampionato 8000 → 9600 → 8000. Ritardo aggiunto circa 60 ms.
    /// </summary>
    public sealed class NeuralDenoiser
    {
        private const int Fft = 256, Hop = 144, Bins = 129, Hidden = 64, Gates = 3;
        private const float Eps = 1.0e-8f;

        // ------------------------------------------------------------------ modello
        private sealed class Model
        {
            public int NumBands, EncoderSize;
            public float MaxGain, LnEps;
            public int[] BandWidths = Array.Empty<int>(), BandEmbedDims = Array.Empty<int>(), BandInputOffsets = Array.Empty<int>(), BandEmbedOffsets = Array.Empty<int>();
            public float[] BandEncW = Array.Empty<float>(), BandEncB = Array.Empty<float>();
            public float[] InNormW = null!, InNormB = null!, GruWih = null!, GruWhh = null!, GruBih = null!, GruBhh = null!;
            public float[] OutNormW = null!, OutNormB = null!, Fc1W = null!, Fc1B = null!, Fc2W = null!, Fc2B = null!;

            public static Model Load(string resource)
            {
                using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                               ?? throw new InvalidOperationException("modello " + resource + " mancante");
                using var r = new BinaryReader(st);
                if (new string(r.ReadChars(4)) != "HNM1") throw new InvalidDataException("modello HamNoise non valido");
                var m = new Model { NumBands = r.ReadInt32(), EncoderSize = r.ReadInt32(), MaxGain = r.ReadSingle(), LnEps = r.ReadSingle() };
                int[] Ints() { int n = r.ReadInt32(); var a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
                float[] Floats(int expect)
                {
                    int n = r.ReadInt32();
                    if (expect > 0 && n != expect) throw new InvalidDataException($"modello HamNoise: {n} pesi invece di {expect}");
                    var a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a;
                }
                if (m.NumBands > 0)
                {
                    m.BandWidths = Ints(); m.BandEmbedDims = Ints(); m.BandInputOffsets = Ints(); m.BandEmbedOffsets = Ints();
                    m.BandEncW = Floats(0); m.BandEncB = Floats(m.EncoderSize);
                    if (m.BandInputOffsets[m.NumBands] != Bins || m.BandEmbedOffsets[m.NumBands] != m.EncoderSize)
                        throw new InvalidDataException("modello HamNoise: bande non coerenti");
                }
                int gruIn = m.NumBands > 0 ? m.EncoderSize : Bins;
                m.InNormW = Floats(Bins); m.InNormB = Floats(Bins);
                m.GruWih = Floats(Gates * Hidden * gruIn); m.GruWhh = Floats(Gates * Hidden * Hidden);
                m.GruBih = Floats(Gates * Hidden); m.GruBhh = Floats(Gates * Hidden);
                m.OutNormW = Floats(Hidden); m.OutNormB = Floats(Hidden);
                m.Fc1W = Floats(Hidden * Hidden); m.Fc1B = Floats(Hidden);
                m.Fc2W = Floats(Bins * Hidden); m.Fc2B = Floats(Bins);
                return m;
            }
        }

        private static Model? _voice, _cw;
        private static readonly object ModelLock = new();
        private static Model Voice { get { lock (ModelLock) return _voice ??= Model.Load("hamnoise/voice"); } }
        private static Model Cw { get { lock (ModelLock) return _cw ??= Model.Load("hamnoise/cw"); } }

        // ------------------------------------------------------------------ stato
        private readonly float[] _window = new float[Fft];
        private readonly float[] _fifo = new float[Fft + Hop];
        private int _buffered;
        private readonly float[] _re = new float[Fft], _im = new float[Fft];
        private readonly float[] _ola = new float[Fft], _olaNorm = new float[Fft];
        private readonly float[] _frameOut = new float[Fft];
        private readonly float[] _features = new float[Bins], _bandRe = new float[Bins], _bandIm = new float[Bins], _gains = new float[Bins];
        private readonly float[] _hidden = new float[Hidden], _inNorm = new float[Bins], _encoded = new float[Bins];
        private readonly float[] _gatesIh = new float[Gates * Hidden], _gatesHh = new float[Gates * Hidden];
        private readonly float[] _nextHidden = new float[Hidden], _outNorm = new float[Hidden], _fc1 = new float[Hidden], _logits = new float[Bins];
        private readonly float[] _hopIn = new float[Hop], _hopOut = new float[Hop];

        private Resampler _up = new(6, 5), _down = new(5, 6);
        private readonly List<float> _in9600 = new(), _out9600 = new(), _tmp = new();
        private readonly Queue<float> _out8000 = new();
        private Model? _model;
        private bool _wasOn;
        private readonly object _lock = new();

        public bool Enabled { get; set; }
        /// <summary>Quanto filtrare: 1 = tutto il modello, 0,5 = metà (come il «wet» di PiRO). Un segnale debole resta più udibile.</summary>
        public float Strength { get; set; } = 1f;
        public string LastError { get; private set; } = "";

        public NeuralDenoiser()
        {
            for (int i = 0; i < Fft; i++) _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / Fft);
        }

        public void Reset()
        {
            lock (_lock) ResetLocked();
        }

        private void ResetLocked()
        {
            Array.Clear(_fifo); Array.Clear(_ola); Array.Clear(_olaNorm); Array.Clear(_hidden);
            _buffered = 0;
            _up = new Resampler(6, 5); _down = new Resampler(5, 6);
            _in9600.Clear(); _out9600.Clear(); _out8000.Clear();
            for (int i = 0; i < 256; i++) _out8000.Enqueue(0f);   // margine: i passi da 144 non coincidono con i blocchi da 40 ms
        }

        /// <summary>Filtra sul posto un blocco PCM 16 bit LE a 8 kHz. cw = modello per il CW, altrimenti fonia.</summary>
        public void Process(byte[] pcm, int len, bool cw)
        {
            if (!Enabled) { _wasOn = false; return; }
            lock (_lock)
            {
                try
                {
                    var m = cw ? Cw : Voice;
                    if (!_wasOn || !ReferenceEquals(m, _model)) { _model = m; ResetLocked(); }
                    _wasOn = true;
                    int n = len / 2;
                    _tmp.Clear();
                    for (int i = 0; i < n; i++) _tmp.Add((short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f);
                    _up.Process(_tmp, _in9600);
                    int pos = 0;
                    while (_in9600.Count - pos >= Hop)
                    {
                        _in9600.CopyTo(pos, _hopIn, 0, Hop);
                        pos += Hop;
                        if (ProcessHop(m)) _out9600.AddRange(_hopOut);
                    }
                    _in9600.RemoveRange(0, pos);
                    _tmp.Clear();
                    _down.Process(_out9600, _tmp);
                    _out9600.Clear();
                    foreach (var v in _tmp) _out8000.Enqueue(v);
                    for (int i = 0; i < n; i++)
                    {
                        float v = _out8000.Count > 0 ? _out8000.Dequeue() : 0f;
                        int s = (int)MathF.Round(Math.Clamp(v, -1f, 0.99997f) * 32768f);
                        pcm[2 * i] = (byte)s; pcm[2 * i + 1] = (byte)(s >> 8);
                    }
                    while (_out8000.Count > 1200) _out8000.Dequeue();   // non deve accumulare ritardo
                    LastError = "";
                }
                catch (Exception ex) { LastError = ex.Message; Enabled = false; }
            }
        }

        // ------------------------------------------------------------------ denoise_stream_process_hop
        private bool ProcessHop(Model m)
        {
            Array.Copy(_hopIn, 0, _fifo, _buffered, Hop);
            _buffered += Hop;
            if (_buffered < Fft) { Array.Clear(_hopOut); return false; }

            // analisi
            for (int i = 0; i < Fft; i++) { _re[i] = _fifo[i] * _window[i]; _im[i] = 0f; }
            FftInPlace(_re, _im, false);
            for (int i = 0; i < Bins; i++)
            {
                float re = _re[i], im = _im[i];
                float mag = MathF.Sqrt(re * re + im * im);
                _bandRe[i] = re; _bandIm[i] = im;
                _features[i] = (float)Math.Log(1.0 + Math.Max(mag, 0f));   // log1pf
            }
            GruStep(m);
            float s = Math.Clamp(Strength, 0f, 1f);
            if (s < 1f) for (int i = 0; i < Bins; i++) _gains[i] = (1f - s) + s * _gains[i];

            // sintesi (il campo 0..128 copre tutto lo spettro: niente dissolvenza ai bordi)
            Array.Clear(_re); Array.Clear(_im);
            for (int i = 0; i < Bins; i++)
            {
                float g = _gains[i];
                float re = _bandRe[i] * g, im = _bandIm[i] * g;
                _re[i] = re; _im[i] = im;
                if (i > 0 && i < Fft / 2) { _re[Fft - i] = re; _im[Fft - i] = -im; }
            }
            FftInPlace(_re, _im, true);
            for (int i = 0; i < Fft; i++) _frameOut[i] = _re[i] * _window[i];
            for (int i = 0; i < Fft; i++) { _ola[i] += _frameOut[i]; _olaNorm[i] += _window[i] * _window[i]; }
            for (int i = 0; i < Hop; i++) _hopOut[i] = _olaNorm[i] > Eps ? _ola[i] / _olaNorm[i] : 0f;
            ShiftLeft(_ola, Hop); ShiftLeft(_olaNorm, Hop);
            Array.Copy(_fifo, Hop, _fifo, 0, _buffered - Hop);
            _buffered -= Hop;
            return true;
        }

        private static void ShiftLeft(float[] v, int amount)
        {
            Array.Copy(v, amount, v, 0, v.Length - amount);
            Array.Clear(v, v.Length - amount, amount);
        }

        // ------------------------------------------------------------------ denoise_gru_step
        private static float Sigmoid(float x)
        {
            if (x >= 0f) { float z = MathF.Exp(-x); return 1f / (1f + z); }
            float e = MathF.Exp(x); return e / (1f + e);
        }

        private static float Silu(float x) => x * Sigmoid(x);

        private static void LayerNorm(float[] input, float[] w, float[] b, int size, float eps, float[] output)
        {
            float mean = 0f;
            for (int i = 0; i < size; i++) mean += input[i];
            mean /= size;
            float variance = 0f;
            for (int i = 0; i < size; i++) { float c = input[i] - mean; variance += c * c; }
            variance /= size;
            float scale = 1f / MathF.Sqrt(variance + eps);
            for (int i = 0; i < size; i++) output[i] = ((input[i] - mean) * scale) * w[i] + b[i];
        }

        private static void Linear(float[] w, int wOff, float[] bias, int bOff, float[] input, int inOff, int outSize, int inSize, float[] output, int outOff)
        {
            for (int o = 0; o < outSize; o++)
            {
                int row = wOff + o * inSize;
                float acc = bias[bOff + o];
                for (int k = 0; k < inSize; k++) acc += w[row + k] * input[inOff + k];
                output[outOff + o] = acc;
            }
        }

        private void GruStep(Model m)
        {
            LayerNorm(_features, m.InNormW, m.InNormB, Bins, m.LnEps, _inNorm);
            float[] gruIn = _inNorm;
            int gruInSize = Bins;
            if (m.NumBands > 0)
            {
                int wOff = 0, bOff = 0;
                for (int band = 0; band < m.NumBands; band++)
                {
                    int width = m.BandWidths[band], embed = m.BandEmbedDims[band];
                    int inOff = m.BandInputOffsets[band], outOff = m.BandEmbedOffsets[band];
                    Linear(m.BandEncW, wOff, m.BandEncB, bOff, _inNorm, inOff, embed, width, _encoded, outOff);
                    for (int i = 0; i < embed; i++) _encoded[outOff + i] = Silu(_encoded[outOff + i]);
                    wOff += embed * width; bOff += embed;
                }
                gruIn = _encoded;
                gruInSize = m.EncoderSize;
            }
            Linear(m.GruWih, 0, m.GruBih, 0, gruIn, 0, Gates * Hidden, gruInSize, _gatesIh, 0);
            Linear(m.GruWhh, 0, m.GruBhh, 0, _hidden, 0, Gates * Hidden, Hidden, _gatesHh, 0);
            for (int i = 0; i < Hidden; i++)
            {
                int r = i, u = Hidden + i, nw = 2 * Hidden + i;   // ordine di PyTorch: reset, update, new
                float reset = Sigmoid(_gatesIh[r] + _gatesHh[r]);
                float update = Sigmoid(_gatesIh[u] + _gatesHh[u]);
                float cand = MathF.Tanh(_gatesIh[nw] + reset * _gatesHh[nw]);
                _nextHidden[i] = (1f - update) * cand + update * _hidden[i];
            }
            Array.Copy(_nextHidden, _hidden, Hidden);
            LayerNorm(_hidden, m.OutNormW, m.OutNormB, Hidden, m.LnEps, _outNorm);
            Linear(m.Fc1W, 0, m.Fc1B, 0, _outNorm, 0, Hidden, Hidden, _fc1, 0);
            for (int i = 0; i < Hidden; i++) _fc1[i] = Silu(_fc1[i]);
            Linear(m.Fc2W, 0, m.Fc2B, 0, _fc1, 0, Bins, Hidden, _logits, 0);
            for (int i = 0; i < Bins; i++) _gains[i] = Sigmoid(_logits[i]) * m.MaxGain;
        }

        // ------------------------------------------------------------------ FFT radix-2 (denoise_fft_fallback)
        private static void FftInPlace(float[] re, float[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                float angle = (inverse ? 2f : -2f) * MathF.PI / len;
                float wlr = MathF.Cos(angle), wli = MathF.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    float wr = 1f, wi = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int even = i + k, odd = even + len / 2;
                        float xr = re[odd] * wr - im[odd] * wi;
                        float xi = re[odd] * wi + im[odd] * wr;
                        float er = re[even], ei = im[even];
                        re[even] = er + xr; im[even] = ei + xi;
                        re[odd] = er - xr; im[odd] = ei - xi;
                        float nwr = wr * wlr - wi * wli;
                        float nwi = wr * wli + wi * wlr;
                        wr = nwr; wi = nwi;
                    }
                }
            }
            if (inverse)
            {
                float sc = 1f / n;
                for (int i = 0; i < n; i++) { re[i] *= sc; im[i] *= sc; }
            }
        }

        // ------------------------------------------------------------------ ricampionatore razionale L/M (polifase, sinc con finestra di Blackman)
        private sealed class Resampler
        {
            private readonly int _l, _m, _taps;
            private readonly float[] _h;
            private readonly List<float> _buf = new();
            private long _t;   // istante della prossima uscita, in campioni della frequenza intermedia (ingresso × L)

            public Resampler(int l, int m, int taps = 24)
            {
                _l = l; _m = m; _taps = taps;
                int n = l * taps;
                _h = new float[n];
                double fc = 0.5 / Math.Max(l, m) * 0.9;   // taglio sotto la Nyquist più bassa (4 kHz), relativo alla frequenza intermedia
                double c = (n - 1) / 2.0;
                for (int i = 0; i < n; i++)
                {
                    double x = i - c;
                    double sinc = x == 0 ? 2 * fc : Math.Sin(2 * Math.PI * fc * x) / (Math.PI * x);
                    double w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1)) + 0.08 * Math.Cos(4 * Math.PI * i / (n - 1));
                    _h[i] = (float)(sinc * w * l);   // guadagno L per compensare gli zeri inseriti
                }
            }

            public void Process(List<float> input, List<float> output)
            {
                _buf.AddRange(input);
                while (true)
                {
                    long baseIdx = _t / _l;
                    if (baseIdx >= _buf.Count) break;
                    int phase = (int)(_t % _l);
                    float acc = 0f;
                    for (int k = 0; k < _taps; k++)
                    {
                        long idx = baseIdx - k;
                        if (idx < 0) break;
                        acc += _h[phase + k * _l] * _buf[(int)idx];
                    }
                    output.Add(acc);
                    _t += _m;
                }
                long keepFrom = _t / _l - _taps;
                if (keepFrom > 0)
                {
                    _buf.RemoveRange(0, (int)keepFrom);
                    _t -= keepFrom * _l;
                }
            }
        }
    }
}
