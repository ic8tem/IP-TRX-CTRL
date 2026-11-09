using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Audio;
using IpTrxCtrl.Net;
using IpTrxCtrl.Net.Udp;
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
        public string LocalNet = "";        // IPv4 / IPv6 di questo PC
        public string PeerNet = "";         // IPv4 / IPv6 dell'altro PC
        public string AudioPath = "";       // «TCP» o «UDP» (percorso dell'audio)
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
        /// <summary>Server: il router non apre le porte con UPnP; il programma deve chiudersi (motivo).</summary>
        public event Action<string>? PortsFailed;
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

        // ciclo delle porte (client): porte proposte al server per il collegamento inverso
        public const int MaxPortRounds = 20;
        private PortManager? _cports;
        private bool _cportsTried, _cportsUpnp;
        private TcpListener? _cCtlListener, _cAudListener;
        private int _cCtlPort, _cAudPort;
        private TaskCompletionSource<SecureChannel>? _revCtlTcs;

        // audio su UDP
        private UdpLink? _udp;
        private volatile bool _udpMine, _udpPeer, _udpAudio;
        private DateTime _lastUdpAudio = DateTime.MinValue, _pttChanged = DateTime.MinValue;
        private List<IPEndPoint>? _udpPeerCands;
        private int _udpPeerDelta;

        // programmi digitali (client) e tunnel rigctld
        private readonly RigTunnelServer _tunnelSrv;
        private RigTunnelClient? _tunnelCli;
        private DigitalAudio? _digital;
        private volatile bool _digitalPtt;
        public string DigitalStatus { get; private set; } = "spento";
        public bool DigitalPtt => _digitalPtt;

        public Node(AppSettings s)
        {
            _s = s;
            _master = KeyStore.LoadMaster();
            if (_master != null) _rdv = new Rendezvous(_master, _s.MqttBrokers);
            Rig = new RigControl(s);
            Rig.Updated += OnRigUpdated;
            Audio.Captured += OnCaptured;
            Audio.Keyer.PttRequest += on => SetPttLocal(on);
            Audio.RxSamples += (f, n) => _digital?.PushRx(f, n);
            _tunnelSrv = new RigTunnelServer(() => Rig.RigctldEndpoint, Send);
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
            else
            {
                StartClient();
                StartDigital();
            }
        }

        // =================================================================== programmi digitali (client)

        /// <summary>Client: rigctld locale per i programmi digitali e cavi audio virtuali.</summary>
        public void StartDigital()
        {
            StopDigital();
            if (_s.Role != NodeRole.Client || !_s.DigitalEnabled) { DigitalStatus = "spento"; return; }
            if (!VirtualCables.Supported) { DigitalStatus = "non disponibile su Windows 32 bit"; return; }
            try
            {
                _tunnelCli = new RigTunnelClient(_s.RigctlPort, Send);
                _tunnelCli.Log += SessionLog.Write;
                _tunnelCli.PttRequest += SetDigitalPtt;
            }
            catch (Exception ex)
            {
                DigitalStatus = $"porta {_s.RigctlPort} occupata ({ex.Message}): chiudi l'altro rigctld o cambia porta";
                SessionLog.Write("DIGITALE " + DigitalStatus);
                Notice?.Invoke("Programmi digitali: " + DigitalStatus);
                return;
            }
            _digital = new DigitalAudio();
            _digital.Captured += OnDigitalCaptured;
            bool audioOk = _digital.Start(_s.DigitalToProgram, _s.DigitalFromProgram, SessionLog.Write);
            DigitalStatus = $"CAT: Hamlib «NET rigctl» 127.0.0.1:{_s.RigctlPort} · audio: {_digital.Status}";
            if (!audioOk) Notice?.Invoke("Programmi digitali: audio non avviato (" + _digital.Status + ")");
            SessionLog.Write("DIGITALE " + DigitalStatus);
        }

        public void StopDigital()
        {
            if (_digitalPtt) SetDigitalPtt(false);
            _tunnelCli?.Dispose(); _tunnelCli = null;
            _digital?.Dispose(); _digital = null;
        }

        private void SetDigitalPtt(bool on)
        {
            if (_digitalPtt == on) return;
            _digitalPtt = on;
            if (_digital != null) _digital.Transmitting = on;
            SessionLog.Write("DIGITALE PTT " + (on ? "acceso" : "spento") + " dal programma digitale");
            SetPttLocal(on);
        }

        private void OnDigitalCaptured(byte[] pcm, int len)
        {
            if (!_localPtt || !_digitalPtt) return;
            SendAudioFrame(pcm, len);
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
            StopDigital();
            StopUdp();
            _tunnelSrv.Dispose();
            Task.Run(() => StopServerAsync()).Wait(3000);
            Task.Run(CloseClientPortsAsync).Wait(3000);
            Rig.Stop();
            Audio.Dispose();
        }

        // =================================================================== SERVER

        private async Task StartServerAsync(bool keepChannels = false)
        {
            _portsBusy = true;   // subito, prima del primo await: l'abbinamento aspetta le porte
            try { await StartServerCoreAsync(keepChannels); }
            catch (Exception ex) { SetStatus("Errore di rete: " + ex.Message, false); }
            finally { _portsBusy = false; }
        }

        private async Task StartServerCoreAsync(bool keepChannels)
        {
            await StopServerAsync(!keepChannels);
            _serverCts = new CancellationTokenSource();
            var ct = _serverCts.Token;
            _ports = new PortManager { Log = PortLog };
            SetStatus("Apertura porte sul router…", false);

            bool upnp = await _ports.DiscoverAsync(ct);
            bool ok = upnp && await OpenServerPortsAsync(ct);
            _ = Task.Run(() => PublishLoopAsync(ct));
            _ = Task.Run(() => ReverseLoopAsync(ct));

            if (_ports.CgnatSuspected)
                Notice?.Invoke("Il tuo operatore sembra usare il CGNAT: il server potrebbe non essere raggiungibile da internet.");
            UpdateServerEndpoint();
            if (ok) SetStatus(IsPaired ? "In attesa del client" : "In attesa dell'abbinamento", false);
            else
            {
                string reason = !upnp
                    ? "il router non risponde con UPnP (" + _ports.LastError + ")"
                    : "il router non ha aperto porte raggiungibili da internet (" + _ports.LastError + ")";
                SessionLog.Write("PORTE server senza porte aperte: " + reason + " · aspetto le porte proposte dal client sul broker MQTT (collegamento inverso)");
                SetStatus("Nessuna porta aperta sul router: aspetto le porte del client", false);
            }
        }

        /// <summary>
        /// Apre una porta con UPnP, ci mette in ascolto il programma e chiede a un server esterno se è
        /// raggiungibile da internet. Se non lo è la chiude e ne prova un'altra (al massimo 6 volte).
        /// La porta audio viene aperta anche in UDP con lo stesso numero.
        /// </summary>
        private async Task<(int port, TcpListener? listener)> OpenVerifiedPortAsync(PortManager pm, string desc, ChannelKind kind, bool clientSide, CancellationToken ct)
        {
            for (int i = 1; i <= 6 && !ct.IsCancellationRequested; i++)
            {
                int p = await pm.OpenRandomPortAsync(desc, ct);
                if (p == 0) return (0, null);
                TcpListener l;
                try { l = new TcpListener(IPAddress.Any, p); l.Start(); }
                catch { await pm.ClosePortAsync(p); continue; }
                if (clientSide) _ = Task.Run(() => ClientAcceptLoopAsync(l, kind, ct));
                else _ = Task.Run(() => AcceptLoopAsync(l, kind, ct));
                if (kind == ChannelKind.Audio) await pm.OpenUdpAsync(p, desc + " (UDP)");
                bool? reach = await PortProbe.TcpReachableAsync(p, PortLog, ct);
                if (reach != false) return (p, l);
                PortLog($"PORTE {desc}: la porta {p} è aperta sul router ma non è raggiungibile da internet: la chiudo e ne provo un'altra ({i}/6)");
                try { l.Stop(); } catch { }
                await pm.ClosePortAsync(p);
            }
            return (0, null);
        }

        /// <summary>Server: cerca le prime due porte utili (controllo e audio); poi verranno comunicate al broker MQTT.</summary>
        private async Task<bool> OpenServerPortsAsync(CancellationToken ct)
        {
            if (_ports == null) return false;
            var c = await OpenVerifiedPortAsync(_ports, "IP-TRX-CTRL controllo", ChannelKind.Control, false, ct);
            var a = c.port > 0 ? await OpenVerifiedPortAsync(_ports, "IP-TRX-CTRL audio", ChannelKind.Audio, false, ct) : (0, null);
            if (c.port == 0 || a.port == 0)
            {
                try { c.listener?.Stop(); } catch { }
                if (c.port > 0) await _ports.ClosePortAsync(c.port);
                return false;
            }
            _ctlPort = c.port; _ctlListener = c.listener;
            _audPort = a.port; _audListener = a.listener;
            PortLog($"PORTE prime due porte utili del server: controllo TCP {_ctlPort}, audio TCP/UDP {_audPort} · le comunico al broker MQTT");
            return true;
        }

        /// <summary>Server: il client non raggiunge le porte attuali; le chiude e ne apre altre.</summary>
        private async Task RotateServerPortsAsync(CancellationToken ct)
        {
            if (_ports == null || !_ports.UpnpAvailable) return;
            SessionLog.Write("PORTE il client non raggiunge le porte del server: le chiudo e ne apro altre");
            try { _ctlListener?.Stop(); } catch { }
            try { _audListener?.Stop(); } catch { }
            if (_ctlPort > 0) await _ports.ClosePortAsync(_ctlPort);
            if (_audPort > 0) await _ports.ClosePortAsync(_audPort);
            _ctlPort = 0; _audPort = 0; _ctlListener = null; _audListener = null;
            if (await OpenServerPortsAsync(ct))
            {
                UpdateServerEndpoint();
                await PublishNowAsync(ct);
                SetStatus(IsPaired ? "In attesa del client" : "In attesa dell'abbinamento", false);
            }
            else
            {
                UpdateServerEndpoint();
                SetStatus("Nessuna porta aperta sul router: aspetto le porte del client", false);
            }
        }

        /// <summary>
        /// Server: legge sul broker MQTT le porte proposte dal client. Prova a collegarsi a quelle
        /// (collegamento inverso); se il client dice di non aver raggiunto le porte del server, le cambia.
        /// </summary>
        private async Task ReverseLoopAsync(CancellationToken ct)
        {
            long lastTicks = DateTime.UtcNow.AddMinutes(-2).Ticks;   // le proposte vecchie non contano
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(4), ct); } catch { break; }
                if (_control != null || _rdv == null || _master == null || _ports == null) continue;
                PortOffer? o;
                try { o = await _rdv.LookupOfferAsync(ct); } catch { continue; }
                if (o == null || o.Ticks <= lastTicks) continue;
                lastTicks = o.Ticks;
                SessionLog.Write($"PORTE giro {o.Round}/{MaxPortRounds}: il client propone {o.Ip} porte {o.ControlPort}/{o.AudioPort} (non ha raggiunto la porta {o.TriedServerPort} del server)");
                if (o.ControlPort > 0 && IPAddress.TryParse(o.Ip, out var ip)
                    && await ConnectReverseAsync(ip, o.ControlPort, o.AudioPort, ct)) continue;
                if (o.TriedServerPort == 0 || o.TriedServerPort == _ctlPort || _ctlPort == 0)
                    await RotateServerPortsAsync(ct);
            }
        }

        private async Task<bool> ConnectReverseAsync(IPAddress ip, int ctlPort, int audPort, CancellationToken ct)
        {
            var tcp = new TcpClient();
            try
            {
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(6000);
                    await tcp.ConnectAsync(ip, ctlPort, t.Token);
                }
                var res = await SecureChannel.AcceptAsync(tcp, ChannelKind.Control, _master, null, ct);
                if (res.Channel == null) { tcp.Dispose(); return false; }
                SessionLog.Write($"PORTE collegamento inverso riuscito: il server si è collegato alle porte del client {ip}:{ctlPort}");
                var ctlCh = res.Channel;
                _ = Task.Run(() => ServeChannelAsync(ctlCh, ChannelKind.Control, ct));
                for (int i = 0; i < 3 && audPort > 0 && !ct.IsCancellationRequested; i++)
                {
                    var at = new TcpClient();
                    try
                    {
                        using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            t.CancelAfter(6000);
                            await at.ConnectAsync(ip, audPort, t.Token);
                        }
                        var ar = await SecureChannel.AcceptAsync(at, ChannelKind.Audio, _master, null, ct);
                        if (ar.Channel != null)
                        {
                            var audCh = ar.Channel;
                            _ = Task.Run(() => ServeChannelAsync(audCh, ChannelKind.Audio, ct));
                            break;
                        }
                        at.Dispose();
                    }
                    catch (Exception ex) { at.Dispose(); SessionLog.Write($"PORTE audio inverso {ip}:{audPort} non riuscito ({ex.Message})"); }
                    try { await Task.Delay(1000, ct); } catch { break; }
                }
                return true;
            }
            catch (Exception ex)
            {
                tcp.Dispose();
                SessionLog.Write($"PORTE il server non raggiunge le porte del client {ip}:{ctlPort} ({ex.Message})");
                return false;
            }
        }

        private volatile bool _portsBusy, _pairingWatch;

        /// <summary>Riga del registro delle porte; durante l'abbinamento compare anche nella finestra.</summary>
        private void PortLog(string line)
        {
            SessionLog.Write(line);
            if (_pairingWatch) PairingLog?.Invoke('·', line.StartsWith("PORTE ") ? line[6..] : line);
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
            Stats.ServerEndpoint = "";
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
            _pairingWatch = true;
            Log('·', "preparo le porte e l'indirizzo pubblico…");
            // «Riprova» dopo un fallimento: si riaprono le porte da capo
            if (CurrentServerAddress == null && !_portsBusy && _control == null)
            {
                Log('·', "riapro le porte sul router con UPnP…");
                _ = StartServerAsync();
            }
            // l'apertura con verifica esterna può durare anche un paio di minuti
            var start = DateTime.UtcNow;
            while (CurrentServerAddress == null && !ct.IsCancellationRequested
                   && (_portsBusy || DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
                   && DateTime.UtcNow - start < TimeSpan.FromMinutes(4))
            {
                try { await Task.Delay(500, ct); } catch { break; }
            }
            var addr = CurrentServerAddress;
            if (addr == null)
            {
                _pairingWatch = false;
                if (ct.IsCancellationRequested) return null;
                string err = _ports?.LastError ?? "";
                if (_ports == null || !_ports.UpnpAvailable)
                    Log('✗', "il router del server non risponde con UPnP: attivalo sul router e premi Riprova" + (err.Length > 0 ? $" ({err})" : ""));
                else if (_ports.ExternalIp == null)
                    Log('✗', "indirizzo pubblico non disponibile: controlla la connessione a internet e premi Riprova");
                else
                    Log('✗', $"il router ({_ports.ExternalIp}) non ha aperto porte raggiungibili da internet: controlla UPnP o il CGNAT dell'operatore, poi premi Riprova" + (err.Length > 0 ? $" ({err})" : ""));
                return null;
            }
            _pairingWatch = false;
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
                        await ServeChannelAsync(res.Channel, kind, ct);
                    }
                    catch { }
                }, ct);
            }
        }

        /// <summary>Server: canale autenticato (in arrivo o collegamento inverso) fino alla sua chiusura.</summary>
        private async Task ServeChannelAsync(SecureChannel ch, ChannelKind kind, CancellationToken ct)
        {
            if (kind == ChannelKind.Control)
            {
                var old = Interlocked.Exchange(ref _control, ch);
                old?.Dispose();
                OnConnected();
                await SendHelloAsync(ch);
                try { await ch.SendAsync(new MsgWriter(Msg.PeerInfo).Str(ch.Remote.Address.MapToIPv4().ToString()).Build(), ct); } catch { }
                Stats.ClientEndpoint = ch.Remote.Address.MapToIPv4().ToString();
                _ = SendDeviceStateAsync();
                _ = Task.Run(() => OfferUdpAsync(ct));
                await ReceiveLoopAsync(ch, ct);
            }
            else
            {
                var old = Interlocked.Exchange(ref _audio, ch);
                old?.Dispose();
                ResetAudioStats();
                await ReceiveLoopAsync(ch, ct);
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
            SessionLog.Write($"PORTE il client non raggiunge la porta audio {_audPort}: ne apro un'altra");
            int p = _ports.UpnpAvailable ? await _ports.OpenRandomPortAsync("IP-TRX-CTRL audio", ct) : 0;
            if (p == 0)
            {
                string reason = "il router non ha aperto la nuova porta audio (" + _ports.LastError + ")";
                SessionLog.Write("PORTE " + reason + " · il programma si spegne");
                PortsFailed?.Invoke(reason);
                return;
            }
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
            int failedRounds = 0, round = 0;
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

                bool reverse = false;
                if (ctl == null)
                {
                    failedRounds++;
                    if (failedRounds >= 2) NeedFallbackCode?.Invoke(true);
                    if (_rdv == null)
                    {
                        SetStatus("Server non raggiungibile, riprovo…", false);
                        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 5 * failedRounds)), ct); } catch { break; }
                        continue;
                    }
                    // ciclo delle porte: il client propone le sue porte al server sul broker MQTT
                    round++;
                    string where = target == null ? "nessun indirizzo del server sul broker" : $"{target.Value.Ip}:{target.Value.ControlPort}";
                    SessionLog.Write($"PORTE giro {round}/{MaxPortRounds}: il client non raggiunge il server ({where})");
                    SetStatus($"Server non raggiungibile · giro {round} di {MaxPortRounds}: propongo le mie porte", false);
                    ctl = await ClientOfferRoundAsync(round, target?.ControlPort ?? 0, ct);
                    if (ctl == null)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (round >= MaxPortRounds)
                        {
                            SessionLog.Write($"PORTE dopo {MaxPortRounds} giri nessuna porta è apribile: il client si chiude");
                            await CloseClientPortsAsync();
                            SetStatus("Nessuna porta apribile", false);
                            PortsFailed?.Invoke($"dopo {MaxPortRounds} giri né il server né il client hanno aperto una porta raggiungibile");
                            return;
                        }
                        continue;
                    }
                    reverse = true;
                }

                failedRounds = 0;
                round = 0;
                NeedFallbackCode?.Invoke(false);
                _control = ctl;

                if (reverse)
                {
                    Stats.ServerEndpoint = $"{ctl.Remote.Address.MapToIPv4()} · collegamento inverso sulle porte del client {_cCtlPort}/{_cAudPort}";
                    UpdateClientEndpoint();
                    OnConnected();
                    await SendHelloAsync(ctl);
                    var recvR = Task.Run(() => ReceiveLoopAsync(ctl, ct));
                    for (int i = 0; i < 50 && _audio == null && !ct.IsCancellationRequested; i++)
                    {
                        try { await Task.Delay(200, ct); } catch { break; }
                    }
                    SessionLog.Write(_audio != null
                        ? "TEST porte: collegamento inverso, controllo e audio attivi"
                        : "TEST porte: collegamento inverso, il canale audio non è arrivato");
                    try { await recvR; } catch { }
                    await CloseClientPortsAsync();
                }
                else
                {
                    var addr = target!.Value;
                    RememberServer(addr);
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
                }

                if (ct.IsCancellationRequested) break;
                CloseChannels();
                SetStatus("Collegamento interrotto, riconnessione…", false);
                try { await Task.Delay(2000, ct); } catch { break; }
            }
        }

        /// <summary>
        /// Un giro del ciclo delle porte lato client: apre con UPnP due porte verificate da un server
        /// esterno, le lascia sul broker MQTT e aspetta che il server si colleghi a lui (collegamento
        /// inverso). Se il client non ha porte, lo dice comunque al server, che proverà porte nuove.
        /// </summary>
        private async Task<SecureChannel?> ClientOfferRoundAsync(int round, int triedServerPort, CancellationToken ct)
        {
            try
            {
                _cports ??= new PortManager { Log = SessionLog.Write };
                if (!_cportsTried) { _cportsUpnp = await _cports.DiscoverAsync(ct); _cportsTried = true; }
                await CloseClientPortsAsync();
                _revCtlTcs = new TaskCompletionSource<SecureChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_cportsUpnp)
                {
                    var c = await OpenVerifiedPortAsync(_cports, "IP-TRX-CTRL client controllo", ChannelKind.Control, true, ct);
                    var a = c.port > 0 ? await OpenVerifiedPortAsync(_cports, "IP-TRX-CTRL client audio", ChannelKind.Audio, true, ct) : (0, null);
                    _cCtlPort = c.port; _cCtlListener = c.listener;
                    _cAudPort = a.port; _cAudListener = a.listener;
                    if (_cCtlPort == 0 || _cAudPort == 0) await CloseClientPortsAsync();
                }
                else SessionLog.Write("PORTE il router del client non risponde con UPnP: il client non può proporre porte");

                var ip = _cports.ExternalIp ?? await PortManager.GetPublicIpAsync(ct);
                var offer = new PortOffer(round, ip?.ToString() ?? "", _cCtlPort, _cAudPort, triedServerPort, DateTime.UtcNow.Ticks);
                bool sent = await _rdv!.PublishOfferAsync(offer, ct);
                SessionLog.Write($"PORTE giro {round}/{MaxPortRounds}: il client propone {offer.Ip} porte {_cCtlPort}/{_cAudPort} sul broker MQTT {(sent ? "(consegnato)" : "(nessun broker raggiungibile)")}");

                // il server legge le proposte ogni pochi secondi; se cambia le sue porte le troveremo al prossimo giro
                var wait = Task.Delay(TimeSpan.FromSeconds(_cCtlPort > 0 ? 25 : 15), ct);
                var done = await Task.WhenAny(_revCtlTcs.Task, wait);
                if (done == _revCtlTcs.Task) return _revCtlTcs.Task.Result;
                await CloseClientPortsAsync();
                return null;
            }
            catch (Exception ex)
            {
                SessionLog.Write($"PORTE giro {round}: errore ({ex.Message})");
                try { await Task.Delay(5000, ct); } catch { }
                return null;
            }
        }

        /// <summary>Client: il server si collega alle porte del client; l'handshake lo avvia comunque il client.</summary>
        private async Task ClientAcceptLoopAsync(TcpListener listener, ChannelKind kind, CancellationToken ct)
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
                        var ch = await SecureChannel.HandshakeAsInitiatorAsync(tcp, kind, _master!, ct);
                        if (kind == ChannelKind.Control)
                        {
                            if (!(_revCtlTcs?.TrySetResult(ch) ?? false)) ch.Dispose();
                        }
                        else
                        {
                            var old = Interlocked.Exchange(ref _audio, ch);
                            old?.Dispose();
                            ResetAudioStats();
                            await ReceiveLoopAsync(ch, ct);
                        }
                    }
                    catch { }
                }, ct);
            }
        }

        private async Task CloseClientPortsAsync()
        {
            try { _cCtlListener?.Stop(); } catch { }
            try { _cAudListener?.Stop(); } catch { }
            _cCtlListener = null; _cAudListener = null;
            if (_cports != null)
            {
                if (_cCtlPort > 0) await _cports.ClosePortAsync(_cCtlPort);
                if (_cAudPort > 0) await _cports.ClosePortAsync(_cAudPort);
            }
            _cCtlPort = 0; _cAudPort = 0;
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
            try { await ch.SendAsync(new MsgWriter(Msg.Hello).Str(_s.Callsign).Str(Stats.LocalNet).Build()); } catch { }
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
                            _s.PeerCallsign = r.Str();
                            Stats.PeerNet = r.AtEnd ? "" : r.Str();
                            _s.Save(); StatsChanged?.Invoke(); break;
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
                        case Msg.SetPassband: if (IsServer) { string pm = r.Str(); int hz = r.I32(); _ = Rig.SetPassbandAsync(pm, hz); } break;
                        case Msg.SetSplit: if (IsServer) _ = Rig.SetSplitAsync(r.Bool()); break;
                        case Msg.VfoOp: if (IsServer) _ = Rig.VfoOpAsync(r.Str()); break;
                        case Msg.SetPower: if (IsServer) { float v = r.F32(); bool w = r.Bool(); _ = Rig.SetPowerAsync(v, w); } break;
                        case Msg.SetPitch: if (IsServer) _ = Rig.SetPitchAsync(r.I32()); break;
                        case Msg.SendMorse:
                            if (IsServer)
                            {
                                string text = r.Str();
                                int wpm = r.AtEnd ? _s.CwSpeedWpm : r.I32();
                                SessionLog.Write($"CW dal client ({wpm} WPM): {text}");
                                _ = Rig.SendCwAsync(text, wpm);
                            }
                            break;
                        case Msg.StopMorse: if (IsServer) _ = Rig.StopCwAsync(); break;
                        case Msg.CwKey: if (IsServer) { bool down = r.Bool(); Rig.CwKeyEvent(down, r.I64()); } break;
                        case Msg.SetKeySpeed: if (IsServer) _ = Rig.SetKeySpeedAsync(r.I32()); break;
                        case Msg.SetDeviceMute:
                            if (IsServer) { bool input = r.U8() == 0; bool mute = r.Bool(); DeviceMute.SetMute(input ? _s.InputDevice : _s.OutputDevice, input, mute); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.SetDeviceVolume:
                            if (IsServer) { bool input = r.U8() == 0; float v = r.F32(); DeviceMute.SetVolume(input ? _s.InputDevice : _s.OutputDevice, input, v); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.SetServerDevices:
                            if (IsServer) { string din = r.Str(); string dout = r.Str(); _ = ChangeServerDevicesAsync(din, dout, true); }
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
                        case Msg.RigTunnelOpen:
                            if (IsServer) _ = _tunnelSrv.OpenAsync(r.I32());
                            break;
                        case Msg.RigTunnelData:
                            {
                                int id = r.I32();
                                byte[] d = r.Rest();
                                if (IsServer) await _tunnelSrv.DataAsync(id, d);
                                else _tunnelCli?.OnData(id, d);
                            }
                            break;
                        case Msg.RigTunnelClose:
                            {
                                int id = r.I32();
                                if (IsServer) _tunnelSrv.Close(id); else _tunnelCli?.OnClose(id);
                            }
                            break;
                        case Msg.UdpOffer:
                            {
                                string cands = r.Str();
                                int delta = r.I32();
                                _ = Task.Run(() => OnUdpOfferAsync(cands, delta, ct));
                            }
                            break;
                        case Msg.UdpAudioOn:
                            _udpPeer = r.U8() == 1;
                            UpdateUdpAudio(_udpPeer ? "l'altro PC riceve su UDP" : "l'altro PC chiede di tornare a TCP");
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
            OnAudio(seq, r.Rest());
        }

        private void OnAudio(uint seq, byte[] pcm)
        {
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
            // Server: invia sempre l'audio ricevuto dalla radio (non durante la trasmissione). Client: solo con PTT.
            bool send = IsServer ? !Stats.PeerTx : _localPtt && !_digitalPtt;
            if (!send) return;
            SendAudioFrame(pcm, len);
        }

        /// <summary>Audio verso l'altro PC: su UDP se il percorso è stato verificato dai due lati, altrimenti su TCP.</summary>
        private void SendAudioFrame(byte[] pcm, int len)
        {
            var udp = _udp;
            if (_udpAudio && udp != null) { udp.SendAudio(pcm.AsSpan(0, len)); return; }
            var aud = _audio;
            if (aud == null) return;
            byte[] msg = new MsgWriter(Msg.Audio).U32(++_txSeq).Bytes(pcm, 0, len).Build();
            _ = aud.SendAsync(msg).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // =================================================================== audio su UDP

        /// <summary>
        /// Prepara il collegamento UDP: socket sulla porta audio (aperta anche in UDP con UPnP), indirizzo
        /// pubblico chiesto ai server STUN, candidati mandati all'altro PC sul canale di controllo cifrato.
        /// </summary>
        private async Task<UdpLink?> EnsureUdpAsync(CancellationToken ct)
        {
            if (_udp != null || _master == null) return _udp;
            int localPort = IsServer ? _audPort : _cAudPort;
            UdpLink link;
            try { link = new UdpLink(_master, localPort); }
            catch { link = new UdpLink(_master, 0); localPort = 0; }
            link.Log += SessionLog.Write;
            link.AudioReceived += (seq, pcm) => { _lastUdpAudio = DateTime.UtcNow; OnAudio(seq, pcm); };
            _udp = link;

            var cands = new List<string>();
            var nat = await Stun.DiscoverAsync(link, Stun.DefaultServers, SessionLog.Write, ct);
            if (nat.Public != null) cands.Add(nat.Public.ToString());
            var pm = IsServer ? _ports : _cports;
            if (localPort > 0 && pm?.ExternalIp != null) cands.Add(new IPEndPoint(pm.ExternalIp, localPort).ToString());
            string lan = LanIp();
            if (lan != "—") cands.Add($"{lan}:{link.LocalPort}");
            cands = cands.Distinct().ToList();
            SessionLog.Write($"UDP NAT {nat.Type} · candidati {string.Join(" ", cands)}");
            var ctl = _control;
            if (ctl != null)
            {
                try { await ctl.SendAsync(new MsgWriter(Msg.UdpOffer).Str(string.Join(";", cands)).I32(nat.PortDelta).Build(), ct); } catch { }
            }
            return link;
        }

        /// <summary>Server: appena il controllo è attivo propone l'audio su UDP.</summary>
        private async Task OfferUdpAsync(CancellationToken ct)
        {
            try
            {
                StopUdp();
                await EnsureUdpAsync(ct);
                if (_udpPeerCands != null) await PunchAsync(ct);
            }
            catch (Exception ex) { SessionLog.Write("UDP errore: " + ex.Message); }
        }

        private async Task OnUdpOfferAsync(string cands, int delta, CancellationToken ct)
        {
            try
            {
                var list = new List<IPEndPoint>();
                foreach (var c in cands.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    if (IPEndPoint.TryParse(c, out var ep)) list.Add(ep);
                _udpPeerCands = list;
                _udpPeerDelta = delta;
                SessionLog.Write($"UDP candidati dell'altro PC: {cands}");
                if (_udp == null) await EnsureUdpAsync(ct);
                await PunchAsync(ct);
            }
            catch (Exception ex) { SessionLog.Write("UDP errore: " + ex.Message); }
        }

        private async Task PunchAsync(CancellationToken ct)
        {
            var link = _udp;
            var cands = _udpPeerCands;
            if (link == null || cands == null || cands.Count == 0) return;
            bool ok = await link.PunchAsync(cands, _udpPeerDelta, TimeSpan.FromSeconds(10), ct);
            _udpMine = ok;
            var ctl = _control;
            if (ctl != null) { try { await ctl.SendAsync(new MsgWriter(Msg.UdpAudioOn).U8((byte)(ok ? 1 : 0)).Build(), ct); } catch { } }
            UpdateUdpAudio(ok ? $"perforazione riuscita verso {link.Remote}" : "perforazione non riuscita: l'audio resta su TCP");
        }

        private void UpdateUdpAudio(string why)
        {
            bool on = _udpMine && _udpPeer && _udp != null;
            if (on != _udpAudio)
            {
                _udpAudio = on;
                _lastUdpAudio = DateTime.UtcNow;
                SessionLog.Write($"UDP audio {(on ? "su UDP" : "su TCP")} ({why})");
            }
            Stats.AudioPath = on ? "UDP" : "TCP";
            StatsChanged?.Invoke();
        }

        private void StopUdp()
        {
            _udpAudio = false; _udpMine = false; _udpPeer = false;
            _udpPeerCands = null;
            Interlocked.Exchange(ref _udp, null)?.Dispose();
            Stats.AudioPath = "";
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

        /// <summary>Server: le schede audio sono cambiate (dal client); la finestra aggiorna le sue liste.</summary>
        public event Action? ServerDevicesChanged;

        private (List<string> ins, List<string> outs) _devLists = (new(), new());
        private DateTime _devListsAt = DateTime.MinValue;

        /// <summary>Elenco delle schede del server, riletto al massimo ogni 30 s.</summary>
        private (List<string> ins, List<string> outs) DeviceLists()
        {
            if (DateTime.UtcNow - _devListsAt > TimeSpan.FromSeconds(30))
            {
                _devLists = (SafeList(Audio.Inputs), SafeList(Audio.Outputs));
                _devListsAt = DateTime.UtcNow;
            }
            return _devLists;
        }

        private static List<string> SafeList(Func<List<string>> f)
        {
            try { return f(); } catch { return new List<string>(); }
        }

        /// <summary>Client: sceglie le schede audio del server. Server: le cambia direttamente.</summary>
        public void SetServerDevices(string input, string output)
        {
            if (IsServer) _ = ChangeServerDevicesAsync(input, output, false);
            else Send(new MsgWriter(Msg.SetServerDevices).Str(input).Str(output).Build());
        }

        /// <summary>
        /// Server: riavvia l'audio sulle schede scelte. Se la nuova scheda non si apre torna a quelle di
        /// prima, così il collegamento non resta senza audio.
        /// </summary>
        private async Task ChangeServerDevicesAsync(string input, string output, bool fromClient)
        {
            _devListsAt = DateTime.MinValue;
            var (ins, outs) = DeviceLists();
            if (!ins.Contains(input)) input = _s.InputDevice;
            if (!outs.Contains(output)) output = _s.OutputDevice;
            if (input == _s.InputDevice && output == _s.OutputDevice) { await SendDeviceStateAsync(); return; }

            string oldIn = _s.InputDevice, oldOut = _s.OutputDevice;
            string who = fromClient ? "dal client" : "dal server";
            _s.InputDevice = input; _s.OutputDevice = output;
            RestartAudio();
            await Task.Delay(800);
            string err = Audio.LastError;
            if (err.Length > 0)
            {
                SessionLog.Write($"AUDIO schede del server scelte {who} non apribili ({err}): torno a «{oldIn}» / «{oldOut}»");
                _s.InputDevice = oldIn; _s.OutputDevice = oldOut;
                RestartAudio();
                string msg = $"Schede audio del server non apribili ({err}): restano quelle di prima.";
                Notice?.Invoke(msg);
                if (fromClient) Send(new MsgWriter(Msg.RadioStatus).Str(msg).Build());
            }
            else
            {
                SessionLog.Write($"AUDIO schede del server cambiate {who}: ingresso «{input}», uscita «{output}»");
                _s.Save();
                if (fromClient) Notice?.Invoke($"Il client ha cambiato le schede audio: ingresso «{input}», uscita «{output}».");
            }
            ServerDevicesChanged?.Invoke();
            await SendDeviceStateAsync();
        }

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
                    Note = DeviceMute.PrivacyNote(),
                    Inputs = DeviceLists().ins, Outputs = DeviceLists().outs
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

        public void SetPassband(string mode, int hz) { if (IsServer) _ = Rig.SetPassbandAsync(mode, hz); else Send(new MsgWriter(Msg.SetPassband).Str(mode).I32(hz).Build()); }
        public void SetVfoB(long hz) { if (IsServer) _ = Rig.SetVfoBAsync(hz); else Send(new MsgWriter(Msg.SetVfoB).I64(hz).Build()); }
        public void SetSplit(bool on) { if (IsServer) _ = Rig.SetSplitAsync(on); else Send(new MsgWriter(Msg.SetSplit).Bool(on).Build()); }
        public void VfoOp(string op) { if (IsServer) _ = Rig.VfoOpAsync(op); else Send(new MsgWriter(Msg.VfoOp).Str(op).Build()); }
        public void SetPower(float value, bool watts) { if (IsServer) _ = Rig.SetPowerAsync(value, watts); else Send(new MsgWriter(Msg.SetPower).F32(value).Bool(watts).Build()); }
        public void SetPitch(int hz) { if (IsServer) _ = Rig.SetPitchAsync(hz); else Send(new MsgWriter(Msg.SetPitch).I32(hz).Build()); }
        public void SetKeySpeed(int wpm) { if (IsServer) _ = Rig.SetKeySpeedAsync(wpm); else Send(new MsgWriter(Msg.SetKeySpeed).I32(wpm).Build()); }
        /// <summary>Testo della macro CW: il client lo inoltra al server come stringa, con la velocità.</summary>
        public void SendMorse(string text) { if (IsServer) _ = Rig.SendCwAsync(text, _s.CwSpeedWpm); else Send(new MsgWriter(Msg.SendMorse).Str(text).I32(_s.CwSpeedWpm).Build()); }
        /// <summary>Tasto CW manuale del client: ogni pressione e rilascio vanno al server con l'istante.</summary>
        public void SendCwKey(bool down, long ms)
        {
            if (IsServer) Rig.CwKeyEvent(down, ms);
            else Send(new MsgWriter(Msg.CwKey).Bool(down).I64(ms).Build());
        }

        public void StopMorse() { if (IsServer) _ = Rig.StopCwAsync(); else Send(new MsgWriter(Msg.StopMorse).Build()); }

        public void SetRemoteDeviceMute(bool input, bool mute) => Send(new MsgWriter(Msg.SetDeviceMute).U8((byte)(input ? 0 : 1)).Bool(mute).Build());
        public void SetRemoteDeviceVolume(bool input, float v) => Send(new MsgWriter(Msg.SetDeviceVolume).U8((byte)(input ? 0 : 1)).F32(v).Build());
        public void SetRemoteAutoUnmute(bool on) => Send(new MsgWriter(Msg.SetAutoUnmute).Bool(on).Build());

        public void SetPttLocal(bool on)
        {
            if (_localPtt == on) return;
            _localPtt = on;
            _pttChanged = DateTime.UtcNow;
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
            StopUdp();
            _tunnelCli?.DropAll();
            _tunnelSrv.Dispose();
            if (_digitalPtt) SetDigitalPtt(false);
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
            DateTime lastNet = DateTime.MinValue;
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

                    // UDP: se in ricezione non arriva più audio su UDP si torna a TCP
                    // (lo controlla il client: il server gli manda la ricezione di continuo quando non trasmette)
                    var now4 = DateTime.UtcNow - TimeSpan.FromSeconds(4);
                    if (!IsServer && _udpAudio && !_localPtt && _pttChanged < now4 && _lastUdpAudio < now4)
                    {
                        _udpMine = false;
                        Send(new MsgWriter(Msg.UdpAudioOn).U8(0).Build());
                        UpdateUdpAudio("nessun pacchetto UDP da 4 s");
                    }

                    if (IsServer && _remotePtt && DateTime.UtcNow - _lastRemotePtt > TimeSpan.FromSeconds(1))
                    {
                        _remotePtt = false;
                        await ApplyPttAsync();
                        SessionLog.Write("PTT remoto rilasciato dal watchdog (nessun rinnovo entro 1 s)");
                    }

                    if (DateTime.UtcNow - lastNet > TimeSpan.FromMinutes(5))
                    {
                        lastNet = DateTime.UtcNow;
                        _ = Task.Run(async () =>
                        {
                            string net = await NetInfo.DescribeAsync(ct);
                            if (net != Stats.LocalNet)
                            {
                                Stats.LocalNet = net;
                                SessionLog.Write("RETE questo PC: " + net);
                                var c = _control;
                                if (c != null) await SendHelloAsync(c);
                            }
                        });
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
