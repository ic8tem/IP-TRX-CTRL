using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Collegamento TCI completo (Expert Electronics: ExpertSDR2/3, Thetis e altri programmi compatibili):
    /// WebSocket in chiaro verso il programma della radio, comandi testuali «nome:argomenti;» e flussi
    /// binari per l'audio. Tiene lo stato della radio (frequenze, modo, trasmissione, split, potenza,
    /// S-meter, potenza in uscita, ROS) dalle notifiche del programma e si ricollega da solo.
    ///
    /// Flussi binari: intestazione di 64 byte (16 interi a 32 bit little endian: ricevitore, frequenza di
    /// campionamento, formato, codec, crc, lunghezza, tipo, canali, 8 riservati) seguita dai campioni.
    /// Tipo 1 = audio ricevuto, 2 = audio da trasmettere, 3 = richiesta di audio da trasmettere (TX_CHRONO,
    /// «lunghezza» = campioni per canale). Formati: 0 int16, 1 int24, 2 int32, 3 float32.
    /// </summary>
    public sealed class TciLink : IDisposable
    {
        /// <summary>Collegamento in uso quando il CAT passa dal TCI (uno solo per volta).</summary>
        public static TciLink? Current { get; private set; }

        // ---- audio: lo usa la «scheda» TCI del server; vale per qualunque collegamento attivo
        private static volatile bool _audioEnabled;
        /// <summary>Audio ricevuto dalla radio: campioni mono -1..1, quanti, frequenza di campionamento.</summary>
        public static event Action<float[], int, int>? RxAudio;
        /// <summary>Audio da trasmettere: chiede (campioni, frequenza) e riceve campioni mono -1..1.</summary>
        public static Func<int, int, float[]>? TxAudioSource;
        public static bool AudioEnabled => _audioEnabled;

        public static void SetAudioEnabled(bool on)
        {
            _audioEnabled = on;
            var link = Current;
            if (link != null && link.Ready) _ = on ? link.StartAudioAsync() : link.SendAsync("audio_stop:0;");
        }

        private readonly string _host;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private ClientWebSocket? _ws;
        private string _lastError = "";

        public string Status { get; private set; } = "TCI: collegamento in corso";
        public bool Connected { get; private set; }
        /// <summary>Il programma della radio ha mandato tutto lo stato iniziale («ready»).</summary>
        public bool Ready { get; private set; }
        public string Device { get; private set; } = "";
        public string Protocol { get; private set; } = "";

        // ---- stato della radio (ricevitore 0)
        public long FreqA { get; private set; }
        public long FreqB { get; private set; }
        public string Modulation { get; private set; } = "";
        public bool Trx { get; private set; }
        public bool Split { get; private set; }
        public int Drive { get; private set; } = -1;          // 0..100, -1 = sconosciuta
        public int FilterLo { get; private set; }
        public int FilterHi { get; private set; }
        public bool FilterKnown { get; private set; }
        public int CwSpeed { get; private set; }
        public int Mute { get; private set; } = -1;            // -1 sconosciuto, 0, 1
        private readonly List<string> _modulations = new();
        private bool _driveWithRx = true;

        private float _smeterDbm = float.NaN, _swr = float.NaN, _powerW = float.NaN;
        private DateTime _smeterAt, _txSensorsAt;

        public TciLink(string host, int port)
        {
            _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            _port = port;
            Current = this;
            _ = Task.Run(LoopAsync);
        }

        public string Endpoint => $"{_host}:{_port}";

        // ================================================================ letture

        /// <summary>S-meter in dBm (NaN se non arriva da più di 3 secondi).</summary>
        public float SmeterDbm => DateTime.UtcNow - _smeterAt < TimeSpan.FromSeconds(3) ? _smeterDbm : float.NaN;

        /// <summary>ROS misurato in trasmissione (NaN se non c'è una misura recente).</summary>
        public float Swr => DateTime.UtcNow - _txSensorsAt < TimeSpan.FromSeconds(1.5) ? _swr : float.NaN;

        /// <summary>Potenza in uscita in watt (0 se non c'è una misura recente).</summary>
        public float PowerW => DateTime.UtcNow - _txSensorsAt < TimeSpan.FromSeconds(1.5) && !float.IsNaN(_powerW) ? _powerW : 0f;

        public bool HasModulation(string m) => _modulations.Contains(m.ToLowerInvariant());

        // ================================================================ collegamento

        private async Task LoopAsync()
        {
            var ct = _cts.Token;
            while (!ct.IsCancellationRequested)
            {
                var ws = new ClientWebSocket();
                try
                {
                    using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        t.CancelAfter(5000);
                        await ws.ConnectAsync(new Uri($"ws://{_host}:{_port}"), t.Token);
                    }
                    _ws = ws;
                    Connected = true;
                    Ready = false;
                    _lastError = "";
                    Status = $"TCI {Endpoint} collegato, attendo lo stato della radio";
                    SessionLog.Write($"TCI collegato a {Endpoint}");
                    await ReceiveLoopAsync(ws, ct);
                    if (!ct.IsCancellationRequested) SessionLog.Write($"TCI {Endpoint}: il programma della radio ha chiuso il collegamento");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    string msg = ex.InnerException?.Message ?? ex.Message;
                    Status = $"TCI {Endpoint} non raggiungibile: {msg}";
                    if (msg != _lastError) { _lastError = msg; SessionLog.Write($"TCI {Endpoint} non raggiungibile: {msg}"); }
                }
                catch { }
                Connected = false;
                Ready = false;
                _ws = null;
                try { ws.Abort(); } catch { }
                ws.Dispose();
                try { await Task.Delay(3000, ct); } catch { break; }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[65536];
            var msg = new MemoryStream();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                msg.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                byte[] data = msg.GetBuffer();
                int len = (int)msg.Length;
                try
                {
                    if (r.MessageType == WebSocketMessageType.Text) OnText(Encoding.UTF8.GetString(data, 0, len));
                    else OnBinary(data, len);
                }
                catch (Exception ex) { SessionLog.Write("TCI messaggio non interpretato: " + ex.Message); }
                msg.SetLength(0);
            }
        }

        // ================================================================ notifiche testuali

        private void OnText(string text)
        {
            foreach (string raw in text.Split(';'))
            {
                string item = raw.Trim();
                if (item.Length == 0) continue;
                int colon = item.IndexOf(':');
                string name = (colon < 0 ? item : item.Substring(0, colon)).Trim().ToLowerInvariant();
                string[] a = colon < 0 ? Array.Empty<string>() : item.Substring(colon + 1).Split(',');
                for (int i = 0; i < a.Length; i++) a[i] = a[i].Trim();
                Handle(name, a);
            }
        }

        private void Handle(string name, string[] a)
        {
            switch (name)
            {
                case "protocol": Protocol = string.Join(" ", a); break;
                case "device": if (a.Length > 0) Device = a[0]; break;
                case "modulations_list":
                    _modulations.Clear();
                    foreach (var m in a) if (m.Length > 0) _modulations.Add(m.ToLowerInvariant());
                    break;
                case "ready":
                    if (!Ready)
                    {
                        Ready = true;
                        Status = $"TCI {Endpoint} · {(Device.Length > 0 ? Device : "radio")} pronta";
                        SessionLog.Write($"TCI pronto: {Device} {Protocol}".Trim());
                        _ = OnReadyAsync();
                    }
                    break;
                case "vfo":
                    if (a.Length >= 3 && Rx0(a[0]) && TryLong(a[2], out long f))
                    {
                        if (a[1] == "0") FreqA = f; else if (a[1] == "1") FreqB = f;
                    }
                    break;
                case "modulation":
                    if (a.Length >= 2 && Rx0(a[0])) Modulation = a[1].ToLowerInvariant();
                    else if (a.Length == 1) Modulation = a[0].ToLowerInvariant();
                    break;
                case "trx":
                    if (a.Length >= 2 && Rx0(a[0])) Trx = IsTrue(a[1]);
                    break;
                case "split_enable":
                    if (a.Length >= 2 && Rx0(a[0])) Split = IsTrue(a[1]);
                    break;
                case "drive":
                    if (a.Length == 1 && TryInt(a[0], out int d1)) { Drive = d1; _driveWithRx = false; }
                    else if (a.Length >= 2 && Rx0(a[0]) && TryInt(a[1], out int d2)) { Drive = d2; _driveWithRx = true; }
                    break;
                case "rx_filter_band":
                    if (a.Length >= 3 && Rx0(a[0]) && TryInt(a[1], out int lo) && TryInt(a[2], out int hi)) { FilterLo = lo; FilterHi = hi; FilterKnown = true; }
                    break;
                case "cw_macros_speed":
                case "cw_keyer_speed":
                    if (a.Length >= 1 && TryInt(a[a.Length - 1], out int wpm) && wpm > 0) CwSpeed = wpm;
                    break;
                case "mute":
                    if (a.Length >= 1) Mute = IsTrue(a[a.Length - 1]) ? 1 : 0;
                    break;
                case "rx_smeter":
                case "rx_channel_sensors":
                    // rx_smeter:ricevitore,canale,dBm;
                    if (a.Length >= 3 && Rx0(a[0]) && a[1] == "0" && TryFloat(a[2], out float dbm)) { _smeterDbm = dbm; _smeterAt = DateTime.UtcNow; }
                    break;
                case "tx_sensors":
                    // tx_sensors:ricevitore,microfono dB,potenza in uscita W,terzo valore,ROS; le fonti concordano sul
                    // 3° campo (potenza diretta/efficace) e sul 5° (ROS), non sul 4° (picco o riflessa): non lo uso
                    if (a.Length >= 5 && Rx0(a[0]))
                    {
                        if (TryFloat(a[2], out float fwd)) _powerW = fwd;
                        if (TryFloat(a[4], out float swr)) _swr = swr;
                        _txSensorsAt = DateTime.UtcNow;
                    }
                    break;
                case "tx_swr":
                    if (a.Length >= 2 && Rx0(a[0]) && TryFloat(a[1], out float s2)) { _swr = s2; _txSensorsAt = DateTime.UtcNow; }
                    break;
                case "tx_power":
                case "tx_forward_power":
                    if (a.Length >= 2 && Rx0(a[0]) && TryFloat(a[1], out float pw)) { _powerW = pw; _txSensorsAt = DateTime.UtcNow; }
                    break;
            }
        }

        private async Task OnReadyAsync()
        {
            // misure periodiche (S-meter, potenza e ROS) ogni 200 ms
            await SendAsync("rx_sensors_enable:true,200;");
            await SendAsync("tx_sensors_enable:true,200;");
            if (_audioEnabled) await StartAudioAsync();
        }

        private async Task StartAudioAsync()
        {
            // 8 kHz mono come il resto del programma; se il programma della radio non le accetta,
            // i campioni vengono comunque convertiti secondo l'intestazione di ogni blocco
            await SendAsync("audio_samplerate:8000;");
            await SendAsync("audio_stream_sample_type:float32;");
            await SendAsync("audio_stream_channels:1;");
            await SendAsync("audio_start:0;");
            SessionLog.Write("TCI audio avviato (ricezione e trasmissione)");
        }

        // ================================================================ comandi

        public async Task<bool> SendAsync(string command)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return false;
            await _sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(Encoding.ASCII.GetBytes(command), WebSocketMessageType.Text, true, _cts.Token);
                return true;
            }
            catch { return false; }
            finally { _sendLock.Release(); }
        }

        private async Task SendBinaryAsync(byte[] data)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync();
            try { await ws.SendAsync(data, WebSocketMessageType.Binary, true, _cts.Token); }
            catch { }
            finally { _sendLock.Release(); }
        }

        private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

        public Task<bool> SetFreqAAsync(long hz) { FreqA = hz; return SendAsync($"vfo:0,0,{I(hz)};"); }
        public Task<bool> SetFreqBAsync(long hz) { FreqB = hz; return SendAsync($"vfo:0,1,{I(hz)};"); }
        public Task<bool> SetSplitAsync(bool on) { Split = on; return SendAsync($"split_enable:0,{B(on)};"); }

        public Task<bool> SetModulationAsync(string tciMode) { Modulation = tciMode; return SendAsync($"modulation:0,{tciMode};"); }

        /// <summary>Larghezza del filtro mantenendo il centro attuale (vale qualunque sia il riferimento del programma).</summary>
        public Task<bool> SetFilterWidthAsync(int width)
        {
            if (!FilterKnown || width <= 0) return Task.FromResult(false);
            int c = (FilterLo + FilterHi) / 2;
            FilterLo = c - width / 2; FilterHi = c + width / 2;
            return SendAsync($"rx_filter_band:0,{I(FilterLo)},{I(FilterHi)};");
        }

        /// <summary>Trasmissione; con l'audio TCI attivo il programma prende il segnale da trasmettere dal TCI.</summary>
        public Task<bool> SetTrxAsync(bool on)
        {
            Trx = on;
            return SendAsync(on ? (_audioEnabled ? "trx:0,true,tci;" : "trx:0,true;") : "trx:0,false;");
        }

        public Task<bool> SetDriveAsync(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            Drive = percent;
            return SendAsync(_driveWithRx ? $"drive:0,{I(percent)};" : $"drive:{I(percent)};");
        }

        public async Task<bool> SetCwSpeedAsync(int wpm)
        {
            CwSpeed = wpm;
            bool a = await SendAsync($"cw_macros_speed:{I(wpm)};");
            await SendAsync($"cw_keyer_speed:{I(wpm)};");
            return a;
        }

        /// <summary>Testo CW trasmesso dal programma della radio; virgole, due punti e punto e virgola sono separatori TCI.</summary>
        public Task<bool> SendCwAsync(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text.ToUpperInvariant())
                sb.Append(ch is ',' or ';' or ':' ? ' ' : ch);
            string t = sb.ToString().Trim();
            return t.Length == 0 ? Task.FromResult(false) : SendAsync($"cw_macros:0,{t};");
        }

        public Task<bool> StopCwAsync() => SendAsync("cw_macros_stop;");

        public Task<bool> SetMuteAsync(bool on) { Mute = on ? 1 : 0; return SendAsync($"mute:{B(on)};"); }

        // ================================================================ modi

        /// <summary>Modo TCI → nome Hamlib usato dal resto del programma.</summary>
        public static string ToHamlib(string tci) => tci.ToLowerInvariant() switch
        {
            "usb" => "USB",
            "lsb" => "LSB",
            "cw" or "cwu" => "CW",
            "cwl" => "CWR",
            "am" => "AM",
            "sam" => "AMS",
            "dsb" => "DSB",
            "nfm" or "fm" => "FM",
            "wfm" => "WFM",
            "digu" => "PKTUSB",
            "digl" => "PKTLSB",
            "drm" => "DRM",
            "" => "",
            _ => tci.ToUpperInvariant()
        };

        /// <summary>Nome Hamlib → modo TCI, scegliendo tra i nomi che il programma della radio dichiara.</summary>
        public string ToTci(string hamlib)
        {
            string Pick(params string[] names)
            {
                foreach (var n in names) if (_modulations.Count == 0 || _modulations.Contains(n)) return n;
                return names[0];
            }
            return hamlib.Trim().ToUpperInvariant() switch
            {
                "USB" => "usb",
                "LSB" => "lsb",
                "CW" => Pick("cw", "cwu"),
                "CWR" => Pick("cwl", "cw"),
                "AM" => "am",
                "AMS" => "sam",
                "DSB" => "dsb",
                "FM" or "FMN" => Pick("nfm", "fm"),
                "WFM" => "wfm",
                "PKTUSB" or "RTTY" => "digu",
                "PKTLSB" or "RTTYR" => "digl",
                "DRM" => "drm",
                var m => m.ToLowerInvariant()
            };
        }

        // ================================================================ audio binario

        private static uint U(byte[] d, int o) => BitConverter.ToUInt32(d, o);   // little endian (tutte le piattaforme supportate)

        private static int Bytes(uint format) => format switch { 0 => 2, 1 => 3, 2 => 4, 3 => 4, _ => 0 };

        private void OnBinary(byte[] d, int len)
        {
            if (len < 64) return;
            uint receiver = U(d, 0), rate = U(d, 4), format = U(d, 8), length = U(d, 20), type = U(d, 24), channels = U(d, 28);
            if (receiver != 0) return;
            if (rate == 0) rate = 48000;
            if (channels != 1 && channels != 2) channels = 2;
            if (type == 1) OnRxAudio(d, len, rate, format, (int)channels);
            else if (type == 3) _ = AnswerTxChronoAsync(rate, format, (int)channels, (int)length);
        }

        private static void OnRxAudio(byte[] d, int len, uint rate, uint format, int channels)
        {
            int bps = Bytes(format);
            if (bps == 0) return;
            int frames = (len - 64) / (bps * channels);
            if (frames <= 0) return;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
                mono[i] = ReadSample(d, 64 + i * channels * bps, format);   // canale sinistro
            try { RxAudio?.Invoke(mono, frames, (int)rate); } catch { }
        }

        private static float ReadSample(byte[] d, int o, uint format) => format switch
        {
            0 => BitConverter.ToInt16(d, o) / 32768f,
            1 => ((d[o] | (d[o + 1] << 8) | (d[o + 2] << 16)) << 8 >> 8) / 8388608f,
            2 => BitConverter.ToInt32(d, o) / 2147483648f,
            _ => BitConverter.ToSingle(d, o)
        };

        private static void WriteSample(byte[] d, int o, uint format, float v)
        {
            v = Math.Clamp(v, -1f, 1f);
            switch (format)
            {
                case 0: BitConverter.TryWriteBytes(d.AsSpan(o), (short)(v * 32767)); break;
                case 1: { int x = (int)(v * 8388607); d[o] = (byte)x; d[o + 1] = (byte)(x >> 8); d[o + 2] = (byte)(x >> 16); break; }
                case 2: BitConverter.TryWriteBytes(d.AsSpan(o), (int)(v * 2147483647.0)); break;
                default: BitConverter.TryWriteBytes(d.AsSpan(o), v); break;
            }
        }

        /// <summary>TX_CHRONO: il programma chiede «length» campioni per canale; rispondo con un blocco TX_AUDIO.</summary>
        private async Task AnswerTxChronoAsync(uint rate, uint format, int channels, int frames)
        {
            if (frames <= 0 || frames > 48000) return;
            if (Bytes(format) == 0) format = 3;
            int bps = Bytes(format);
            float[] mono;
            try { mono = TxAudioSource?.Invoke(frames, (int)rate) ?? new float[frames]; }
            catch { mono = new float[frames]; }
            var d = new byte[64 + frames * channels * bps];
            void H(int idx, uint v) => BitConverter.TryWriteBytes(d.AsSpan(idx * 4), v);
            H(0, 0); H(1, rate); H(2, format); H(3, 0); H(4, 0); H(5, (uint)frames); H(6, 2); H(7, (uint)channels);
            for (int i = 0; i < frames; i++)
            {
                float v = i < mono.Length ? mono[i] : 0f;
                for (int c = 0; c < channels; c++) WriteSample(d, 64 + (i * channels + c) * bps, format, v);
            }
            await SendBinaryAsync(d);
        }

        // ================================================================ utilità

        private static bool Rx0(string s) => s == "0";
        private static bool IsTrue(string s) => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        private static string B(bool b) => b ? "true" : "false";
        private static bool TryLong(string s, out long v)
        {
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { v = (long)Math.Round(d); return true; }
            return false;
        }
        private static bool TryInt(string s, out int v)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { v = (int)Math.Round(d); return true; }
            return false;
        }
        private static bool TryFloat(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        public void Dispose()
        {
            if (Current == this) Current = null;
            _cts.Cancel();
            try { _ws?.Abort(); } catch { }
        }
    }
}
