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
        // CAT via TCI: collegamento, traduttore rigctld e rigctld «Dummy» per i comandi non legati alla radio
        private TciLink? _tciLink;
        private TciRigctl? _tciBridge;
        private Process? _dummyProc;
        private int _cycle;
        private string _maxKey = "";
        private float _maxW = float.NaN;
        private bool _mw2PowerOk = true;
        private KeyingPlan _plan = new("", SerialLine.None, false, "", SerialLine.None, false, "");
        private SerialLines? _pttLines, _cwLines;
        private CwKeyer? _keyer;
        private ManualCwReplayer? _manual;
        private volatile bool _cwPtt;
        private bool _pttActual;
        private readonly SemaphoreSlim _pttLock = new(1, 1);

        // protezione ROS: blocco della trasmissione (resta attivo finché l'operatore non sblocca)
        private volatile bool _txBlocked;
        private float _blockedSwr = float.NaN;
        private int _highSwrCount;
        private bool _swrMissingLogged;

        // tempo massimo di trasmissione: la trasmissione resta interrotta finché l'operatore non rilascia il PTT
        private volatile bool _txTimedOut;
        private DateTime _txSince = DateTime.MinValue;
        private bool _txWasOn;
        // precedenza a chi è alla radio: trasmissioni che il programma non ha comandato bloccano quella remota
        private DateTime _ownTxUntil = DateTime.MinValue;    // fino a qui una trasmissione della radio è «nostra»
        private DateTime _localLockUntil = DateTime.MinValue;
        // comandi della radio: letti ogni 4 s e subito dopo un comando
        private volatile bool _ctlRefresh = true;
        private static readonly int[] DbSteps = { 1, 2, 3, 6, 10, 12, 15, 16, 18, 20, 21, 24, 30 };
        private static readonly int[] AgcCycle = { 2, 5, 3 };   // FAST, MID, SLOW

        /// <summary>Avviso per l'operatore (va anche al client collegato).</summary>
        public event Action<string>? Notice;

        public RigTelemetry Telemetry { get; private set; } = new();
        public string Status { get; private set; } = "Radio non configurata";
        public event Action? Updated;

        public RigControl(AppSettings s) { _s = s; }

        /// <summary>Trasmissione bloccata dalla protezione ROS.</summary>
        public bool TxBlocked => _txBlocked;

        /// <summary>La radio è in trasmissione (letta via CAT o comandata dal programma).</summary>
        public bool TransmittingNow => Telemetry.Transmitting || _pttActual;

        /// <summary>Trasmissione remota bloccata: qualcuno ha trasmesso dalla radio (o dal programma sul server).</summary>
        public bool LocalLockActive => _s.LocalPriority && DateTime.UtcNow < _localLockUntil;
        public int LocalLockSeconds => LocalLockActive ? (int)Math.Ceiling((_localLockUntil - DateTime.UtcNow).TotalSeconds) : 0;

        /// <summary>L'operatore alla stazione ha trasmesso: la trasmissione remota resta bloccata per qualche minuto.</summary>
        public void NoteLocalOperator(string how)
        {
            if (!_s.LocalPriority) return;
            bool was = LocalLockActive;
            _localLockUntil = DateTime.UtcNow.AddMinutes(Math.Clamp(_s.LocalPriorityMin, 1, 30));
            if (!was)
            {
                SessionLog.Write($"PRECEDENZA trasmissione locale ({how}): trasmissione remota bloccata per {_s.LocalPriorityMin} minuti");
                Notice?.Invoke($"Operatore alla radio ({how}): la trasmissione remota è bloccata per {_s.LocalPriorityMin} minuti.");
            }
        }

        public void ClearLocalLock(string who)
        {
            if (!LocalLockActive) return;
            _localLockUntil = DateTime.MinValue;
            SessionLog.Write("PRECEDENZA blocco della trasmissione remota tolto " + who);
            PushTelemetry();
        }

        /// <summary>Cambio di frequenza o modo rifiutato durante la trasmissione (se l'opzione è attiva).</summary>
        private bool QsyRefused(string what)
        {
            if (!_s.QsyLockInTx || !TransmittingNow) return false;
            SessionLog.Write($"QSY rifiutato in trasmissione: {what}");
            Notice?.Invoke($"In trasmissione non cambio {what}: rilascia il PTT e riprova.");
            return true;
        }

        public bool IsConfigured => _s.CatViaTci || _s.UseExternalRigctld || (_s.RigModel > 0 && !string.IsNullOrEmpty(_s.RigCom));

        /// <summary>CAT tramite TCI attivo.</summary>
        public bool CatViaTci => _tciLink != null;

        public void Start()
        {
            Stop();
            if (_s.CatViaTci)
            {
                _tciLink = new TciLink(_s.TciHost, _s.TciPort);
                SessionLog.Write($"RADIO CAT via TCI {_s.TciHost}:{_s.TciPort}");
            }
            else if (_s.PttMethod == PttMethod.Tci) _tci = new TciClient(_s.TciHost, _s.TciPort);
            OpenKeyingPorts();
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
            _tciBridge?.Dispose(); _tciBridge = null;
            _tciLink?.Dispose(); _tciLink = null;
            if (_dummyProc != null)
            {
                try { if (!_dummyProc.HasExited) _dummyProc.Kill(true); } catch { }
                _dummyProc.Dispose();
                _dummyProc = null;
            }
            _keyer?.Dispose(); _keyer = null;
            _manual?.Dispose(); _manual = null;
            _pttLines?.Dispose(); _pttLines = null;
            _cwLines?.Dispose(); _cwLines = null;
            _pttWanted = false; _cwPtt = false; _pttActual = false;
            Telemetry = new RigTelemetry();
        }

        public void Dispose() => Stop();

        public KeyingPlan Plan => _plan;

        /// <summary>Indirizzo del rigctld in uso (per i programmi digitali del client), null se non è avviato.</summary>
        public (string host, int port)? RigctldEndpoint
        {
            get
            {
                if (_tciBridge != null) return ("127.0.0.1", _tciBridge.Port);
                if (_s.UseExternalRigctld) return (_s.ExternalRigctldHost, _s.ExternalRigctldPort);
                try { if (_procPort > 0 && _proc != null && !_proc.HasExited) return ("127.0.0.1", _procPort); } catch { }
                return null;
            }
        }

        public string PttInfo
        {
            get
            {
                string ptt = _s.PttMethod switch
                {
                    PttMethod.Cat => _s.CatViaTci ? "CAT (TCI)" : "CAT",
                    PttMethod.Rts or PttMethod.Dtr => $"{KeyingPlan.Name(_plan.PttLine)} {_plan.PttPort}",
                    PttMethod.Vox => "VOX",
                    PttMethod.Tci => $"TCI {_s.TciHost}:{_s.TciPort}",
                    _ => ""
                };
                string cw = _plan.CwSerial ? $"CW {KeyingPlan.Name(_plan.CwLine)} {_plan.CwPort}" : "CW via CAT";
                return $"PTT {ptt} · {cw}";
            }
        }

        /// <summary>Apre le porte seriali per PTT (se non le gestisce rigctld) e per il tasto CW.</summary>
        private void OpenKeyingPorts()
        {
            _plan = KeyingPlan.Resolve(_s);
            if (_plan.Note.Length > 0) SessionLog.Write("PTT/CW " + _plan.Note);
            try
            {
                if ((_s.PttMethod == PttMethod.Rts || _s.PttMethod == PttMethod.Dtr) && !_plan.PttViaRigctld && !string.IsNullOrEmpty(_plan.PttPort))
                {
                    _pttLines = SerialLines.Acquire(_plan.PttPort);
                    SessionLog.Write($"PTT su {KeyingPlan.Name(_plan.PttLine)} della porta {_plan.PttPort}");
                }
            }
            catch (Exception ex) { SetStatus($"Porta PTT {_plan.PttPort} non apribile: {ex.Message}"); SessionLog.Write("PTT porta non apribile: " + ex.Message); }
            try
            {
                if (_plan.CwSerial)
                {
                    _cwLines = SerialLines.Acquire(_plan.CwPort);
                    var lines = _cwLines;
                    var line = _plan.CwLine;
                    _keyer = new CwKeyer(down => lines.Set(line, down && !_txBlocked), on => SetCwPtt(on), () => _s.PttDelayMs);
                    _keyer.Log += m => SessionLog.Write(m);
                    _manual = new ManualCwReplayer(down => lines.Set(line, down && !_txBlocked), on => SetCwPtt(on), _s.CwKeyDelayMs);
                    SessionLog.Write($"CW su {KeyingPlan.Name(line)} della porta {_plan.CwPort}");
                }
            }
            catch (Exception ex) { SetStatus($"Porta CW {_plan.CwPort} non apribile: {ex.Message}"); SessionLog.Write("CW porta non apribile: " + ex.Message); }
        }

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
            if (_tciLink != null)
            {
                // CAT via TCI: il traduttore fa da rigctld; il «Dummy» serve solo ai programmi digitali
                if (_tciBridge == null)
                {
                    int dp = FreeLocalPort();
                    _dummyProc = Hamlib.StartDummyRigctld(_s, dp);
                    if (_dummyProc == null) { dp = 0; SessionLog.Write("TCI: rigctld non trovato, i programmi digitali avranno risposte ridotte"); }
                    else await Task.Delay(800, ct);
                    _tciBridge = new TciRigctl(_tciLink, dp);
                }
                if (!_tciLink.Ready) throw new IOException(_tciLink.Status);
                host = "127.0.0.1";
                port = _tciBridge.Port;
            }
            else if (_s.UseExternalRigctld)
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
            SetStatus(_tciLink != null ? _tciLink.Status : "CAT OK");
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
            if (_tciLink != null && !_tciLink.Ready) throw new IOException(_tciLink.Status);
            var t = Telemetry.Clone();
            bool slow = _cycle++ % 4 == 0;
            var f = await CommandAsync("f", 1, ct);
            if (f != null && f.Count > 0 && long.TryParse(f[0], out long hz)) t.FreqHz = hz;
            var m = await CommandAsync("m", 2, ct);
            if (m != null && m.Count > 0) t.Mode = m[0];
            if (m != null && m.Count > 1 && int.TryParse(m[1], out int pb)) t.PassbandHz = pb;

            t.Transmitting = (await GetAsync("t", ct)) == "1";
            t.StrengthDb = ParseF(await GetAsync("l STRENGTH", ct));
            bool txNow = t.Transmitting || _pttActual;   // anche se la radio non risponde al comando «t»
            CheckTxTime(t);
            CheckLocalTx(t);

            if (txNow || slow)
            {
                t.Swr = ParseF(await GetAsync("l SWR", ct));
                if (txNow) CheckSwr(t.Swr); else _highSwrCount = 0;
                if (_wattsMeter != false)
                {
                    float w = ParseF(await GetAsync("l RFPOWER_METER_WATTS", ct));
                    _wattsMeter = !float.IsNaN(w);
                    t.PowerW = w;
                }
                if (_wattsMeter == false) t.PowerRel = ParseF(await GetAsync("l RFPOWER_METER", ct));
                t.Alc = txNow ? ParseF(await GetAsync("l ALC", ct)) : float.NaN;
            }
            if (_ctlRefresh || _cycle % 8 == 1)
            {
                _ctlRefresh = false;
                t.Nr = FuncOn(await GetAsync("u NR", ct));
                t.Nb = FuncOn(await GetAsync("u NB", ct));
                t.Anf = FuncOn(await GetAsync("u ANF", ct));
                t.Comp = FuncOn(await GetAsync("u COMP", ct));
                t.Att = LevelInt(await GetAsync("l ATT", ct));
                t.Preamp = LevelInt(await GetAsync("l PREAMP", ct));
                t.Agc = LevelInt(await GetAsync("l AGC", ct));
                t.Sql = ParseF(await GetAsync("l SQL", ct));
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
            t.RigName = _tciLink != null ? "TCI · " + (_tciLink.Device.Length > 0 ? _tciLink.Device : _tciLink.Endpoint) : _s.RigModelName;
            t.PttInfo = PttInfo;
            t.PttDelayMs = _s.PttDelayMs;
            t.Status = _tci != null && _s.PttMethod == PttMethod.Tci ? _tci.Status : Status;
            FillSwrFields(t);
            Telemetry = t;
            Updated?.Invoke();
        }

        private static int FuncOn(string? v) => v == null ? -1 : v.Split('\n')[0].Trim() == "1" ? 1 : 0;
        private static int LevelInt(string? v) { float f = ParseF(v); return float.IsNaN(f) ? -1 : (int)Math.Round(f); }

        // ------------------------------------------------------------------ tempo massimo di trasmissione

        /// <summary>
        /// Conta la durata delle trasmissioni comandate dal programma (PTT, messaggi, tasto CW su linea seriale).
        /// Oltre il tempo massimo il PTT si toglie e resta tolto finché l'operatore non lo rilascia.
        /// </summary>
        private void CheckTxTime(RigTelemetry t)
        {
            bool own = _pttActual;
            var now = DateTime.UtcNow;
            if (own && !_txWasOn) _txSince = now;
            _txWasOn = own;
            t.TxSeconds = own ? (int)(now - _txSince).TotalSeconds : 0;
            t.TxLimit = _s.TxTimeoutEnabled ? Math.Clamp(_s.TxTimeoutSec, 30, 600) : 0;
            if (own && t.TxLimit > 0 && t.TxSeconds >= t.TxLimit && !_txTimedOut)
            {
                _txTimedOut = true;
                SessionLog.Write($"TEMPO MASSIMO di trasmissione raggiunto ({t.TxLimit} s): PTT tolto");
                Notice?.Invoke($"Tempo massimo di trasmissione ({t.TxLimit} s): ho tolto il PTT. Rilascialo e premilo di nuovo per trasmettere ancora.");
                _ = Task.Run(StopTxAsync);
            }
        }

        /// <summary>Il PTT è stato rilasciato: dopo il tempo massimo si può trasmettere di nuovo.</summary>
        private void ResetTimeoutIfReleased()
        {
            if (_txTimedOut && !_pttWanted && !_cwPtt) { _txTimedOut = false; PushTelemetry(); }
        }

        // ------------------------------------------------------------------ precedenza a chi è alla radio

        /// <summary>
        /// La radio trasmette ma il programma non l'ha comandata (né ora né negli ultimi secondi, per il VOX che
        /// rilascia in ritardo e per il CW mandato al keyer della radio): è l'operatore alla radio.
        /// </summary>
        private void CheckLocalTx(RigTelemetry t)
        {
            var now = DateTime.UtcNow;
            if (_pttActual || _cwPtt) _ownTxUntil = now.AddSeconds(4);
            if (_s.LocalPriority && t.Transmitting && now > _ownTxUntil) NoteLocalOperator("trasmissione dalla radio");
        }

        // ------------------------------------------------------------------ protezione ROS

        /// <summary>
        /// ROS letto in trasmissione. Da SwrTrip in su per due letture di fila (circa un secondo) la trasmissione
        /// si blocca: due letture evitano blocchi per un valore sbagliato all'inizio della trasmissione.
        /// </summary>
        private void CheckSwr(float swr)
        {
            if (!_s.SwrProtection || _txBlocked) { _highSwrCount = 0; return; }
            if (float.IsNaN(swr))
            {
                if (!_swrMissingLogged) { _swrMissingLogged = true; SessionLog.Write("ROS: la radio non fornisce il valore (l SWR): protezione ROS non operativa con questa radio"); }
                _highSwrCount = 0;
                return;
            }
            if (swr >= _s.SwrTrip)
            {
                if (++_highSwrCount >= 2) BlockTx(swr);
            }
            else _highSwrCount = 0;
        }

        private void BlockTx(float swr)
        {
            _txBlocked = true;
            _blockedSwr = swr;
            _highSwrCount = 0;
            SessionLog.Write($"ROS {swr.ToString("F1", CultureInfo.InvariantCulture)}: trasmissione BLOCCATA (soglia {Inv(_s.SwrTrip)})");
            SetStatus($"Trasmissione bloccata: ROS {swr.ToString("F1", CultureInfo.InvariantCulture)}");
            _ = Task.Run(StopTxAsync);
        }

        /// <summary>Toglie il PTT, ferma il keyer CW e alza il tasto.</summary>
        private async Task StopTxAsync()
        {
            try
            {
                _keyer?.Stop();
                try { _cwLines?.Set(_plan.CwLine, false); } catch { }
                await ApplyPttAsync();                 // con il blocco attivo il PTT va comunque a riposo
                if (_keyer == null) await StopMorseAsync();
            }
            catch (Exception ex) { SessionLog.Write("ROS: arresto della trasmissione non riuscito: " + ex.Message); }
            PushTelemetry();
        }

        /// <summary>Sblocco chiesto dall'operatore (popup) o protezione disattivata.</summary>
        public void UnlockTx(string who)
        {
            if (!_txBlocked) return;
            _txBlocked = false;
            _blockedSwr = float.NaN;
            _highSwrCount = 0;
            SessionLog.Write("ROS: trasmissione sbloccata " + who);
            SetStatus("Trasmissione sbloccata: controlla antenna e cavi prima di trasmettere");
            PushTelemetry();
        }

        /// <summary>Manda subito lo stato della protezione (dopo blocco, sblocco o cambio delle impostazioni).</summary>
        public void PushTelemetry()
        {
            var t = Telemetry.Clone();
            FillSwrFields(t);
            t.Status = Status;
            Telemetry = t;
            Updated?.Invoke();
        }

        private void FillSwrFields(RigTelemetry t)
        {
            t.SwrProtect = _s.SwrProtection;
            t.SwrWarnAt = (float)_s.SwrWarn;
            t.SwrTripAt = (float)_s.SwrTrip;
            t.TxBlocked = _txBlocked;
            t.TxBlockedSwr = _blockedSwr;
            t.TxTimedOut = _txTimedOut;
            t.LocalLockSec = LocalLockSeconds;
            t.QsyLock = _s.QsyLockInTx;
            if (t.TxLimit == 0 && _s.TxTimeoutEnabled) t.TxLimit = Math.Clamp(_s.TxTimeoutSec, 30, 600);
            if (!_s.TxTimeoutEnabled) t.TxLimit = 0;
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

        // ------------------------------------------------------------------ comandi della radio

        /// <summary>
        /// NR, NB, ANF (notch automatico), COMP: 0/1. SQL: 0..1. ATT, PREAMP, AGC: valore -1 = passo successivo
        /// (si provano i valori in ordine e si tiene il primo che la radio accetta, poi si torna a spento).
        /// </summary>
        public async Task SetRadioCtlAsync(string name, float value)
        {
            var t = Telemetry;
            switch (name)
            {
                case "NR": case "NB": case "ANF": case "COMP":
                    await SetAsync($"U {name} {(value > 0.5f ? 1 : 0)}");
                    break;
                case "SQL":
                    await SetAsync("L SQL " + Inv(Math.Round(Math.Clamp(value, 0f, 1f), 2)));
                    break;
                case "ATT":
                case "PREAMP":
                {
                    // valore successivo: il primo che la radio accetta e che, riletto, è più alto di quello attuale
                    // (le radio con un solo valore accettano qualunque numero: rileggendo si torna a spento)
                    int cur = Math.Max(0, name == "ATT" ? t.Att : t.Preamp);
                    bool done = false;
                    var ct = _cts?.Token ?? CancellationToken.None;
                    foreach (int v in DbSteps)
                    {
                        if (v <= cur) continue;
                        if (!await SetAsync($"L {name} {v}")) continue;
                        _unsupported.Remove("l " + name);
                        float rb = ParseF(await GetAsync("l " + name, ct));
                        if (float.IsNaN(rb) || rb > cur) { done = true; break; }
                    }
                    if (!done) await SetAsync($"L {name} 0");
                    break;
                }
                case "AGC":
                {
                    int i = Array.IndexOf(AgcCycle, t.Agc);
                    for (int k = 1; k <= AgcCycle.Length; k++)
                        if (await SetAsync("L AGC " + AgcCycle[(Math.Max(i, -1) + k + AgcCycle.Length) % AgcCycle.Length])) break;
                    break;
                }
                default: return;
            }
            SessionLog.Write($"RADIO comando {name} {(value < 0 ? "passo successivo" : value.ToString("0.##", CultureInfo.InvariantCulture))}");
            _unsupported.Remove(name is "NR" or "NB" or "ANF" or "COMP" ? "u " + name : "l " + name);
            _ctlRefresh = true;
        }

        // ------------------------------------------------------------------ comandi

        public async Task SetFreqModeAsync(long hz, string? mode)
        {
            if (QsyRefused(hz > 0 ? "frequenza" : "modo")) return;
            if (hz > 0) await SetAsync("F " + hz.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(mode)) await SetAsync($"M {mode.Trim()} 0");
        }

        /// <summary>Modo con larghezza del filtro (es. CW 3000 Hz per vedere tutta la cascata).</summary>
        public async Task SetPassbandAsync(string mode, int hz)
        {
            if (string.IsNullOrWhiteSpace(mode)) return;
            if (QsyRefused("filtro")) return;
            await SetAsync($"M {mode.Trim()} {Math.Clamp(hz, 0, 10000)}");
            SessionLog.Write($"RADIO filtro {mode} a {hz} Hz");
        }

        public async Task SetVfoBAsync(long hz)
        {
            if (QsyRefused("la frequenza del VFO B")) return;
            await SetAsync("I " + hz.ToString(CultureInfo.InvariantCulture));
        }

        public async Task SetSplitAsync(bool on)
        {
            if (QsyRefused("lo split")) { PushTelemetry(); return; }
            await SetAsync(on ? "S 1 VFOB" : "S 0 VFOA");
            _cycle = 0;
        }

        public async Task VfoOpAsync(string op)
        {
            if (op != "XCHG" && op != "CPY") return;
            if (QsyRefused("i VFO")) return;
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
            // il keyer della radio trasmette da solo: per la durata stimata (circa 12 punti per carattere)
            // la trasmissione è del programma, non di un operatore alla radio
            int wpm = Telemetry.KeySpeed >= 5 ? Telemetry.KeySpeed : 18;
            var until = DateTime.UtcNow.AddSeconds(text.Length * 12.0 * 1.2 / wpm + 4);
            if (until > _ownTxUntil) _ownTxUntil = until;
            if (!await SetAsync("b " + text)) SetStatus("La radio non accetta testo CW via CAT (send_morse)");
        }

        public Task StopMorseAsync() => SetAsync("\\stop_morse");

        /// <summary>PTT dall'operatore (pulsante, barra, messaggi WAV).</summary>
        public async Task SetPttAsync(bool on)
        {
            if (_pttWanted == on) return;
            _pttWanted = on;
            if (!on) ResetTimeoutIfReleased();
            await ApplyPttAsync();
        }

        /// <summary>PTT chiesto dal tasto CW seriale (dal suo thread: attende che il PTT sia applicato).</summary>
        private void SetCwPtt(bool on)
        {
            _cwPtt = on;
            if (!on) ResetTimeoutIfReleased();
            try { ApplyPttAsync().GetAwaiter().GetResult(); } catch { }
        }

        /// <summary>PTT: col metodo VOX non invia comandi; con TCI usa il WebSocket; RTS/DTR direttamente o tramite rigctld.</summary>
        private async Task ApplyPttAsync()
        {
            await _pttLock.WaitAsync();
            try
            {
                bool on = !_txBlocked && !_txTimedOut && (_pttWanted || _cwPtt);
                if (on == _pttActual) return;
                _pttActual = on;
                _ownTxUntil = DateTime.UtcNow.AddSeconds(4);   // la radio può restare in TX ancora un attimo (VOX, ritardi)
                switch (_s.PttMethod)
                {
                    case PttMethod.Vox: return;
                    case PttMethod.Tci when _tciLink != null:
                        await _tciLink.SetTrxAsync(on);
                        return;
                    case PttMethod.Tci:
                        _tci ??= new TciClient(_s.TciHost, _s.TciPort);
                        await _tci.SetPttAsync(on);
                        return;
                    case PttMethod.Rts:
                    case PttMethod.Dtr:
                        if (_pttLines != null) { _pttLines.Set(_plan.PttLine, on); return; }
                        break;
                }
                var ct = _cts?.Token ?? CancellationToken.None;
                await CommandAsync(on ? "T 1" : "T 0", 1, ct);
            }
            catch (Exception ex) { SetStatus("PTT non riuscito: " + ex.Message); }
            finally { _pttLock.Release(); }
        }

        /// <summary>Testo CW dal client: tasto seriale (DTR/RTS) se impostato, altrimenti keyer della radio via CAT.</summary>
        public async Task SendCwAsync(string text, int wpm)
        {
            if (_txBlocked) { SetStatus("Trasmissione bloccata per ROS alto: testo CW non inviato"); return; }
            if (_txTimedOut) return;
            if (_keyer != null) { _keyer.Send(text, wpm); return; }
            if (wpm >= 5) await SetKeySpeedAsync(wpm);
            await SendMorseAsync(text);
        }

        /// <summary>Tasto CW manuale del client (evento giù/su con il suo istante).</summary>
        public void CwKeyEvent(bool down, long clientMs)
        {
            if (_txBlocked && down) return;
            if (_manual == null)
            {
                if (down) SetStatus("Tasto CW manuale: sul server imposta l'invio CW su una linea DTR o RTS");
                return;
            }
            _manual.Event(down, clientMs);
        }

        public async Task StopCwAsync()
        {
            if (_keyer != null) { _keyer.Stop(); return; }
            await StopMorseAsync();
        }

        private void SetStatus(string s)
        {
            if (Status == s) return;
            Status = s;
            Updated?.Invoke();
        }
    }
}
