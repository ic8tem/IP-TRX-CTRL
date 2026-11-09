using System;
using System.IO;
using System.Text;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Elaborazione dei messaggi WAV (da WavPTT): lettura WAV, conversione a 8 kHz con filtro sinc a finestra,
    /// compressore 2:1, filtro passa banda Butterworth del 4° ordine, volume e limitazione.
    /// </summary>
    public static class Dsp
    {
        public static readonly string[] FilterNames = { "Nessun filtro", "300-2700 Hz", "500-2400 Hz", "500-2100 Hz" };

        /// <summary>Legge un file WAV PCM (8/16/24/32 bit) o float (32/64 bit). Restituisce i canali e la frequenza.</summary>
        public static (double[][] chans, int rate) ReadWav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            string name = Path.GetFileName(path);
            if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                throw new InvalidDataException($"{name} non è un file WAV valido");
            int fmtOff = -1, fmtLen = 0, dataOff = -1, dataLen = 0;
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int start = pos + 8;
                int len = (size < 0 || start + size > b.Length) ? b.Length - start : size;
                if (id == "fmt ") { fmtOff = start; fmtLen = len; }
                else if (id == "data") { dataOff = start; dataLen = len; }
                pos = start + Math.Max(0, size) + (size % 2 == 1 ? 1 : 0);
                if (size < 0) break;
            }
            if (fmtOff < 0 || fmtLen < 16 || dataOff < 0) throw new InvalidDataException($"{name}: formato WAV non riconosciuto");
            int tag = BitConverter.ToUInt16(b, fmtOff);
            int ch = BitConverter.ToUInt16(b, fmtOff + 2);
            int rate = BitConverter.ToInt32(b, fmtOff + 4);
            int bits = BitConverter.ToUInt16(b, fmtOff + 14);
            if (tag == 0xFFFE && fmtLen >= 26) tag = BitConverter.ToUInt16(b, fmtOff + 24);
            if (ch < 1 || ch > 8 || rate < 1000 || rate > 384000) throw new InvalidDataException("formato WAV non gestito");
            int bps = bits / 8;
            if (bps < 1 || (tag != 1 && tag != 3) || (tag == 3 && bps != 4 && bps != 8) || (tag == 1 && bps > 4))
                throw new InvalidDataException($"formato WAV non gestito (tipo {tag}, {bits} bit)");
            int n = dataLen / (bps * ch);
            if (n == 0) throw new InvalidDataException($"{name} non contiene audio");
            var outp = new double[ch][];
            for (int c = 0; c < ch; c++) outp[c] = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < ch; c++)
                {
                    int p = dataOff + (i * ch + c) * bps;
                    double v;
                    if (tag == 3 && bps == 4) v = BitConverter.ToSingle(b, p);
                    else if (tag == 3) v = BitConverter.ToDouble(b, p);
                    else if (bps == 1) v = (b[p] - 128) / 128.0;
                    else if (bps == 2) v = BitConverter.ToInt16(b, p) / 32768.0;
                    else if (bps == 3) { int x = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16); x = (x << 8) >> 8; v = x / 8388608.0; }
                    else v = BitConverter.ToInt32(b, p) / 2147483648.0;
                    outp[c][i] = v;
                }
            }
            return (outp, rate);
        }

        // ---------------------------------------------------------------- ricampionamento
        private const int KRes = 1024, Zc = 16;
        private static readonly double[] KTable = BuildTable();

        private static double[] BuildTable()
        {
            var t = new double[Zc * KRes + 2];
            for (int i = 0; i < t.Length; i++)
            {
                double a = (double)i / KRes;
                double v = a == 0 ? 1 : Math.Sin(Math.PI * a) / (Math.PI * a);
                v = a < Zc ? v * (0.5 + 0.5 * Math.Cos(Math.PI * a / Zc)) : 0;
                t[i] = v;
            }
            return t;
        }

        private static double KernelAt(double a)
        {
            if (a < 0) a = -a;
            double f = a * KRes;
            int i = (int)f;
            if (i >= KTable.Length - 1) return 0;
            double fr = f - i;
            return KTable[i] + (KTable[i + 1] - KTable[i]) * fr;
        }

        public static double[] Resample(double[] x, int inRate, int outRate)
        {
            if (inRate == outRate || x.Length == 0) return x;
            double ratio = (double)outRate / inRate;
            double cut = Math.Min(1, ratio) * 0.95;
            double half = Zc / cut;
            int n = (int)(x.Length * ratio);
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / ratio;
                int lo = Math.Max(0, (int)Math.Ceiling(t - half));
                int hi = Math.Min(x.Length - 1, (int)Math.Floor(t + half));
                double acc = 0;
                for (int k = lo; k <= hi; k++) acc += x[k] * KernelAt((k - t) * cut);
                y[i] = acc * cut;
            }
            return y;
        }

        // ---------------------------------------------------------------- filtri
        public sealed class Biquad
        {
            private readonly double _b0, _b1, _b2, _a1, _a2;
            private double _x1, _x2, _y1, _y2;

            private Biquad(double b0, double b1, double b2, double a1, double a2) { _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2; }

            /// <summary>Passa-alto o passa-basso (formule RBJ).</summary>
            public static Biquad Pass(bool highpass, double fs, double f0, double q)
            {
                double w0 = 2 * Math.PI * f0 / fs, cw = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q), a0 = 1 + alpha;
                double b0 = highpass ? (1 + cw) / 2 / a0 : (1 - cw) / 2 / a0;
                double b1 = highpass ? -(1 + cw) / a0 : (1 - cw) / a0;
                return new Biquad(b0, b1, b0, -2 * cw / a0, (1 - alpha) / a0);
            }

            /// <summary>Passa-banda a fattore Q costante (picco 0 dB).</summary>
            public static Biquad BandPass(double fs, double f0, double q)
            {
                double w0 = 2 * Math.PI * f0 / fs, cw = Math.Cos(w0), alpha = Math.Sin(w0) / (2 * q), a0 = 1 + alpha;
                return new Biquad(alpha / a0, 0, -alpha / a0, -2 * cw / a0, (1 - alpha) / a0);
            }

            public double Run(double v)
            {
                double y = _b0 * v + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = v; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        public static void BandPassInPlace(double[] x, double fs, double lo, double hi)
        {
            foreach (double q in new[] { 0.5411961, 1.3065630 })
            {
                var h = Biquad.Pass(true, fs, lo, q);
                var l = Biquad.Pass(false, fs, hi, q);
                for (int i = 0; i < x.Length; i++) x[i] = l.Run(h.Run(x[i]));
            }
        }

        /// <summary>Compressore 2:1, soglia 2·dB sotto il picco dell'inviluppo, recupero +dB (attacco 5 ms, rilascio 150 ms).</summary>
        public static void Compress(double[][] chans, double fs, double db)
        {
            int n = chans[0].Length;
            if (n == 0) return;
            double atk = Math.Exp(-1 / (0.005 * fs)), rel = Math.Exp(-1 / (0.150 * fs));
            var env = new double[n];
            double e = 0, emax = 0;
            for (int i = 0; i < n; i++)
            {
                double a = 0;
                foreach (var ch in chans) a = Math.Max(a, Math.Abs(ch[i]));
                e = a > e ? atk * e + (1 - atk) * a : rel * e + (1 - rel) * a;
                env[i] = e;
                emax = Math.Max(emax, e);
            }
            if (emax <= 0) return;
            double thr = emax * Math.Pow(10, -2 * db / 20), makeup = Math.Pow(10, db / 20);
            for (int i = 0; i < n; i++)
            {
                double g = makeup;
                if (env[i] > thr) g *= Math.Pow(10, -0.5 * (20 * Math.Log10(env[i] / thr)) / 20);
                foreach (var ch in chans) ch[i] *= g;
            }
        }

        /// <summary>Volume come in WavPTT: guadagno = 1000^(volume/100), con limitazione finale.</summary>
        public static double GainOf(int volume) => volume <= 0 ? 0 : Math.Pow(1000.0, volume / 100.0);

        /// <summary>Livello di picco della normalizzazione: −1 dB dal fondo scala.</summary>
        public const double NormalizePeak = 0.891250938;

        /// <summary>Porta il picco di tutti i canali a NormalizePeak (file muti lasciati come sono).</summary>
        public static void Normalize(double[][] chans)
        {
            double peak = 0;
            foreach (var ch in chans) foreach (double v in ch) peak = Math.Max(peak, Math.Abs(v));
            if (peak < 1e-6) return;
            double g = NormalizePeak / peak;
            foreach (var ch in chans) for (int i = 0; i < ch.Length; i++) ch[i] *= g;
        }

        /// <summary>
        /// Prepara un messaggio WAV: 8 kHz, normalizzazione a −1 dBFS, compressore, filtro,
        /// boost del cursore VOLUME (1000^(volume/100), come WavPTT), limitazione.
        /// </summary>
        public static short[] PrepareClip(string path, int volume, int filterMode, int compDb)
        {
            var (chans, rate) = ReadWav(path);
            for (int c = 0; c < chans.Length; c++) chans[c] = Resample(chans[c], rate, AudioEngine.SampleRate);
            Normalize(chans);
            if (compDb > 0) Compress(chans, AudioEngine.SampleRate, compDb);
            int n = chans[0].Length;
            var mono = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                foreach (var ch in chans) s += ch[i];
                mono[i] = s / chans.Length;
            }
            switch (filterMode)
            {
                case 1: BandPassInPlace(mono, AudioEngine.SampleRate, 300, 2700); break;
                case 2: BandPassInPlace(mono, AudioEngine.SampleRate, 500, 2400); break;
                case 3: BandPassInPlace(mono, AudioEngine.SampleRate, 500, 2100); break;
            }
            double g = GainOf(volume);
            var outp = new short[n];
            for (int i = 0; i < n; i++)
            {
                double v = mono[i] * g * 32768;
                outp[i] = (short)Math.Clamp(Math.Round(v), -32768, 32767);
            }
            return outp;
        }
    }
}
