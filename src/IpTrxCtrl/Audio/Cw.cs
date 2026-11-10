using System;
using System.Collections.Generic;
using System.Text;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Decodificatore CW scritto da zero: demodulazione I/Q sulla frequenza scelta, filtro a media mobile
    /// largo quanto la banda di decodifica, soglia adattiva sul rumore con isteresi, misura di punti,
    /// linee e pause con stima automatica della velocità (5-60 WPM).
    /// </summary>
    public sealed class CwDecoder
    {
        private static readonly Dictionary<string, char> Table = new()
        {
            [".-"] = 'A', ["-..."] = 'B', ["-.-."] = 'C', ["-.."] = 'D', ["."] = 'E', ["..-."] = 'F', ["--."] = 'G', ["...."] = 'H',
            [".."] = 'I', [".---"] = 'J', ["-.-"] = 'K', [".-.."] = 'L', ["--"] = 'M', ["-."] = 'N', ["---"] = 'O', [".--."] = 'P',
            ["--.-"] = 'Q', [".-."] = 'R', ["..."] = 'S', ["-"] = 'T', ["..-"] = 'U', ["...-"] = 'V', [".--"] = 'W', ["-..-"] = 'X',
            ["-.--"] = 'Y', ["--.."] = 'Z', ["-----"] = '0', [".----"] = '1', ["..---"] = '2', ["...--"] = '3', ["....-"] = '4',
            ["....."] = '5', ["-...."] = '6', ["--..."] = '7', ["---.."] = '8', ["----."] = '9', ["..--.."] = '?', ["-..-."] = '/',
            [".-.-.-"] = '.', ["--..--"] = ',', ["-...-"] = '=', [".-.-."] = '+', ["-....-"] = '-', [".--.-."] = '@', ["---..."] = ':',
        };

        private const int Fs = AudioEngine.SampleRate;
        private const int Tick = 40;                 // campioni per passo di analisi = 5 ms
        private const double TickMs = 1000.0 * Tick / Fs;

        private double _freq, _phase, _dphase;
        private int _avgLen;
        private double[] _iBuf = new double[1], _qBuf = new double[1];
        private int _pos;
        private double _iSum, _qSum;
        private int _tickCount;

        private double _noise = 1, _peak = 1;
        private bool _on;
        private double _stateMs;
        private double _dotMs = 60;                  // 20 WPM all'inizio
        private int _marks;
        private readonly StringBuilder _sym = new();
        private bool _charDone = true, _wordDone = true;

        public double ThresholdFactor = 0.45;        // 0.25..0.75
        public int FixedWpm;                         // 0 = automatico
        public event Action<string>? Text;
        public double Level { get; private set; }    // 0..1 rispetto al picco
        public double Wpm => FixedWpm > 0 ? FixedWpm : 1200.0 / _dotMs;
        public double FrequencyHz => _freq;

        public CwDecoder(double freqHz, double widthHz) { Configure(freqHz, widthHz); }

        public void Configure(double freqHz, double widthHz)
        {
            _freq = Math.Clamp(freqHz, 50, 3500);
            _dphase = 2 * Math.PI * _freq / Fs;
            int len = Math.Clamp((int)Math.Round(Fs / Math.Clamp(widthHz, 25, 400)), 10, 400);
            if (len != _avgLen)
            {
                _avgLen = len;
                _iBuf = new double[len]; _qBuf = new double[len];
                _pos = 0; _iSum = 0; _qSum = 0;
            }
        }

        public void Process(short[] x, int count)
        {
            for (int n = 0; n < count; n++)
            {
                double v = x[n] / 32768.0;
                double i = v * Math.Cos(_phase), q = v * Math.Sin(_phase);
                _phase += _dphase;
                if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
                _iSum += i - _iBuf[_pos]; _qSum += q - _qBuf[_pos];
                _iBuf[_pos] = i; _qBuf[_pos] = q;
                if (++_pos >= _avgLen) _pos = 0;
                if (++_tickCount >= Tick)
                {
                    _tickCount = 0;
                    Step(Math.Sqrt(_iSum * _iSum + _qSum * _qSum) / _avgLen);
                }
            }
        }

        private readonly double[] _hist = new double[600];   // 3 s di inviluppo
        private readonly double[] _sorted = new double[600];
        private int _histCount, _histPos, _sinceSort;

        private void Step(double m)
        {
            // rumore = 30° percentile, segnale = 95° percentile degli ultimi 3 s
            _hist[_histPos] = m;
            _histPos = (_histPos + 1) % _hist.Length;
            if (_histCount < _hist.Length) _histCount++;
            if (++_sinceSort >= 10 || _histCount < 50)
            {
                _sinceSort = 0;
                Array.Copy(_hist, _sorted, _histCount);
                Array.Sort(_sorted, 0, _histCount);
                _noise = _sorted[(int)(_histCount * 0.30)];
                _peak = _sorted[Math.Min(_histCount - 1, (int)(_histCount * 0.95))];
            }
            double thr = _noise + (_peak - _noise) * ThresholdFactor;
            bool snrOk = _histCount >= 100 && _peak > _noise * 4;
            Level = Math.Clamp((m - _noise) / Math.Max(1e-9, _peak - _noise), 0, 1);

            bool on = snrOk && (_on ? m > thr * 0.8 : m > thr);
            _stateMs += TickMs;
            if (on != _on)
            {
                if (_on) EndMark(_stateMs);
                _on = on;
                _stateMs = 0;
                if (on) { _charDone = false; _wordDone = false; }
            }
            else if (!_on) CheckSpace(_stateMs);
        }

        private double Dot => FixedWpm > 0 ? 1200.0 / FixedWpm : _dotMs;

        private void EndMark(double ms)
        {
            if (ms < 8) return;   // disturbo
            double dot = Dot;
            bool dash = ms > 2 * dot;
            _sym.Append(dash ? '-' : '.');
            if (FixedWpm == 0)
            {
                double est = dash ? ms / 3 : ms;
                _marks++;
                _dotMs += (est - _dotMs) * (_marks < 8 ? 0.5 : dash ? 0.15 : 0.25);
                _dotMs = Math.Clamp(_dotMs, 20, 240);
            }
            if (_sym.Length > 7) { _sym.Clear(); Emit("*"); }
        }

        private void CheckSpace(double ms)
        {
            double dot = Dot;
            if (!_charDone && ms > 2.2 * dot)
            {
                _charDone = true;
                if (_sym.Length > 0)
                {
                    Emit(Table.TryGetValue(_sym.ToString(), out char c) ? c.ToString() : "*");
                    _sym.Clear();
                }
            }
            if (!_wordDone && ms > 5.5 * dot)
            {
                _wordDone = true;
                Emit(" ");
            }
        }

        private void Emit(string s) => Text?.Invoke(s);
    }

    /// <summary>Spettro per la cascata: finestra di Hann di 512 campioni con zeri fino a 1024 punti, passo 171 campioni (21 ms), 0-2000 Hz.</summary>
    public sealed class Spectrum
    {
        // 0.2.5: una riga ogni 171 campioni, circa 21 ms (prima 512, 64 ms), cioè 3 volte più veloce. La finestra
        // è di 64 ms (512 campioni) completata con zeri a 1024 punti: punti e linee del CW restano separati.
        // Spettro visibile da 0 a 2000 Hz: 256 righe da 7,8 Hz.
        public const int N = 1024, W = 512, Hop = 171;
        public const double MaxHz = 2000;
        public static readonly int Bins = (int)(MaxHz * N / AudioEngine.SampleRate);   // 256
        private readonly double[] _buf = new double[W];
        private int _fill;
        private readonly double[] _win = new double[W];
        private readonly double[] _re = new double[N], _im = new double[N];

        /// <summary>Riga di magnitudini in dB (Bins valori, da 0 Hz a 3000 Hz).</summary>
        public event Action<float[]>? Row;

        public Spectrum()
        {
            for (int i = 0; i < W; i++) _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (W - 1));
        }

        public void Process(short[] x, int count)
        {
            for (int n = 0; n < count; n++)
            {
                _buf[_fill++] = x[n] / 32768.0;
                if (_fill == W)
                {
                    Compute();
                    Array.Copy(_buf, Hop, _buf, 0, W - Hop);
                    _fill = W - Hop;
                }
            }
        }

        private void Compute()
        {
            for (int i = 0; i < N; i++) { _re[i] = i < W ? _buf[i] * _win[i] : 0; _im[i] = 0; }
            Fft(_re, _im);
            var row = new float[Bins];
            for (int k = 0; k < Bins; k++)
            {
                double p = _re[k] * _re[k] + _im[k] * _im[k];
                row[k] = (float)(10 * Math.Log10(p + 1e-12));
            }
            Row?.Invoke(row);
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }
    }
}
