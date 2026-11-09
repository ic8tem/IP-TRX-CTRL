using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;
using IpTrxCtrl.Net;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Controllo della radio tramite rigctld (Hamlib): interroga la radio ogni 500 ms e accoda i comandi
    /// (frequenza, modo, VFO B e split, potenza, pitch CW, velocità del keyer, testo CW, PTT).
    /// </summary>
    public sealed class RigControl : IDisposable
    {
        private readonly AppSettings _s;
        private Process? _proc;
        private int _procPort;
        private TcpClient? _tcp;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _io = new(1, 1);
        private CancellationTokenSource? _cts;
        private readonly HashSet<string> _unsupported = new();
        private bool? _wattsMeter;
        private bool _pttWanted;
        private TciClient? _tci;
        private int _cycle;
        private string _maxKey = "";
        private float _maxW = float.NaN;
        private bool _mw2PowerOk = true;

        public RigTelemetry Telemetry { get; private set; } = new();
        public string Status { get; private set; } = "Radio non configurata";
        public event Action? Updated;

        public RigControl(AppSettings s) { _s = s; }

        public bool IsConfigured => _s.UseExternalRigctld || (_s.RigModel > 0 && !string.IsNullOrEmpty(_s.RigCom));

        public void Start()
        {
            Stop();
            if (_s.PttMethod == PttMethod.Tci) _tci = new TciClient(_s.TciHost, _s.TciPort);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Task.Run(() => LoopAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            Disconnect();
            if (_proc != null)
            {
                try { if (!_proc.HasExited) _proc.Kill(true); } catch { }
                _proc.Dispose();
                _proc = null;
            }
            _tci?.Dispose();
            _tci = null;
            _pttWanted = false;
            Telemetry = new RigTelemetry();
        }

        public void Dispose() => Stop();

        public string PttInfo => _s.PttMethod switch
        {
            PttMethod.Cat => "CAT",
            PttMethod.Rts => "RTS " + (string.IsNullOrEmpty(_s.PttCom) ? _s.RigCom : _s.PttCom),
            PttMethod.Dtr => "DTR " + (string.IsNullOrEmpty(_s.PttCom) ? _s.RigCom : _s.PttCom),
            PttMethod.Vox => "VOX",
            PttMethod.Tci => $"TCI {_s.TciHost}:{_s.TciPort}",
            _ => ""
        };

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!IsConfigured)
                    {
                        SetStatus("Radio non configurata (Impostazioni)");
                        await Task.Delay(2000, ct);
                        continue;
                    }
                    await EnsureConnectedAsync(ct);
                    await PollAsync(ct);
                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    var t = Telemetry.Clone();
                    t.CatOk = false;
                    Telemetry = t;
                    SetStatus("CAT non risponde: " + ex.Message);
                    Disconnect();
                    try { await Task.Delay(3000, ct); } catch { break; }
                }
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (_tcp != null && _tcp.Connected) return;
            string host; int port;
            if (_s.UseExternalRigctld)
            {
                host = _s.ExternalRigctldHost; port = _s.ExternalRigctldPort;
            }
            else
            {
                host = "127.0.0.1";
                if (_proc == null || _proc.HasExited)
                {
                    int p = FreeLocalPort();
                    _proc?.Dispose();
                    _proc = Hamlib.StartRigctld(_s, p);
                    if (_proc == null) throw new IOException(Hamlib.InstallHint);
                    _procPort = p;
                    await Task.Delay(1200, ct);
                }
                port = _procPort;
            }
            _tcp = new TcpClient();
            using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                t.CancelAfter(4000);
                await _tcp.ConnectAsync(host, port, t.Token);
            }
            var stream = _tcp.GetStream();
            _reader = new StreamReader(stream, Encoding.ASCII);
            _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };
            _unsupported.Clear();
            _wattsMeter = null;
            _maxKey = "";
            _mw2PowerOk = true;
            _cycle = 0;
            SetStatus("CAT OK");
        }

        private void Disconnect()
        {
            try { _tcp?.Dispose(); } catch { }
            _tcp = null; _reader = null; _writer = null;
        }

        private static int FreeLocalPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        /// <summary>Invia un comando e legge le righe. Null se rigctld risponde con errore (RPRT negativo).</summary>
        private async Task<List<string>?> CommandAsync(string cmd, int valueLines, CancellationToken ct)
        {
            await _io.WaitAsync(ct);
            try
            {
                if (_writer == null || _reader == null) throw new IOException("Non connesso");
                await _writer.WriteLineAsync(cmd);
                var lines = new List<string>();
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(3000);
                for (int i = 0; i < Math.Max(1, valueLines); i++)
                {
                    string? line = await _reader.ReadLineAsync(t.Token);
                    if (line == null) throw new IOException("rigctld ha chiuso la connessione");
                    if (line.StartsWith("RPRT", StringComparison.Ordinal))
                    {
                        int code = int.TryParse(line.Substring(4).Trim(), out int c) ? c : -1;
                        return code == 0 ? lines : null;
                    }
                    lines.Add(line.Trim());
                }
                return lines;
            }
            finally { _io.Release(); }
        }

        private async Task<string?> GetAsync(string cmd, CancellationToken ct, int lines = 1)
        {
            if (_unsupported.Contains(cmd)) return null;
            var r = await CommandAsync(cmd, lines, ct);
            if (r == null || r.Count == 0) { _unsupported.Add(cmd); return null; }
            return string.Join("\n", r);
        }

        private async Task<bool> SetAsync(string cmd)
        {
            try
            {
                var ct = _cts?.Token ?? CancellationToken.None;
                return await CommandAsync(cmd, 1, ct) != null;
            }
            catch (Exception ex)
            {
                SetStatus("Comando non riuscito: " + ex.Message);
                return false;
            }
        }

        private static float ParseF(string? s) =>
            s != null && float.TryParse(s.Split('\n')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN;

        private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

        private async Task PollAsync(CancellationToken ct)
        {
            var t = Telemetry.Clone();
            bool slow = _cycle++ % 4 == 0;
            var f = await CommandAsync("f", 1, ct);
            if (f != null && f.Count > 0 && long.TryParse(f[0], out long hz)) t.FreqHz = hz;
            var m = await CommandAsync("m", 2, ct);
            if (m != null && m.Count > 0) t.Mode = m[0];

            t.Transmitting = (await GetAsync("t", ct)) == "1";
            t.StrengthDb = ParseF(await GetAsync("l STRENGTH", ct));

            if (t.Transmitting || slow)
            {
                t.Swr = ParseF(await GetAsync("l SWR", ct));
                if (_wattsMeter != false)
                {
                    float w = ParseF(await GetAsync("l RFPOWER_METER_WATTS", ct));
                    _wattsMeter = !float.IsNaN(w);
                    t.PowerW = w;
                }
                if (_wattsMeter == false) t.PowerRel = ParseF(await GetAsync("l RFPOWER_METER", ct));
            }

            if (slow)
            {
                t.TxGain = ParseF(await GetAsync("l " + _s.TxGainLevel, ct));
                string? split = await GetAsync("s", ct, 2);
                if (split != null) t.Split = split.Split('\n')[0].Trim() == "1";
                string? fb = await GetAsync("i", ct);
                t.FreqBHz = fb != null && long.TryParse(fb.Split('\n')[0], out long b) ? b : 0;
                _unsupported.Remove("i");   // con lo split spento alcune radio rispondono con errore: riprova

                t.PowerSetRel = ParseF(await GetAsync("l RFPOWER", ct));
                await UpdatePowerWattsAsync(t, ct);

                float pitch = ParseF(await GetAsync("l CWPITCH", ct));
                t.CwPitch = float.IsNaN(pitch) ? 0 : (int)Math.Round(pitch);
                float ks = ParseF(await GetAsync("l KEYSPD", ct));
                t.KeySpeed = float.IsNaN(ks) ? 0 : (int)Math.Round(ks);

                string? mute = await GetAsync("u MUTE", ct);
                t.Mute = mute == null ? -1 : (mute.Trim() == "1" ? 1 : 0);
            }
            t.CatOk = true;
            t.RigName = _s.RigModelName;
            t.PttInfo = PttInfo;
            t.PttDelayMs = _s.PttDelayMs;
            t.Status = _tci != null && _s.PttMethod == PttMethod.Tci ? _tci.Status : Status;
            Telemetry = t;
            Updated?.Invoke();
        }

        /// <summary>Potenza impostata in watt tramite power2mW; se la radio non converte resta in percentuale.</summary>
        private async Task UpdatePowerWattsAsync(RigTelemetry t, CancellationToken ct)
        {
            if (t.FreqHz <= 0 || string.IsNullOrEmpty(t.Mode)) return;
            string key = $"{t.Mode}|{t.FreqHz / 1_000_000}";
            if (key != _maxKey)
            {
                _maxKey = key;
                _maxW = float.NaN;
                string? mw = await GetAsync($"\\power2mW 1.0 {t.FreqHz} {t.Mode}", ct);
                float v = ParseF(mw);
                if (!float.IsNaN(v) && v > 0) _maxW = v / 1000f;
                _unsupported.Remove($"\\power2mW 1.0 {t.FreqHz} {t.Mode}");
            }
            t.PowerMaxW = _maxW;
            if (!float.IsNaN(_maxW) && !float.IsNaN(t.PowerSetRel))
            {
                string cmd = $"\\power2mW {Inv(t.PowerSetRel)} {t.FreqHz} {t.Mode}";
                float v = ParseF(await GetAsync(cmd, ct));
                _unsupported.Remove(cmd);
                t.PowerSetW = float.IsNaN(v) ? t.PowerSetRel * _maxW : v / 1000f;
            }
            else t.PowerSetW = float.NaN;
        }

        // ------------------------------------------------------------------ comandi

        public async Task SetFreqModeAsync(long hz, string? mode)
        {
            if (hz > 0) await SetAsync("F " + hz.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(mode)) await SetAsync($"M {mode.Trim()} 0");
        }

        public Task SetVfoBAsync(long hz) => SetAsync("I " + hz.ToString(CultureInfo.InvariantCulture));

        public async Task SetSplitAsync(bool on)
        {
            await SetAsync(on ? "S 1 VFOB" : "S 0 VFOA");
            _cycle = 0;
        }

        public async Task VfoOpAsync(string op)
        {
            if (op != "XCHG" && op != "CPY") return;
            await SetAsync("G " + op);
            _cycle = 0;
        }

        /// <summary>Potenza: in watt (convertiti con mW2power) o relativa 0..1.</summary>
        public async Task SetPowerAsync(float value, bool watts)
        {
            float rel = value;
            var t = Telemetry;
            if (watts)
            {
                rel = float.NaN;
                if (_mw2PowerOk && t.FreqHz > 0 && !string.IsNullOrEmpty(t.Mode))
                {
                    try
                    {
                        var ct = _cts?.Token ?? CancellationToken.None;
                        var r = await CommandAsync($"\\mW2power {Inv(Math.Round(value * 1000))} {t.FreqHz} {t.Mode}", 1, ct);
                        if (r != null && r.Count > 0) rel = ParseF(r[0]);
                        else _mw2PowerOk = false;
                    }
                    catch { }
                }
                if (float.IsNaN(rel) && !float.IsNaN(t.PowerMaxW) && t.PowerMaxW > 0) rel = value / t.PowerMaxW;
                if (float.IsNaN(rel)) rel = value / 100f;
            }
            rel = Math.Clamp(rel, 0.01f, 1f);
            await SetAsync("L RFPOWER " + Inv(Math.Round(rel, 3)));
            _cycle = 0;
        }

        public async Task SetPitchAsync(int hz)
        {
            await SetAsync("L CWPITCH " + Math.Clamp(hz, 200, 1000).ToString(CultureInfo.InvariantCulture));
            _cycle = 0;
        }

        public async Task SetKeySpeedAsync(int wpm)
        {
            await SetAsync("L KEYSPD " + Math.Clamp(wpm, 5, 60).ToString(CultureInfo.InvariantCulture));
            _cycle = 0;
        }

        /// <summary>Testo CW trasmesso dal keyer interno della radio (send_morse).</summary>
        public async Task SendMorseAsync(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (!await SetAsync("b " + text)) SetStatus("La radio non accetta testo CW via CAT (send_morse)");
        }

        public Task StopMorseAsync() => SetAsync("\\stop_morse");

        /// <summary>PTT: col metodo VOX non invia comandi; con TCI usa il WebSocket.</summary>
        public async Task SetPttAsync(bool on)
        {
            if (_pttWanted == on) return;
            _pttWanted = on;
            try
            {
                switch (_s.PttMethod)
                {
                    case PttMethod.Vox: return;
                    case PttMethod.Tci:
                        _tci ??= new TciClient(_s.TciHost, _s.TciPort);
                        await _tci.SetPttAsync(on);
                        return;
                    default:
                        var ct = _cts?.Token ?? CancellationToken.None;
                        await CommandAsync(on ? "T 1" : "T 0", 1, ct);
                        return;
                }
            }
            catch (Exception ex) { SetStatus("PTT non riuscito: " + ex.Message); }
        }

        private void SetStatus(string s)
        {
            if (Status == s) return;
            Status = s;
            Updated?.Invoke();
        }
    }
}
