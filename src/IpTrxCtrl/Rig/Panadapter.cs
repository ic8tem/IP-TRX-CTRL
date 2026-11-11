using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;
using IpTrxCtrl.Net;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Panadapter della radio (server): lo spettro vero della radio, non quello dell'audio.
    /// - Radio Icom: dati della cascata (comando CI-V 27 00) letti da una porta CI-V che Hamlib non usa
    ///   (per esempio la seconda porta USB delle radio che la offrono come CI-V).
    /// - CAT via TCI (SDR): flusso IQ del ricevitore 0 trasformato in spettro (FFT) attorno al centro del panorama.
    /// Le righe escono al massimo 10 volte al secondo, con livelli 0..255.
    /// </summary>
    public sealed class Panadapter : IDisposable
    {
        public const int MaxLinesPerSecond = 10;
        private readonly AppSettings _s;
        private CancellationTokenSource? _cts;
        private SerialPort? _port;
        private TciIqSpectrum? _tci;
        private DateTime _lastLine = DateTime.MinValue;

        public string Status { get; private set; } = "spento";
        public bool Running { get; private set; }
        public event Action<SpectrumLine>? Line;

        public Panadapter(AppSettings s) { _s = s; }

        /// <summary>Il server può fornire il panadapter (opzione attiva e una sorgente configurata).</summary>
        public bool Available => _s.PanEnabled && (_s.CatViaTci || _s.PanCom.Length > 0);

        public void Start()
        {
            Stop();
            if (!_s.PanEnabled) { Status = "spento (Impostazioni › Panadapter)"; return; }
            _cts = new CancellationTokenSource();
            Running = true;
            if (_s.CatViaTci)
            {
                _tci = new TciIqSpectrum();
                _tci.Line += Emit;
                TciLink.SetIq(TciIqSpectrum.SampleRate);
                Status = "TCI · flusso IQ";
                SessionLog.Write("PANADAPTER avviato: TCI, flusso IQ del ricevitore 0");
                return;
            }
            if (_s.PanCom.Length == 0) { Status = "porta CI-V non scelta"; Running = false; return; }
            var ct = _cts.Token;
            _ = Task.Run(() => IcomLoopAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            if (_tci != null)
            {
                TciLink.SetIq(0);
                _tci.Dispose();
                _tci = null;
            }
            try { if (_port?.IsOpen == true) SendCiv(0x27, 0x11, 0x00); } catch { }
            try { _port?.Dispose(); } catch { }
            _port = null;
            Running = false;
        }

        public void Dispose() => Stop();

        private void Emit(SpectrumLine l)
        {
            var now = DateTime.UtcNow;
            if (now - _lastLine < TimeSpan.FromMilliseconds(1000.0 / MaxLinesPerSecond - 5)) return;
            _lastLine = now;
            try { Line?.Invoke(l); } catch { }
        }

        // ================================================================ Icom CI-V

        private byte RigAddr => byte.TryParse(_s.PanCivAddr.Trim().Replace("0x", "").Replace("h", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a) ? a : (byte)0x94;
        private const byte Controller = 0xE0;

        private void SendCiv(params byte[] body)
        {
            var f = new List<byte> { 0xFE, 0xFE, RigAddr, Controller };
            f.AddRange(body);
            f.Add(0xFD);
            _port?.Write(f.ToArray(), 0, f.Count);
        }

        private async Task IcomLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _port = new SerialPort(_s.PanCom, Math.Clamp(_s.PanBaud, 4800, 115200)) { Handshake = Handshake.None, DtrEnable = false, RtsEnable = false, ReadTimeout = 500 };
                    _port.Open();
                    // scopio acceso e dati della cascata verso questa porta
                    SendCiv(0x27, 0x10, 0x01);
                    await Task.Delay(100, ct);
                    SendCiv(0x27, 0x11, 0x01);
                    Status = $"Icom CI-V {_s.PanCom} · indirizzo {RigAddr:X2}h";
                    SessionLog.Write($"PANADAPTER avviato: Icom CI-V su {_s.PanCom} a {_s.PanBaud} baud, indirizzo {RigAddr:X2}h");
                    await ReadIcomAsync(_port, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    string msg = $"porta {_s.PanCom} non apribile o chiusa: {ex.Message}";
                    if (Status != msg) SessionLog.Write("PANADAPTER " + msg);
                    Status = msg;
                }
                try { _port?.Dispose(); } catch { }
                _port = null;
                try { await Task.Delay(3000, ct); } catch { break; }
            }
        }

        // dati della cascata arrivati a pezzi (USB: 11 divisioni; LAN: una sola)
        private long _scopeLo, _scopeHi;
        private bool _scopeOut;
        private readonly List<byte> _wave = new();
        private int _lastSeq;
        private DateTime _lastWave = DateTime.UtcNow;

        /// <summary>Lettura sincrona con tempo massimo (più affidabile delle letture asincrone delle porte seriali).</summary>
        private Task ReadIcomAsync(SerialPort port, CancellationToken ct)
        {
            var frame = new List<byte>(1024);
            var buf = new byte[4096];
            _lastWave = DateTime.UtcNow;
            port.ReadTimeout = 500;
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = port.Read(buf, 0, buf.Length); }
                catch (TimeoutException) { n = 0; }
                if (n == 0)
                {
                    // niente dati: riaccendo l'uscita della cascata (la radio la spegne se cambia impostazioni)
                    if (DateTime.UtcNow - _lastWave > TimeSpan.FromSeconds(5))
                    {
                        Status = $"Icom CI-V {_s.PanCom}: nessun dato della cascata (controlla porta, velocità, indirizzo e che la porta sia impostata come CI-V)";
                        SendCiv(0x27, 0x10, 0x01);
                        SendCiv(0x27, 0x11, 0x01);
                        _lastWave = DateTime.UtcNow;
                    }
                    continue;
                }
                for (int i = 0; i < n; i++)
                {
                    byte b = buf[i];
                    if (b == 0xFE && (frame.Count == 0 || (frame.Count == 1 && frame[0] == 0xFE))) { frame.Add(b); continue; }
                    if (frame.Count < 2) { frame.Clear(); continue; }
                    if (b == 0xFD) { OnCivFrame(frame); frame.Clear(); continue; }
                    frame.Add(b);
                    if (frame.Count > 2000) frame.Clear();
                }
            }
            return Task.CompletedTask;
        }

        private static int Bcd(byte b) => (b >> 4) * 10 + (b & 0x0F);

        /// <summary>Frequenza Icom: 5 byte BCD, cifre meno significative per prime.</summary>
        private static long BcdFreq(List<byte> f, int o)
        {
            long v = 0, mul = 1;
            for (int i = 0; i < 5; i++) { v += Bcd(f[o + i]) * mul; mul *= 100; }
            return v;
        }

        /// <summary>
        /// FE FE [a] [da] 27 00 [principale/secondario] [divisione] [divisioni] … : la prima divisione porta modo
        /// (centro o fisso), le due frequenze e «fuori campo»; le successive i livelli (0..160). Via LAN tutto insieme.
        /// </summary>
        private void OnCivFrame(List<byte> f)
        {
            // f[0..1] = FE FE, f[2] = destinatario, f[3] = mittente, f[4] = comando
            if (f.Count < 9 || f[4] != 0x27 || f[5] != 0x00) return;
            if (f[3] != RigAddr) return;
            if (f[6] != 0x00) return;                       // solo lo scopio principale
            int seq = Bcd(f[7]), max = Bcd(f[8]);
            int p = 9;
            if (seq == 1)
            {
                if (f.Count < p + 12) return;
                bool fixedMode = f[p] == 0x01;
                long a = BcdFreq(f, p + 1), b = BcdFreq(f, p + 6);
                _scopeOut = f[p + 11] == 0x01;
                if (fixedMode) { _scopeLo = a; _scopeHi = b; }
                else { _scopeLo = a - b; _scopeHi = a + b; }   // centro e metà dell'ampiezza
                p += 12;
                _wave.Clear();
            }
            else if (seq != _lastSeq + 1) { _wave.Clear(); _lastSeq = 0; return; }
            _lastSeq = seq;
            for (int i = p; i < f.Count; i++) _wave.Add(f[i]);
            if (seq < max) return;
            _lastSeq = 0;
            _lastWave = DateTime.UtcNow;
            if (_scopeHi <= _scopeLo) return;
            var bins = new byte[_wave.Count];
            for (int i = 0; i < bins.Length; i++) bins[i] = (byte)Math.Clamp(_wave[i] * 255 / 160, 0, 255);
            Status = $"Icom CI-V {_s.PanCom} · {_scopeLo / 1000.0:0.0}–{_scopeHi / 1000.0:0.0} kHz";
            Emit(new SpectrumLine { StartHz = _scopeLo, EndHz = _scopeHi, OutOfRange = _scopeOut, Bins = _scopeOut ? Array.Empty<byte>() : bins });
        }
    }

    /// <summary>Spettro dal flusso IQ del TCI: FFT di 2048 punti con finestra di Hann, 512 valori in uscita.</summary>
    public sealed class TciIqSpectrum : IDisposable
    {
        public const int SampleRate = 48000;
        private const int N = 2048, Out = 512;
        private readonly double[] _re = new double[N], _im = new double[N], _win = new double[N];
        private readonly float[] _i = new float[N], _q = new float[N];
        private int _fill;
        private readonly double[] _avg = new double[Out];
        private bool _avgInit;
        public event Action<SpectrumLine>? Line;

        public TciIqSpectrum()
        {
            for (int i = 0; i < N; i++) _win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
            TciLink.IqData += OnIq;
        }

        private void OnIq(long center, int rate, float[] iq, int pairs)
        {
            for (int k = 0; k < pairs; k++)
            {
                _i[_fill] = iq[2 * k];
                _q[_fill] = iq[2 * k + 1];
                if (++_fill == N) { _fill = 0; Compute(center, rate); }
            }
        }

        private void Compute(long center, int rate)
        {
            if (center <= 0 || rate <= 0) return;
            for (int k = 0; k < N; k++) { _re[k] = _i[k] * _win[k]; _im[k] = _q[k] * _win[k]; }
            Fft(_re, _im);
            // dalla frequenza più bassa alla più alta: prima la metà negativa dell'FFT
            const int group = N / Out;
            var bins = new byte[Out];
            for (int o = 0; o < Out; o++)
            {
                double p = 0;
                for (int g = 0; g < group; g++)
                {
                    int k = o * group + g;
                    int idx = (k + N / 2) % N;
                    p = Math.Max(p, _re[idx] * _re[idx] + _im[idx] * _im[idx]);
                }
                double db = 10 * Math.Log10(p + 1e-20);
                _avg[o] = _avgInit ? _avg[o] * 0.5 + db * 0.5 : db;
            }
            _avgInit = true;
            // da -130 dB a -30 dB (relativi al fondo scala del flusso) su 0..255
            for (int o = 0; o < Out; o++) bins[o] = (byte)Math.Clamp((_avg[o] + 130) * 2.55, 0, 255);
            Line?.Invoke(new SpectrumLine { StartHz = center - rate / 2, EndHz = center + rate / 2, Bins = bins });
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

        public void Dispose() => TciLink.IqData -= OnIq;
    }
}
