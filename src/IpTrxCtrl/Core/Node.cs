using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Audio;
using IpTrxCtrl.Net;
using IpTrxCtrl.Rig;

namespace IpTrxCtrl.Core
{
    /// <summary>Stato del collegamento mostrato nella finestra.</summary>
    public sealed class LinkStats
    {
        public bool Connected;
        public string Status = "Avvio…";
        public double LatencyMs = double.NaN;
        public double JitterMs;
        public double LossPercent;
        public int BufferMs;
        public long Underruns;
        public bool PeerTx;
        public DateTime ConnectedSince;
        public float RxPeak, TxPeak;
        public int MqttOk = -1;            // broker che hanno risposto (-1 sconosciuto)
        public string ServerEndpoint = "";  // IP pubblico e porte del server
        public string ClientEndpoint = "";  // IP pubblico e di rete locale del client
    }

    /// <summary>
    /// Il cuore del programma: rete, cifratura, ruoli server/client, audio, CAT, PTT, punto d'incontro.
    /// </summary>
    public sealed class Node : IDisposable
    {
        private readonly AppSettings _s;
        private byte[]? _master;
        private Rendezvous? _rdv;

        public AudioEngine Audio { get; } = new();
        public RigControl Rig { get; }
        public LinkStats Stats { get; } = new();
        public RigTelemetry LastTelemetry { get; private set; } = new();
        public DeviceStateInfo DeviceState { get; private set; } = new();
        public NodeRole Role => _s.Role;
        public bool IsPaired => _master != null;
        public string Fingerprint => _master == null ? "" : Crypto.Fingerprint(_master);

        public event Action? StatsChanged;
        public event Action<RigTelemetry>? TelemetryChanged;
        public event Action<DeviceStateInfo>? DeviceStateChanged;
        public event Action<string?>? FallbackCodeChanged;
        public event Action<bool>? NeedFallbackCode;
        public event Action? PairingCompleted;
        public event Action<Action<bool>>? SwapRequested;
        public event Action? RoleChanged;
        public event Action<string>? Notice;
        public event Action<bool>? LocalPttChanged;
        /// <summary>Registro del dialogo di abbinamento (simbolo, testo).</summary>
        public event MqttLog? PairingLog;

        private SecureChannel? _control, _audio;

        // server
        private PortManager? _ports;
        private TcpListener? _ctlListener, _audListener;
        private int _ctlPort, _audPort;
        private CancellationTokenSource? _serverCts;
        private byte[]? _pairingSecret;
        private CancellationTokenSource? _pairListenCts;
        private DateTime _lastRemotePtt = DateTime.MinValue;
        private bool _remotePtt, _localPtt;

        // client
        private CancellationTokenSource? _clientCts;
        private ServerAddress? _fallbackAddr;
        private TaskCompletionSource<int>? _newAudioPortTcs;
        private string _clientPublicIp = "";

        private uint _lastSeq;
        private DateTime _lastArrival = DateTime.MinValue;
        private long _expected, _lost;
        private uint _txSeq;

        private readonly CancellationTokenSource _life = new();

        public Node(AppSettings s)
        {
            _s = s;
            _master = KeyStore.LoadMaster();
            if (_master != null) _rdv = new Rendezvous(_master, _s.MqttBrokers);
            Rig = new RigControl(s);
            Rig.Updated += OnRigUpdated;
            Audio.Captured += OnCaptured;
            Audio.Keyer.PttRequest += on => SetPttLocal(on);
            _ = Task.Run(() => HousekeepingAsync(_life.Token));
        }

        private void Log(char sym, string text)
        {
            PairingLog?.Invoke(sym, text);
            SessionLog.Write($"ABBINAMENTO {sym} {text}");
        }

        // =================================================================== avvio / arresto

        public void Start()
        {
            ApplyAudioSettings();
            Audio.Start(_s.InputDevice, _s.OutputDevice);
            if (_s.Role == NodeRole.Server)
            {
                Rig.Start();
                _ = StartServerAsync();
            }
            else StartClient();
        }

        public void ApplyAudioSettings()
        {
            Audio.RxGain = AudioEngine.GainFrom(_s.RxVolume, _s.RxBoost);
            Audio.TxGain = AudioEngine.GainFrom(_s.TxVolume, _s.TxBoost);
            Audio.Playback.PrefillSamples = Math.Clamp(_s.PrefillMs, 20, 1000) * AudioEngine.SampleRate / 1000;
        }

        public void RestartAudio()
        {
            ApplyAudioSettings();
            Audio.Stop();
            Audio.Start(_s.InputDevice, _s.OutputDevice);
        }

        public void Dispose()
        {
            _life.Cancel();
            Audio.Keyer.Stop();
            SetPttLocal(false);
            StopClient();
            _pairListenCts?.Cancel();
            Task.Run(() => StopServerAsync()).Wait(3000);
            Rig.Stop();
            Audio.Dispose();
        }

        // =================================================================== SERVER

        private async Task StartServerAsync(bool keepChannels = false)
        {
            try { await StartServerCoreAsync(keepChannels); }
            catch (Exception ex) { SetStatus("Errore di rete: " + ex.Message, false); }
        }

        private async Task StartServerCoreAsync(bool keepChannels)
        {
            await StopServerAsync(!keepChannels);
            _serverCts = new CancellationTokenSource();
            var ct = _serverCts.Token;
            _ports = new PortManager();
            SetStatus("Apertura porte sul router…", false);

            bool upnp = await _ports.DiscoverAsync(ct);
            if (upnp)
            {
                _ctlPort = await _ports.OpenRandomPortAsync("IP-TRX-CTRL controllo", ct);
                _audPort = await _ports.OpenRandomPortAsync("IP-TRX-CTRL audio", ct);
            }
            if (_ctlPort == 0) _ctlPort = _ports.PickLocalPort();
            if (_audPort == 0) _audPort = _ports.PickLocalPort();

            _ctlListener = new TcpListener(IPAddress.Any, _ctlPort);
            _audListener = new TcpListener(IPAddress.Any, _audPort);
            _ctlListener.Start();
            _audListener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_ctlListener, ChannelKind.Control, ct));
            _ = Task.Run(() => AcceptLoopAsync(_audListener, ChannelKind.Audio, ct));
            _ = Task.Run(() => PublishLoopAsync(ct));

            if (_ports.CgnatSuspected)
                Notice?.Invoke("Il tuo operatore sembra usare il CGNAT: il server potrebbe non essere raggiungibile da internet.");
            else if (!upnp)
                Notice?.Invoke("UPnP non disponibile sul router: abilitalo, altrimenti il client non potrà collegarsi.");
            UpdateServerEndpoint();
            SetStatus(IsPaired ? "In attesa del client" : "In attesa dell'abbinamento", false);
        }

        private async Task StopServerAsync(bool closeChannels = true)
        {
            _serverCts?.Cancel();
            _serverCts = null;
            try { _ctlListener?.Stop(); } catch { }
            try { _audListener?.Stop(); } catch { }
            _ctlListener = null; _audListener = null;
            if (closeChannels) CloseChannels();
            if (_ports != null) await _ports.CloseAllAsync();
            _ports = null;
            _ctlPort = 0; _audPort = 0;
            _remotePtt = false;
        }

        public ServerAddress? CurrentServerAddress =>
            _ports?.ExternalIp != null && _ctlPort > 0 && _audPort > 0
                ? new ServerAddress(_ports.ExternalIp, _ctlPort, _audPort) : null;

        private void UpdateServerEndpoint()
        {
            var a = CurrentServerAddress;
            Stats.ServerEndpoint = a == null ? "—" : $"{a.Value.Ip} · porte {a.Value.ControlPort}/{a.Value.AudioPort} · LAN {LanIp()}";
        }

        public static string LanIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address)
                            && PortManager.IsPrivateOrCgnat(ua.Address))
                            return ua.Address.ToString();
                }
            }
            catch { }
            return "—";
        }

        /// <summary>Server: prepara l'abbinamento, avvia l'ascolto sui broker MQTT e restituisce il codice.</summary>
        public async Task<string?> BeginPairingServerAsync(CancellationToken ct)
        {
            Log('·', "preparo le porte e l'indirizzo pubblico…");
            for (int i = 0; i < 40 && CurrentServerAddress == null && !ct.IsCancellationRequested; i++)
                await Task.Delay(500, ct);
            var addr = CurrentServerAddress;
            if (addr == null)
            {
                Log('✗', "indirizzo pubblico non disponibile: controlla la connessione a internet");
                return null;
            }
            Log('·', $"indirizzo pubblico {addr.Value.Ip} · porte {addr.Value.ControlPort} / {addr.Value.AudioPort} · UPnP {(_ports?.UpnpAvailable == true ? "ok" : "non disponibile")}");
            _pairingSecret = RandomNumberGenerator.GetBytes(Crypto.PairingSecretSize);
            string code = Crypto.MakePairingCode(_pairingSecret, addr.Value);

            _pairListenCts?.Cancel();
            _pairListenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var secret = _pairingSecret;
            var lct = _pairListenCts.Token;
            _ = Task.Run(() => PairingRendezvous.ServerListenAsync(secret,
                () => (CurrentServerAddress, _ports?.UpnpAvailable == true), _s.MqttBrokers, Log, lct));
            return code;
        }

        public void CancelPairing()
        {
            _pairingSecret = null;
            _pairListenCts?.Cancel();
        }

        private async Task AcceptLoopAsync(TcpListener listener, ChannelKind kind, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var res = await SecureChannel.AcceptAsync(tcp, kind, _master, _pairingSecret, ct);
                        if (res.NewMaster != null)
                        {
                            SetMaster(res.NewMaster);
                            _pairingSecret = null;
                            _pairListenCts?.Cancel();
                            Log('◀', "handshake ECDH P-256 ok · chiave AES-256 derivata");
                            Log('✓', "Abbinamento completato");
                            PairingCompleted?.Invoke();
                            _ = PublishNowAsync(ct);
                            return;
                        }
                        if (res.Channel == null) return;
                        if (kind == ChannelKind.Control)
                        {
                            var old = Interlocked.Exchange(ref _control, res.Channel);
                            old?.Dispose();
                            OnConnected();
                            await SendHelloAsync(res.Channel);
                            try { await res.Channel.SendAsync(new MsgWriter(Msg.PeerInfo).Str(res.Channel.Remote.Address.MapToIPv4().ToString()).Build(), ct); } catch { }
                            Stats.ClientEndpoint = res.Channel.Remote.Address.MapToIPv4().ToString();
                            _ = SendDeviceStateAsync();
                            await ReceiveLoopAsync(res.Channel, ct);
                        }
                        else
                        {
                            var old = Interlocked.Exchange(ref _audio, res.Channel);
                            old?.Dispose();
                            ResetAudioStats();
                            await ReceiveLoopAsync(res.Channel, ct);
                        }
                    }
                    catch { }
                }, ct);
            }
        }

        private async Task PublishLoopAsync(CancellationToken ct)
        {
            IPAddress? lastIp = null;
            DateTime lastPublish = DateTime.MinValue;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_ports != null)
                    {
                        var ip = await _ports.RefreshExternalIpAsync(ct);
                        UpdateServerEndpoint();
                        bool changed = ip != null && !ip.Equals(lastIp);
                        if (_master != null && (changed || DateTime.UtcNow - lastPublish > TimeSpan.FromMinutes(10)))
                        {
                            await PublishNowAsync(ct);
                            lastIp = ip;
                            lastPublish = DateTime.UtcNow;
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(60), ct);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        private async Task PublishNowAsync(CancellationToken ct)
        {
            var addr = CurrentServerAddress;
            if (addr == null || _rdv == null || _master == null) return;
            bool ok = await _rdv.PublishAsync(addr.Value, ct);
            Stats.MqttOk = _rdv.LastOkBrokers;
            FallbackCodeChanged?.Invoke(ok ? null : Crypto.MakeFallbackCode(_master, addr.Value));
        }

        private async Task ReplaceAudioPortAsync(SecureChannel control, CancellationToken ct)
        {
            if (_ports == null) return;
            try { _audListener?.Stop(); } catch { }
            await _ports.ClosePortAsync(_audPort);
            int p = _ports.UpnpAvailable ? await _ports.OpenRandomPortAsync("IP-TRX-CTRL audio", ct) : 0;
            if (p == 0) p = _ports.PickLocalPort();
            _audPort = p;
            _audListener = new TcpListener(IPAddress.Any, p);
            _audListener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_audListener, ChannelKind.Audio, ct));
            await control.SendAsync(new MsgWriter(Msg.NewAudioPort).I32(p).Build(), ct);
            UpdateServerEndpoint();
            _ = PublishNowAsync(ct);
        }

        // =================================================================== CLIENT

        private void StartClient()
        {
            StopClient();
            _clientCts = new CancellationTokenSource();
            var ct = _clientCts.Token;
            _ = Task.Run(() => ClientLoopAsync(ct));
        }

        private void StopClient()
        {
            _clientCts?.Cancel();
            _clientCts = null;
            if (_s.Role == NodeRole.Client) CloseChannels();
        }

        /// <summary>
        /// Client: abbinamento. Cerca il server sui broker MQTT (indirizzo aggiornato), poi apre il
        /// collegamento diretto; se nessun broker risponde usa l'indirizzo scritto nel codice.
        /// </summary>
        public async Task<string?> PairClientAsync(string code, CancellationToken ct)
        {
            if (!Crypto.TryParsePairingCode(code, out byte[] secret, out ServerAddress addr))
            {
                Log('✗', "codice non valido: controlla di averlo scritto bene");
                return "Codice non valido: controlla di averlo scritto bene.";
            }
            Log('·', $"codice letto · indirizzo nel codice {addr.Ip} · porte {addr.ControlPort} / {addr.AudioPort}");
            var found = await PairingRendezvous.ClientFindAsync(secret, _s.Callsign, _s.MqttBrokers, Log, ct);
            if (found != null) addr = found.Value;
            else Log('·', "nessuna risposta dai broker: provo l'indirizzo scritto nel codice");

            try
            {
                Log('▶', $"TCP {addr.Ip}:{addr.ControlPort}  apertura canale di controllo");
                byte[] master = await SecureChannel.PairAsClientAsync(addr, secret, ct);
                Log('◀', "handshake ECDH P-256 ok · chiave AES-256 derivata");
                SetMaster(master);
                RememberServer(addr);
                Log('✓', "Abbinamento completato");
                PairingCompleted?.Invoke();
                StartClient();
                return null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Log('✗', $"la porta {addr.ControlPort} del server non risponde: UPnP disattivato sul router o rete con IP condiviso (CGNAT)");
                return "Abbinamento non riuscito: il server non è raggiungibile (vedi il registro).";
            }
            catch (SocketException ex)
            {
                Log('✗', $"connessione rifiutata ({ex.SocketErrorCode}): il programma server è chiuso o la porta non è aperta");
                return "Abbinamento non riuscito: connessione rifiutata (vedi il registro).";
            }
            catch (Exception ex)
            {
                Log('✗', ex.Message);
                return "Abbinamento non riuscito: " + ex.Message;
            }
        }

        public bool UseFallbackCode(string code)
        {
            if (_master == null || !Crypto.TryParseFallbackCode(_master, code, out var addr)) return false;
            _fallbackAddr = addr;
            return true;
        }

        private void RememberServer(ServerAddress a)
        {
            _s.LastServerIp = a.Ip.ToString();
            _s.LastServerControlPort = a.ControlPort;
            _s.LastServerAudioPort = a.AudioPort;
            _s.Save();
        }

        private ServerAddress? LastServer =>
            IPAddress.TryParse(_s.LastServerIp, out var ip) && _s.LastServerControlPort > 0
                ? new ServerAddress(ip, _s.LastServerControlPort, _s.LastServerAudioPort) : null;

        private async Task ClientLoopAsync(CancellationToken ct)
        {
            int failedRounds = 0;
            while (!ct.IsCancellationRequested)
            {
                if (_master == null)
                {
                    SetStatus("In attesa dell'abbinamento", false);
                    try { await Task.Delay(1000, ct); } catch { break; }
                    continue;
                }

                ServerAddress? target;
                if (_fallbackAddr != null) { target = _fallbackAddr; _fallbackAddr = null; }
                else target = LastServer;

                SecureChannel? ctl = null;
                if (target != null) ctl = await TryConnectAsync(target.Value, ChannelKind.Control, ct);

                if (ctl == null && _rdv != null)
                {
                    SetStatus("Cerco il server…", false);
                    var found = await _rdv.LookupAsync(ct);
                    Stats.MqttOk = _rdv.LastOkBrokers;
                    if (found != null && !found.Equals(target))
                    {
                        target = found;
                        ctl = await TryConnectAsync(found.Value, ChannelKind.Control, ct);
                    }
                }

                if (ctl == null)
                {
                    failedRounds++;
                    SetStatus("Server non raggiungibile, riprovo…", false);
                    if (failedRounds >= 2) NeedFallbackCode?.Invoke(true);
                    try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 5 * failedRounds)), ct); } catch { break; }
                    continue;
                }

                failedRounds = 0;
                NeedFallbackCode?.Invoke(false);
                var addr = target!.Value;
                RememberServer(addr);
                _control = ctl;
                Stats.ServerEndpoint = $"{addr.Ip} · porte {addr.ControlPort}/{addr.AudioPort}";
                UpdateClientEndpoint();
                OnConnected();
                await SendHelloAsync(ctl);
                var recv = Task.Run(() => ReceiveLoopAsync(ctl, ct));

                for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
                {
                    var aud = await TryConnectAsync(addr, ChannelKind.Audio, ct);
                    if (aud != null)
                    {
                        _audio = aud;
                        ResetAudioStats();
                        _ = Task.Run(() => ReceiveLoopAsync(aud, ct));
                        SessionLog.Write($"TEST porte: controllo e audio raggiungibili su {addr.Ip}");
                        break;
                    }
                    SessionLog.Write($"TEST porte: porta audio {addr.AudioPort} non raggiungibile, chiedo una nuova porta");
                    _newAudioPortTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    try { await ctl.SendAsync(new MsgWriter(Msg.AudioUnreachable).Build(), ct); } catch { break; }
                    var done = await Task.WhenAny(_newAudioPortTcs.Task, Task.Delay(15000, ct));
                    if (done != _newAudioPortTcs.Task) break;
                    addr = addr with { AudioPort = _newAudioPortTcs.Task.Result };
                    RememberServer(addr);
                    Stats.ServerEndpoint = $"{addr.Ip} · porte {addr.ControlPort}/{addr.AudioPort}";
                }

                try { await recv; } catch { }
                if (ct.IsCancellationRequested) break;
                CloseChannels();
                SetStatus("Collegamento interrotto, riconnessione…", false);
                try { await Task.Delay(2000, ct); } catch { break; }
            }
        }

        private void UpdateClientEndpoint()
        {
            string lan = _control?.Local?.Address.MapToIPv4().ToString() ?? LanIp();
            Stats.ClientEndpoint = (_clientPublicIp.Length > 0 ? _clientPublicIp : "—") + " · LAN " + lan;
        }

        private async Task<SecureChannel?> TryConnectAsync(ServerAddress a, ChannelKind kind, CancellationToken ct)
        {
            try { return await SecureChannel.ConnectAsync(a, kind, _master!, ct); }
            catch { return null; }
        }

        // =================================================================== messaggi

        private async Task SendHelloAsync(SecureChannel ch)
        {
            try { await ch.SendAsync(new MsgWriter(Msg.Hello).Str(_s.Callsign).Build()); } catch { }
        }

        private bool IsServer => _s.Role == NodeRole.Server;

        private async Task ReceiveLoopAsync(SecureChannel ch, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    byte[]? data = await ch.ReceiveAsync(ct);
                    if (data == null || data.Length == 0) break;
                    var r = new MsgReader(data);
                    switch (r.Type)
                    {
                        case Msg.Audio: OnAudioPacket(r); break;
                        case Msg.Hello:
                            _s.PeerCallsign = r.Str(); _s.Save(); StatsChanged?.Invoke(); break;
                        case Msg.PeerInfo:
                            _clientPublicIp = r.Str(); UpdateClientEndpoint(); StatsChanged?.Invoke(); break;
                        case Msg.Ping:
                            await ch.SendAsync(new MsgWriter(Msg.Pong).I64(r.I64()).Build(), ct); break;
                        case Msg.Pong:
                            Stats.LatencyMs = (DateTime.UtcNow.Ticks - r.I64()) / (double)TimeSpan.TicksPerMillisecond;
                            StatsChanged?.Invoke(); break;
                        case Msg.Telemetry:
                            if (!IsServer)
                            {
                                var tel = RigTelemetry.Decode(r);
                                Stats.PeerTx = tel.Transmitting;
                                LastTelemetry = tel;
                                Audio.Keyer.DelayMs = tel.PttDelayMs;
                                TelemetryChanged?.Invoke(tel);
                            }
                            break;
                        case Msg.DeviceState:
                            if (!IsServer) { DeviceState = DeviceStateInfo.Decode(r); DeviceStateChanged?.Invoke(DeviceState); }
                            break;
                        case Msg.SetFreqMode:
                            if (IsServer) { long hz = r.I64(); string mode = r.Str(); _ = Rig.SetFreqModeAsync(hz, mode); }
                            break;
                        case Msg.SetVfoB: if (IsServer) _ = Rig.SetVfoBAsync(r.I64()); break;
                        case Msg.SetSplit: if (IsServer) _ = Rig.SetSplitAsync(r.Bool()); break;
                        case Msg.VfoOp: if (IsServer) _ = Rig.VfoOpAsync(r.Str()); break;
                        case Msg.SetPower: if (IsServer) { float v = r.F32(); bool w = r.Bool(); _ = Rig.SetPowerAsync(v, w); } break;
                        case Msg.SetPitch: if (IsServer) _ = Rig.SetPitchAsync(r.I32()); break;
                        case Msg.SendMorse: if (IsServer) _ = Rig.SendMorseAsync(r.Str()); break;
                        case Msg.StopMorse: if (IsServer) _ = Rig.StopMorseAsync(); break;
                        case Msg.SetKeySpeed: if (IsServer) _ = Rig.SetKeySpeedAsync(r.I32()); break;
                        case Msg.SetDeviceMute:
                            if (IsServer) { bool input = r.U8() == 0; bool mute = r.Bool(); DeviceMute.SetMute(input ? _s.InputDevice : _s.OutputDevice, input, mute); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.SetDeviceVolume:
                            if (IsServer) { bool input = r.U8() == 0; float v = r.F32(); DeviceMute.SetVolume(input ? _s.InputDevice : _s.OutputDevice, input, v); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.SetAutoUnmute:
                            if (IsServer) { _s.AutoUnmute = r.Bool(); _s.Save(); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.Ptt:
                            if (IsServer)
                            {
                                _remotePtt = r.U8() == 1;
                                _lastRemotePtt = DateTime.UtcNow;
                                await ApplyPttAsync();
                            }
                            break;
                        case Msg.RadioStatus:
                            Notice?.Invoke(r.Str()); break;
                        case Msg.AudioUnreachable:
                            if (IsServer) await ReplaceAudioPortAsync(ch, ct);
                            break;
                        case Msg.NewAudioPort:
                            _newAudioPortTcs?.TrySetResult(r.I32()); break;
                        case Msg.SwapRequest:
                            SwapRequested?.Invoke(accepted => _ = AnswerSwapAsync(accepted)); break;
                        case Msg.SwapAnswer:
                            if (r.U8() == 1) _ = BeginSwapAsync(); else Notice?.Invoke("Il peer ha rifiutato lo scambio dei ruoli.");
                            break;
                        case Msg.SwapReady:
                            if (IsServer) await FinishSwapAsServerAsync(ServerAddress.FromBytes(r.Bytes(8), 0));
                            break;
                    }
                }
            }
            catch { }
            finally
            {
                if (ReferenceEquals(ch, _control))
                {
                    Interlocked.CompareExchange(ref _control, null, ch);
                    ch.Dispose();
                    Interlocked.Exchange(ref _audio, null)?.Dispose();
                    OnDisconnected();
                }
                else if (ReferenceEquals(ch, _audio))
                {
                    Interlocked.CompareExchange(ref _audio, null, ch);
                    ch.Dispose();
                }
                else ch.Dispose();
            }
        }

        private void OnAudioPacket(MsgReader r)
        {
            uint seq = r.U32();
            byte[] pcm = r.Rest();
            var now = DateTime.UtcNow;
            if (_lastArrival != DateTime.MinValue)
            {
                double delta = (now - _lastArrival).TotalMilliseconds;
                Stats.JitterMs += (Math.Abs(delta - AudioEngine.FrameMs) - Stats.JitterMs) / 16.0;
            }
            _lastArrival = now;
            if (_lastSeq != 0)
            {
                long diff = (long)seq - _lastSeq;
                if (diff > 1) _lost += diff - 1;
                if (diff > 0) _expected += diff;
            }
            else _expected = 1;
            _lastSeq = seq;
            Stats.LossPercent = _expected > 0 ? 100.0 * _lost / _expected : 0;
            Audio.Playback.Write(pcm, 0, pcm.Length);
        }

        private void ResetAudioStats()
        {
            _lastSeq = 0; _expected = 0; _lost = 0; _lastArrival = DateTime.MinValue;
            Stats.JitterMs = 0; Stats.LossPercent = 0;
            Audio.Playback.Reset();
        }

        private void OnCaptured(byte[] pcm, int len)
        {
            var aud = _audio;
            if (aud == null) return;
            // Server: invia sempre l'audio ricevuto dalla radio (non durante la trasmissione). Client: solo con PTT.
            bool send = IsServer ? !Stats.PeerTx : _localPtt;
            if (!send) return;
            byte[] msg = new MsgWriter(Msg.Audio).U32(++_txSeq).Bytes(pcm, 0, len).Build();
            _ = aud.SendAsync(msg).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void OnRigUpdated()
        {
            var t = Rig.Telemetry;
            LastTelemetry = t;
            TelemetryChanged?.Invoke(t);
            var ctl = _control;
            if (ctl != null && IsServer)
                _ = ctl.SendAsync(t.Encode()).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // =================================================================== schede audio del server

        private DateTime _lastDevicePoll = DateTime.MinValue;

        public void RefreshDeviceState() => _ = SendDeviceStateAsync();

        private async Task SendDeviceStateAsync()
        {
            if (!IsServer) return;
            var st = await Task.Run(() =>
            {
                var (im, iv) = DeviceMute.Get(_s.InputDevice, true);
                var (om, ov) = DeviceMute.Get(_s.OutputDevice, false);
                if (_s.AutoUnmute)
                {
                    if (im == 1 && DeviceMute.SetMute(_s.InputDevice, true, false)) { im = 0; Notice?.Invoke("Mute dell'ingresso audio del server tolto in automatico."); }
                    if (om == 1 && DeviceMute.SetMute(_s.OutputDevice, false, false)) { om = 0; Notice?.Invoke("Mute dell'uscita audio del server tolto in automatico."); }
                }
                return new DeviceStateInfo
                {
                    InName = string.IsNullOrEmpty(_s.InputDevice) ? "predefinito" : _s.InputDevice,
                    OutName = string.IsNullOrEmpty(_s.OutputDevice) ? "predefinito" : _s.OutputDevice,
                    InMute = im, InVol = iv, OutMute = om, OutVol = ov, AutoUnmute = _s.AutoUnmute,
                    Note = DeviceMute.PrivacyNote()
                };
            });
            DeviceState = st;
            DeviceStateChanged?.Invoke(st);
            var ctl = _control;
            if (ctl != null) { try { await ctl.SendAsync(st.Encode()); } catch { } }
        }

        // =================================================================== comandi dalla UI

        private void Send(byte[] msg)
        {
            var ctl = _control;
            if (ctl != null) _ = ctl.SendAsync(msg).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public void SetFreqMode(long hz, string mode)
        {
            if (IsServer) { _ = Rig.SetFreqModeAsync(hz, mode); return; }
            Send(new MsgWriter(Msg.SetFreqMode).I64(hz).Str(mode).Build());
        }

        public void SetVfoB(long hz) { if (IsServer) _ = Rig.SetVfoBAsync(hz); else Send(new MsgWriter(Msg.SetVfoB).I64(hz).Build()); }
        public void SetSplit(bool on) { if (IsServer) _ = Rig.SetSplitAsync(on); else Send(new MsgWriter(Msg.SetSplit).Bool(on).Build()); }
        public void VfoOp(string op) { if (IsServer) _ = Rig.VfoOpAsync(op); else Send(new MsgWriter(Msg.VfoOp).Str(op).Build()); }
        public void SetPower(float value, bool watts) { if (IsServer) _ = Rig.SetPowerAsync(value, watts); else Send(new MsgWriter(Msg.SetPower).F32(value).Bool(watts).Build()); }
        public void SetPitch(int hz) { if (IsServer) _ = Rig.SetPitchAsync(hz); else Send(new MsgWriter(Msg.SetPitch).I32(hz).Build()); }
        public void SetKeySpeed(int wpm) { if (IsServer) _ = Rig.SetKeySpeedAsync(wpm); else Send(new MsgWriter(Msg.SetKeySpeed).I32(wpm).Build()); }
        public void SendMorse(string text) { if (IsServer) _ = Rig.SendMorseAsync(text); else Send(new MsgWriter(Msg.SendMorse).Str(text).Build()); }
        public void StopMorse() { if (IsServer) _ = Rig.StopMorseAsync(); else Send(new MsgWriter(Msg.StopMorse).Build()); }

        public void SetRemoteDeviceMute(bool input, bool mute) => Send(new MsgWriter(Msg.SetDeviceMute).U8((byte)(input ? 0 : 1)).Bool(mute).Build());
        public void SetRemoteDeviceVolume(bool input, float v) => Send(new MsgWriter(Msg.SetDeviceVolume).U8((byte)(input ? 0 : 1)).F32(v).Build());
        public void SetRemoteAutoUnmute(bool on) => Send(new MsgWriter(Msg.SetAutoUnmute).Bool(on).Build());

        public void SetPttLocal(bool on)
        {
            if (_localPtt == on) return;
            _localPtt = on;
            if (IsServer) _ = ApplyPttAsync();
            else SendPttState();
            LocalPttChanged?.Invoke(on);
        }

        public bool LocalPtt => _localPtt;
        public bool Connected => _control != null;

        private void SendPttState() => Send(new MsgWriter(Msg.Ptt).U8((byte)(_localPtt ? 1 : 0)).Build());

        private async Task ApplyPttAsync()
        {
            bool on = _localPtt || _remotePtt;
            Stats.PeerTx = _remotePtt;
            await Rig.SetPttAsync(on);
            StatsChanged?.Invoke();
        }

        public void RequestSwap()
        {
            if (_control == null) { Notice?.Invoke("Per invertire i ruoli i due programmi devono essere collegati."); return; }
            Send(new MsgWriter(Msg.SwapRequest).Build());
        }

        private async Task AnswerSwapAsync(bool accepted)
        {
            var ctl = _control;
            if (ctl == null) return;
            await ctl.SendAsync(new MsgWriter(Msg.SwapAnswer).U8((byte)(accepted ? 1 : 0)).Build());
            if (accepted) await BeginSwapAsync();
        }

        private async Task BeginSwapAsync()
        {
            if (_s.Role != NodeRole.Client) return;
            Audio.Keyer.Stop();
            SetPttLocal(false);
            var oldCtl = _control;
            var oldClientCts = _clientCts;

            _s.Role = NodeRole.Server;
            _s.Save();
            await StartServerAsync(keepChannels: true);
            for (int i = 0; i < 40 && CurrentServerAddress == null; i++) await Task.Delay(250);
            var addr = CurrentServerAddress;
            if (oldCtl != null && addr != null)
            {
                try { await oldCtl.SendAsync(new MsgWriter(Msg.SwapReady).Bytes(addr.Value.ToBytes()).Build()); } catch { }
            }
            await Task.Delay(300);
            if (ReferenceEquals(_clientCts, oldClientCts)) _clientCts = null;
            oldClientCts?.Cancel();
            if (ReferenceEquals(_control, oldCtl)) Interlocked.CompareExchange(ref _control, null, oldCtl);
            oldCtl?.Dispose();
            Rig.Start();
            SessionLog.Write("RUOLI invertiti: questo PC ora è SERVER");
            RoleChanged?.Invoke();
        }

        private async Task FinishSwapAsServerAsync(ServerAddress newServer)
        {
            SetPttLocal(false);
            _remotePtt = false;
            await Rig.SetPttAsync(false);
            Rig.Stop();
            await StopServerAsync();
            _s.Role = NodeRole.Client;
            RememberServer(newServer);
            _s.Save();
            SessionLog.Write("RUOLI invertiti: questo PC ora è CLIENT");
            StartClient();
            RoleChanged?.Invoke();
        }

        // =================================================================== varie

        private void SetMaster(byte[] master)
        {
            _master = master;
            KeyStore.SaveMaster(master);
            _rdv = new Rendezvous(master, _s.MqttBrokers);
        }

        public void ForgetPairing()
        {
            KeyStore.DeleteMaster();
            _master = null;
            _rdv = null;
            CloseChannels();
            SessionLog.Write("ABBINAMENTO cancellato dall'utente");
        }

        private void CloseChannels()
        {
            Interlocked.Exchange(ref _control, null)?.Dispose();
            Interlocked.Exchange(ref _audio, null)?.Dispose();
            Stats.Connected = false;
        }

        private void OnConnected()
        {
            Stats.Connected = true;
            Stats.ConnectedSince = DateTime.Now;
            SetStatus("Collegato", true);
        }

        private void OnDisconnected()
        {
            Stats.Connected = false;
            Stats.LatencyMs = double.NaN;
            if (IsServer)
            {
                _remotePtt = false;
                _ = ApplyPttAsync();
                SetStatus("In attesa del client", false);
            }
            else Audio.Keyer.Stop();
            StatsChanged?.Invoke();
        }

        private void SetStatus(string text, bool connected)
        {
            Stats.Status = text;
            Stats.Connected = connected;
            StatsChanged?.Invoke();
        }

        /// <summary>Ping, rinnovo PTT, controllo di sicurezza, schede audio e indicatori.</summary>
        private async Task HousekeepingAsync(CancellationToken ct)
        {
            int tick = 0;
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(250, ct); } catch { break; }
                tick++;
                try
                {
                    var ctl = _control;
                    if (ctl != null && tick % 8 == 0)
                        _ = ctl.SendAsync(new MsgWriter(Msg.Ping).I64(DateTime.UtcNow.Ticks).Build()).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);

                    if (!IsServer && _localPtt) SendPttState();

                    if (IsServer && _remotePtt && DateTime.UtcNow - _lastRemotePtt > TimeSpan.FromSeconds(1))
                    {
                        _remotePtt = false;
                        await ApplyPttAsync();
                        SessionLog.Write("PTT remoto rilasciato dal watchdog (nessun rinnovo entro 1 s)");
                    }

                    if (IsServer && DateTime.UtcNow - _lastDevicePoll > TimeSpan.FromSeconds(3))
                    {
                        _lastDevicePoll = DateTime.UtcNow;
                        _ = SendDeviceStateAsync();
                    }

                    Stats.BufferMs = Audio.Playback.BufferedMs;
                    Stats.Underruns = Interlocked.Read(ref Audio.Playback.Underruns);
                    Stats.RxPeak = Audio.RxPeak;
                    Stats.TxPeak = Audio.TxPeak;
                    StatsChanged?.Invoke();
                }
                catch { }
            }
        }
    }
}
