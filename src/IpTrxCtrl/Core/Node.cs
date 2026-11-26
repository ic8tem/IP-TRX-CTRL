using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        public string PeerVersion = "";     // versione del programma sull'altro PC
        public string Listeners = "";       // server: chi è collegato in solo ascolto (dalla 0.3.6)
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
        public bool IsPaired => IsServer ? ClientCount > 0 || _s.WebEnabled : _master != null;   // col browser attivo non serve un abbinamento
        public string Fingerprint => _master != null ? Crypto.Fingerprint(_master)
            : IsServer && ClientCount > 0 ? $"{ClientCount} client abbinati" : "";

        // server: più client abbinati (una chiave ciascuno, uno collegato per volta) e codice fisso
        private readonly Dictionary<string, byte[]> _clientKeys = new();
        private readonly Dictionary<string, Rendezvous> _clientRdvs = new();
        private string _sessionClientId = "";
        private string _fixedDigits = "";
        private volatile byte[]? _fixedSecret;
        private CancellationTokenSource? _fixedCts;
        private DateTime _busyUntil = DateTime.MinValue;   // client: server occupato da un altro operatore
        public event Action? ClientsChanged;
        private int ClientCount { get { lock (_clientKeys) return _clientKeys.Count; } }

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
        /// <summary>Versione dell'altro PC da segnalare (testo, compatibile sì/no).</summary>
        public event Action<string, bool>? VersionWarning;
        /// <summary>Stato di UPnP sul router di questo PC (server e client), controllato ogni 60 s.</summary>
        public UpnpCheck Upnp { get; } = new();
        /// <summary>Server: porte aperte ma non raggiungibili da internet (testo con porte e rimedi).</summary>
        public event Action<string>? PortsUnreachable;
        private bool _keptUnreachable;
        /// <summary>Registro del dialogo di abbinamento (simbolo, testo).</summary>
        public event MqttLog? PairingLog;

        private IMsgChannel? _control, _audio;

        // server
        private PortManager? _ports;
        private TcpListener? _ctlListener, _audListener;
        private int _ctlPort, _audPort;   // porte ESTERNE pubblicate (sul router)
        private int _audLocal;            // porta interna dell'audio sul PC (uguale a _audPort se non è inoltrata)
        private List<int>? _preferredPorts; // porte in uscita che il client riesce a usare (dalla sua proposta)
        private OutboundProbe.Result? _outbound; // client: porte che escono dalla sua rete
        private CancellationTokenSource? _serverCts;
        private byte[]? _pairingSecret;
        // abbinamento: ciclo delle porte lato server (porte sempre nuove, un giro per proposta del client)
        private readonly SemaphoreSlim _pairRoundLock = new(1, 1);
        private readonly object _pairDoneLock = new();
        private readonly HashSet<int> _pairTriedSrv = new();
        private long _pairLastTicks;
        private int _pairRoundPort;
        private TcpListener? _pairRoundListener;
        /// <summary>Server: il numero di abbinamento è scaduto.</summary>
        public event Action? PairingExpired;
        /// <summary>Client: l'elenco dei server o quello in uso è cambiato.</summary>
        public event Action? ServersChanged;
        /// <summary>Client: dopo un nuovo collegamento la finestra richiede il panadapter se lo mostra.</summary>
        public event Action? SpectrumResend;
        /// <summary>Client: giro del ciclo delle porte durante l'abbinamento (giro, totale).</summary>
        public event Action<int, int>? PairingRound;
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
            LoadKeyForRole();
            Rig = new RigControl(s);
            Rig.Updated += OnRigUpdated;
            Rig.Notice += OnRigNotice;
            CodeGuard.LockedNow += m => Notice?.Invoke(m);   // 0.3.8: troppi codici sbagliati da più indirizzi
            Pan = new Panadapter(s);
            Pan.Line += OnPanLine;
            Audio.Captured += OnCaptured;
            Audio.Keyer.PttRequest += on => SetPttLocal(on);
            Audio.RxSamples += (f, n) => _digital?.PushRx(f, n);
            _tunnelSrv = new RigTunnelServer(() => Rig.RigctldEndpoint, Send);
            Upnp.Log = SessionLog.Write;
            _ = Task.Run(() => Upnp.RunAsync(_life.Token));
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
                Pan.Start();
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
            Audio.OfferTci = IsServer && _s.CatViaTci;   // «TCI» tra le schede del server
            Audio.OfferIcomLan = IsServer && _s.IcomLan;   // «Icom LAN» tra le schede del server
            _devListsAt = DateTime.MinValue;
            Audio.RxGain = AudioEngine.GainFrom(_s.RxVolume, _s.RxBoost);
            Audio.TxGain = AudioEngine.GainFrom(_s.TxVolume, _s.TxBoost);
            Audio.Playback.PrefillSamples = Math.Clamp(_s.PrefillMs, 20, 1000) * AudioEngine.SampleRate / 1000;
            _opusTx.SetBitrate(_s.OpusBitrate);
            Squelch.Enabled = !IsServer && _s.VoiceSquelch;
            Denoiser.Enabled = !IsServer && _s.NeuralNr;
            Denoiser.Strength = Math.Clamp(_s.NeuralNrPercent, 10, 100) / 100f;
            Squelch.HoldMs = Math.Clamp(_s.VoiceSquelchHoldMs, 200, 5000);
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
            Pan.Dispose();
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
            LoadServerKeys();   // tutti i client abbinati (anche dopo lo scambio dei ruoli)
            _serverCts = new CancellationTokenSource();
            var ct = _serverCts.Token;
            _ports = new PortManager { Log = PortLog };
            SetStatus("Apertura porte sul router…", false);

            bool upnp = await _ports.DiscoverAsync(ct);
            bool ok = await OpenServerPortsAsync(ct);
            _ = Task.Run(() => PublishLoopAsync(ct));
            _ = Task.Run(() => ReverseLoopAsync(ct));
            RestartFixedListener();
            RestartWeb();

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
            bool upnp = pm.UpnpAvailable;
            // 0.3.4: doppio NAT o CGNAT, il router di casa apre la porta ma quello davanti la blocca. Provare altre
            // porte non serve (e fa rifiutare le verifiche esterne per troppe richieste): una sola.
            bool doubleNat = upnp && pm.CgnatSuspected;
            int tries = upnp && !doubleNat ? 6 : 1;
            if (doubleNat) PortLog($"PORTE {desc}: {pm.NatNote()}: provo una sola porta (le altre sarebbero bloccate allo stesso modo)");
            for (int i = 1; i <= tries && !ct.IsCancellationRequested; i++)
            {
                int p = upnp ? await pm.OpenRandomPortAsync(desc, ct) : pm.PickLocalPort();
                if (p == 0 && upnp)
                {
                    // il router non accetta nessuna porta: si va avanti con una porta del PC (rete locale,
                    // DMZ, inoltro manuale) e, nell'abbinamento, con il ciclo delle porte e il collegamento inverso
                    p = pm.PickLocalPort();
                    upnp = false;
                    tries = i;
                    if (p > 0) PortLog($"PORTE {desc}: il router non apre porte, uso la porta del PC {p} (vale in rete locale, con DMZ o inoltro manuale; da fuori serve il collegamento inverso)");
                }
                else if (!upnp) PortLog($"PORTE {desc}: router senza UPnP, provo la porta locale {p} (funziona con DMZ, inoltro manuale sul router o sulla stessa rete locale)");
                if (p == 0) return (0, null);
                TcpListener l;
                try { l = new TcpListener(IPAddress.Any, p); l.Start(); }
                catch { await pm.ClosePortAsync(p); continue; }
                if (clientSide) _ = Task.Run(() => ClientAcceptLoopAsync(l, kind, ct));
                else _ = Task.Run(() => AcceptLoopAsync(l, kind, ct));
                if (kind == ChannelKind.Audio) await pm.OpenUdpAsync(p, desc + " (UDP)");
                bool? reach = await ProbeOwnAsync(p, PortLog, ct);
                if (reach != false) return (p, l);
                // il server tiene comunque l'ultima porta: serve ai PC della stessa rete locale
                if (!clientSide && i == tries)
                {
                    _keptUnreachable = true;
                    PortLog($"PORTE {desc}: la porta {p} non è raggiungibile da internet; la tengo per la rete locale");
                    return (p, l);
                }
                PortLog($"PORTE {desc}: la porta {p} è aperta sul router ma non è raggiungibile da internet: la chiudo e ne provo un'altra ({i}/6)");
                try { l.Stop(); } catch { }
                await pm.ClosePortAsync(p);
            }
            return (0, null);
        }

        private int _accepted;   // collegamenti TCP arrivati a questo programma (per riconoscere chi risponde alle verifiche)

        /// <summary>
        /// Verifica esterna della porta, controllando che a rispondere sia davvero questo programma (0.3.4, come
        /// IP-DESK-CTRL 0.5.17): se i servizi di verifica dicono «sì» ma qui non è arrivato nessun collegamento,
        /// ha risposto un altro apparecchio (spesso la pagina del router sulla porta 80 o 443) e la porta in
        /// realtà non porta a questo PC.
        /// </summary>
        private async Task<bool?> ProbeOwnAsync(int port, Action<string> log, CancellationToken ct)
        {
            int before = Volatile.Read(ref _accepted);
            bool? r = await PortProbe.TcpReachableAsync(port, log, ct);
            if (r != true) return r;
            for (int i = 0; i < 15 && Volatile.Read(ref _accepted) == before; i++)
            {
                try { await Task.Delay(100, ct); } catch { return r; }
            }
            if (Volatile.Read(ref _accepted) != before) return true;
            log($"PORTE la porta {port} risponde da internet, ma non è questo programma (forse la pagina del router): la considero chiusa");
            return false;
        }

        // ---------------- porte scelte dall'utente (0.3.4)

        /// <summary>Porte aperte a mano sul router (Impostazioni › PORTE APERTE SUL ROUTER): 0 = automatiche.</summary>
        public int ManualControlPort => _s.ManualControlPort;
        public int ManualAudioPort => _s.ManualAudioPort;
        public bool ManualPortsSet => _s.ManualControlPort > 0 && _s.ManualAudioPort > 0;

        /// <summary>Porte in uso adesso da questo PC: server le sue due, client quelle per il collegamento inverso (0 se nessuna).</summary>
        public (int control, int audio) PortsInUse => IsServer ? (_ctlPort, _audPort) : (_cCtlPort, _cAudPort);
        public string LanAddress => LanIp();
        /// <summary>Spiegazione del tipo di NAT trovato (vuota se le porte arrivano da internet senza ostacoli).</summary>
        public string NatNote => (IsServer ? _ports : _cports)?.NatNote() ?? "";

        /// <summary>Nuove porte scelte dall'utente: si salvano e, sul server senza client collegato, si riaprono subito.</summary>
        public void SetManualPorts(int control, int audio)
        {
            control = control is > 0 and < 65536 ? control : 0;
            audio = audio is > 0 and < 65536 && audio != control ? audio : 0;
            if (control == 0 || audio == 0) control = audio = 0;
            _s.ManualControlPort = control; _s.ManualAudioPort = audio;
            _s.Save();
            SessionLog.Write(control > 0 ? $"PORTE porte scelte dall'utente: controllo TCP {control}, audio TCP e UDP {audio}"
                                         : "PORTE porte scelte dall'utente tolte: si torna alle porte automatiche");
            if (IsServer && !_portsBusy && _control == null) _ = StartServerAsync(true);
        }

        /// <summary>
        /// Server: usa le due porte scelte dall'utente (stesso numero dentro e fuori). Con UPnP si chiede anche al
        /// router di aprirle; senza, devono essere inoltrate a mano. Si tengono comunque: valgono anche in rete locale.
        /// </summary>
        private async Task<bool> OpenManualServerPortsAsync(CancellationToken ct)
        {
            var pm = _ports!;
            int cp = _s.ManualControlPort, ap = _s.ManualAudioPort;
            bool upnp = pm.UpnpAvailable;
            TcpListener cl, al;
            try { cl = new TcpListener(IPAddress.Any, cp); cl.Start(); }
            catch { PortLog($"PORTE porta scelta {cp}: già usata da un altro programma di questo PC, torno alle porte automatiche"); return false; }
            try { al = new TcpListener(IPAddress.Any, ap); al.Start(); }
            catch { try { cl.Stop(); } catch { } PortLog($"PORTE porta scelta {ap}: già usata da un altro programma di questo PC, torno alle porte automatiche"); return false; }
            bool mc = upnp && await pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, cp, cp, "IP-TRX-CTRL controllo");
            bool ma = upnp && await pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, ap, ap, "IP-TRX-CTRL audio");
            if (ma) await pm.OpenFixedAsync(Open.Nat.Protocol.Udp, ap, ap, "IP-TRX-CTRL audio (UDP)");
            _ = Task.Run(() => AcceptLoopAsync(cl, ChannelKind.Control, ct));
            _ = Task.Run(() => AcceptLoopAsync(al, ChannelKind.Audio, ct));
            bool? rc = await ProbeOwnAsync(cp, PortLog, ct);
            bool? ra = rc == false ? false : await ProbeOwnAsync(ap, PortLog, ct);
            _ctlPort = cp; _ctlListener = cl;
            _audPort = ap; _audListener = al; _audLocal = ap;
            _keptUnreachable = rc == false || ra == false;
            PortLog($"PORTE uso le porte scelte: controllo TCP {cp}, audio TCP e UDP {ap}" + (mc || ma ? " (aperte anche con UPnP)" : " (inoltro manuale sul router)")
                    + (_keptUnreachable ? $" · da internet NON raggiungibili: controlla l'inoltro sul router verso {LanIp()}" : " · raggiungibili da internet"));
            return true;
        }

        /// <summary>Server: cerca le prime due porte utili (controllo e audio); poi verranno comunicate al broker MQTT.</summary>
        private async Task<bool> OpenServerPortsAsync(CancellationToken ct)
        {
            if (_ports == null) return false;
            _keptUnreachable = false;
            if (ManualPortsSet && await OpenManualServerPortsAsync(ct))
            {
                if (_keptUnreachable) ShowUnreachable();
                return true;
            }
            if (_preferredPorts is { Count: >= 2 } pref && _ports.UpnpAvailable && await OpenPreferredPortsAsync(pref, ct)) return true;
            var c = await OpenVerifiedPortAsync(_ports, "IP-TRX-CTRL controllo", ChannelKind.Control, false, ct);
            var a = c.port > 0 ? await OpenVerifiedPortAsync(_ports, "IP-TRX-CTRL audio", ChannelKind.Audio, false, ct) : (0, null);
            if (c.port == 0 || a.port == 0)
            {
                try { c.listener?.Stop(); } catch { }
                if (c.port > 0) await _ports.ClosePortAsync(c.port);
                return false;
            }
            _ctlPort = c.port; _ctlListener = c.listener;
            _audPort = a.port; _audListener = a.listener; _audLocal = a.port;
            if (_keptUnreachable) ShowUnreachable();
            PortLog($"PORTE prime due porte utili del server: controllo TCP {_ctlPort}, audio TCP/UDP {_audPort} · le comunico al broker MQTT");
            return true;
        }

        /// <summary>Server: avviso con le due porte da aprire quando da internet non si entra.</summary>
        private void ShowUnreachable()
        {
            var pm = _ports;
            if (pm == null) return;
            string lan = LanIp();
            string why = pm.WrongUpnpDevice != null
                ? $"All'UPnP non risponde il router che porta a internet ma un altro apparecchio della rete ({pm.WrongUpnpDevice}: ripetitore, nodo mesh, decoder o router in cascata), quindi le porte non sono state aperte sul router giusto."
                : pm.UpnpAvailable
                    ? "Il router ha accettato le porte con UPnP, ma da internet non si entra" + (pm.NatNote().Length > 0 ? $" ({pm.NatNote()})." : " (spesso è l'IP condiviso dell'operatore, CGNAT).")
                    : "Il router di questo PC non risponde con UPnP, quindi le porte non sono state aperte.";
            bool manual = ManualPortsSet && _ctlPort == _s.ManualControlPort;
            PortsUnreachable?.Invoke(why
                + $"\n\nPorte da aprire sul router principale verso questo PC ({lan}):"
                + $"\n• {_ctlPort} TCP (controllo)"
                + $"\n• {_audPort} TCP e UDP (audio)"
                + $"\nIP pubblico {pm.ExternalIp}."
                + (manual ? "\n\nSono le porte che hai scelto: controlla che l'inoltro sul router punti a questo PC."
                          : "\n\nQueste porte cambiano a ogni avvio: per inoltrarle a mano scegline due fisse in Impostazioni › PORTE APERTE SUL ROUTER.")
                + "\n\nIn alternativa: attivare UPnP sul router principale, mettere in DMZ il PC " + lan + ", oppure attivare UPnP sul router del client (allora apre le porte il client)."
                + "\n\nDalla stessa rete locale il collegamento funziona comunque.");
        }

        /// <summary>
        /// Server: apre come porte ESTERNE quelle che il client riesce a usare in uscita (per esempio 443 e 80),
        /// inoltrate verso porte interne libere del PC. Prova la lista in ordine finché due vanno.
        /// </summary>
        private async Task<bool> OpenPreferredPortsAsync(List<int> pref, CancellationToken ct)
        {
            PortLog($"PORTE il client esce solo da alcune porte ({string.Join(", ", pref)}): le chiedo al router come porte esterne");
            int ctl = 0, aud = 0, ctlIn = 0, audIn = 0;
            TcpListener? ctlL = null, audL = null;
            foreach (int ext in pref)
            {
                if (ct.IsCancellationRequested || (ctl > 0 && aud > 0)) break;
                bool forAudio = ctl > 0;
                string desc = forAudio ? "IP-TRX-CTRL audio" : "IP-TRX-CTRL controllo";
                int inner = _ports!.PickLocalPort();
                if (inner == 0) break;
                if (!await _ports.OpenFixedAsync(Open.Nat.Protocol.Tcp, ext, inner, desc)) continue;
                TcpListener l;
                try { l = new TcpListener(IPAddress.Any, inner); l.Start(); }
                catch { await _ports.ClosePortAsync(ext); continue; }
                var kind = forAudio ? ChannelKind.Audio : ChannelKind.Control;
                _ = Task.Run(() => AcceptLoopAsync(l, kind, ct));
                if (forAudio) await _ports.OpenFixedAsync(Open.Nat.Protocol.Udp, ext, inner, desc + " (UDP)");
                bool? reach = await ProbeOwnAsync(ext, PortLog, ct);
                if (reach == false)
                {
                    PortLog($"PORTE porta esterna {ext} non raggiungibile da internet: provo la successiva");
                    try { l.Stop(); } catch { }
                    await _ports.ClosePortAsync(ext);
                    continue;
                }
                if (forAudio) { aud = ext; audIn = inner; audL = l; }
                else { ctl = ext; ctlIn = inner; ctlL = l; }
            }
            if (ctl == 0 || aud == 0)
            {
                PortLog("PORTE con le porte del client non sono riuscito ad aprirne due: torno alle porte casuali");
                try { ctlL?.Stop(); } catch { }
                if (ctl > 0) await _ports!.ClosePortAsync(ctl);
                return false;
            }
            _ctlPort = ctl; _ctlListener = ctlL;
            _audPort = aud; _audListener = audL; _audLocal = audIn;
            PortLog($"PORTE porte scelte per il client: controllo {ctl} (PC {ctlIn}), audio {aud} (PC {audIn}) · le comunico al broker MQTT");
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
            _ctlPort = 0; _audPort = 0; _audLocal = 0; _ctlListener = null; _audListener = null;
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
            long start = DateTime.UtcNow.AddMinutes(-2).Ticks;   // le proposte vecchie non contano
            var lastTicks = new Dictionary<string, long>();
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(4), ct); } catch { break; }
                if (_control != null || _ports == null) continue;
                // ogni client abbinato ha il suo canale cifrato sul broker: guardo le proposte di tutti
                List<(string id, Rendezvous rdv, byte[] key)> all;
                lock (_clientKeys) all = _clientRdvs.Select(kv => (kv.Key, kv.Value, _clientKeys[kv.Key])).ToList();
                PortOffer? o = null;
                string cid = "";
                byte[]? ckey = null;
                foreach (var (id, rdv, key) in all)
                {
                    PortOffer? x;
                    try { x = await rdv.LookupOfferAsync(ct); } catch { continue; }
                    if (x == null || x.Ticks <= lastTicks.GetValueOrDefault(id, start)) continue;
                    lastTicks[id] = x.Ticks;
                    o = x; cid = id; ckey = key;
                    break;
                }
                if (o == null || ckey == null) continue;
                SessionLog.Write($"PORTE giro {o.Round}/{MaxPortRounds}: {ClientName(cid)} propone {o.Ip} porte {o.ControlPort}/{o.AudioPort} (non ha raggiunto la porta {o.TriedServerPort} del server)"
                    + (o.Restricted ? $" · la sua rete fa uscire solo le porte {string.Join(", ", o.OutboundPorts)}" : ""));
                if (o.Restricted && o.OutboundPorts.Count >= 2)
                {
                    _preferredPorts = o.OutboundPorts;
                    bool already = o.OutboundPorts.Contains(_ctlPort) && o.OutboundPorts.Contains(_audPort);
                    if (!already && _ports.UpnpAvailable)
                    {
                        await RotateServerPortsAsync(ct);
                        continue;
                    }
                    if (already && o.ControlPort == 0 && o.Round == 0) continue;   // le porte giuste sono già aperte
                }
                else if (o.Restricted)
                    SessionLog.Write("PORTE la rete del client fa uscire meno di due porte utili: il collegamento da quella rete non è possibile");
                if (o.ControlPort > 0 && IPAddress.TryParse(o.Ip, out var ip)
                    && await ConnectReverseAsync(ip, o.ControlPort, o.AudioPort, ckey, ct)) continue;
                if (o.TriedServerPort == 0 || o.TriedServerPort == _ctlPort || _ctlPort == 0)
                    await RotateServerPortsAsync(ct);
            }
        }

        private async Task<bool> ConnectReverseAsync(IPAddress ip, int ctlPort, int audPort, byte[] key, CancellationToken ct)
        {
            var tcp = new TcpClient();
            try
            {
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(6000);
                    await tcp.ConnectAsync(ip, ctlPort, t.Token);
                }
                var res = await SecureChannel.AcceptAsync(tcp, ChannelKind.Control, key, null, ct);
                if (res.Channel == null) { tcp.Dispose(); return false; }
                SessionLog.Write($"PORTE collegamento inverso riuscito: il server si è collegato alle porte del client {ip}:{ctlPort}");
                var ctlCh = res.Channel;
                _ = Task.Run(() => ServeChannelAsync(ctlCh, ChannelKind.Control, key, ct));
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
                        var ar = await SecureChannel.AcceptAsync(at, ChannelKind.Audio, key, null, ct);
                        if (ar.Channel != null)
                        {
                            var audCh = ar.Channel;
                            _ = Task.Run(() => ServeChannelAsync(audCh, ChannelKind.Audio, key, ct));
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
            _webCts?.Cancel();
            _webCts = null;
            Web?.Stop();
            _serverCts?.Cancel();
            _serverCts = null;
            try { _ctlListener?.Stop(); } catch { }
            try { _audListener?.Stop(); } catch { }
            _ctlListener = null; _audListener = null;
            await StopV6LockedAsync();
            if (closeChannels) CloseChannels();
            if (_ports != null) await _ports.CloseAllAsync();
            _ports = null;
            _ctlPort = 0; _audPort = 0; _audLocal = 0;
            _remotePtt = false;
            Stats.ServerEndpoint = "";
        }

        // =================================================================== IPv6 diretto e PCP (0.3.7, da rigcontrol)

        private static string ShowIp(IPAddress a) =>
            a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv4MappedToIPv6 ? $"[{a}]" : a.MapToIPv4().ToString();

        private TcpListener? _v6Ctl, _v6Aud;
        private CancellationTokenSource? _v6Cts;
        private IPAddress? _v6Addr, _v6Gw;
        private int _v6CtlPort, _v6AudPort;
        private DateTime _v6PcpRenew = DateTime.MinValue;
        private bool _v6PcpOk;
        public string Ipv6Status { get; private set; } = "";

        /// <summary>
        /// Server: con un IPv6 pubblico ascolta anche su IPv6 (stesse porte interne, nessuna traduzione) e chiede al
        /// router con PCP di lasciar passare le connessioni in arrivo. Restituisce l'indirizzo da pubblicare.
        /// </summary>
        private readonly SemaphoreSlim _v6Lock = new(1, 1);

        private async Task<Rendezvous.V6Address?> EnsureV6Async(CancellationToken ct)
        {
            await _v6Lock.WaitAsync(ct);
            try { return await EnsureV6InnerAsync(ct); }
            finally { _v6Lock.Release(); }
        }

        private async Task StopV6LockedAsync()
        {
            await _v6Lock.WaitAsync();
            try { await StopV6Async(); }
            finally { _v6Lock.Release(); }
        }

        private async Task<Rendezvous.V6Address?> EnsureV6InnerAsync(CancellationToken ct)
        {
            int c = (_ctlListener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
            int a = (_audListener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
            var (addr, gw) = Pcp.StableIpv6();
            if (!_s.Ipv6Direct || !Socket.OSSupportsIPv6 || addr == null || c == 0 || a == 0)
            {
                await StopV6Async();
                Ipv6Status = !_s.Ipv6Direct ? "spento nelle Impostazioni" : addr == null ? "nessun indirizzo IPv6 pubblico su questo PC" : "";
                return null;
            }
            if (_v6Ctl == null || c != _v6CtlPort || a != _v6AudPort || !addr.Equals(_v6Addr))
            {
                await StopV6Async();
                try
                {
                    _v6Cts = new CancellationTokenSource();
                    _v6Ctl = ListenV6(c); _v6Aud = ListenV6(a);
                    _v6CtlPort = c; _v6AudPort = a; _v6Addr = addr; _v6Gw = gw;
                    var vct = _v6Cts.Token;
                    var lc = _v6Ctl; var la = _v6Aud;
                    _ = Task.Run(() => AcceptLoopAsync(lc, ChannelKind.Control, vct));
                    _ = Task.Run(() => AcceptLoopAsync(la, ChannelKind.Audio, vct));
                    SessionLog.Write($"IPV6 il server ascolta anche su [{addr}]:{c}/{a}");
                }
                catch (Exception ex)
                {
                    SessionLog.Write("IPV6 non disponibile: " + ex.Message);
                    Ipv6Status = "non disponibile: " + ex.Message;
                    await StopV6Async();
                    return null;
                }
            }
            if (_s.PcpEnabled && gw != null && DateTime.UtcNow >= _v6PcpRenew)
            {
                try
                {
                    var r1 = await Pcp.MapAsync(gw, addr, c, true, 7200, ct);
                    var r2 = r1.Ok ? await Pcp.MapAsync(gw, addr, a, true, 7200, ct) : r1;
                    _v6PcpOk = r1.Ok && r2.Ok;
                    int life = _v6PcpOk ? Math.Max(120, Math.Min(r1.Lifetime, r2.Lifetime)) : 1800;
                    _v6PcpRenew = DateTime.UtcNow.AddSeconds(_v6PcpOk ? life / 2 : life);
                    SessionLog.Write(_v6PcpOk
                        ? $"IPV6 PCP: il router lascia passare le porte {c}/{a} per {life / 60} minuti"
                        : $"IPV6 PCP: {Pcp.Describe(r1.Ok ? r2.ResultCode : r1.ResultCode)}; le porte IPv6 vanno aperte a mano nel router (firewall IPv6)");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SessionLog.Write("IPV6 PCP errore: " + ex.Message); _v6PcpRenew = DateTime.UtcNow.AddMinutes(30); }
            }
            Ipv6Status = $"[{addr}]:{c}/{a}" + (_s.PcpEnabled ? (_v6PcpOk ? " · aperte con PCP" : " · PCP non riuscito: firewall IPv6 da aprire a mano") : "");
            return new Rendezvous.V6Address(addr, c, a, DateTime.UtcNow.Ticks);
        }

        private static TcpListener ListenV6(int port)
        {
            var l = new TcpListener(IPAddress.IPv6Any, port);
            l.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);   // l'IPv4 resta all'altro socket
            l.Start();
            return l;
        }

        private async Task StopV6Async()
        {
            _v6Cts?.Cancel(); _v6Cts = null;
            try { _v6Ctl?.Stop(); } catch { }
            try { _v6Aud?.Stop(); } catch { }
            _v6Ctl = null; _v6Aud = null;
            if (_v6PcpOk && _v6Gw != null && _v6Addr != null)
            {
                try
                {
                    using var t = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await Pcp.MapAsync(_v6Gw, _v6Addr, _v6CtlPort, true, 0, t.Token);
                    await Pcp.MapAsync(_v6Gw, _v6Addr, _v6AudPort, true, 0, t.Token);
                }
                catch { }
            }
            _v6PcpOk = false; _v6PcpRenew = DateTime.MinValue;
            _v6CtlPort = 0; _v6AudPort = 0;
        }

        // ---- client: tentativo IPv6
        private ServerAddress? _v6Target;
        private DateTime _v6FailedUntil = DateTime.MinValue;

        private async Task<SecureChannel?> TryV6Async(CancellationToken ct)
        {
            _v6Target = null;
            var v6 = _rdv?.LastV6;
            if (!_s.Ipv6Direct || v6 == null || DateTime.UtcNow < _v6FailedUntil || !Socket.OSSupportsIPv6) return null;
            if (Pcp.StableIpv6().addr == null) return null;   // questo PC non ha IPv6 pubblico
            var t = new ServerAddress(v6.Ip, v6.ControlPort, v6.AudioPort);
            SessionLog.Write($"IPV6 provo il server su [{v6.Ip}]:{v6.ControlPort}");
            var ch = await TryConnectAsync(t, ChannelKind.Control, ct);
            if (ch == null)
            {
                _v6FailedUntil = DateTime.UtcNow.AddMinutes(30);   // firewall IPv6 chiuso: per un po' si va diretti su IPv4
                SessionLog.Write("IPV6 il server non risponde su IPv6 (firewall del router?): uso IPv4");
                return null;
            }
            SessionLog.Write("IPV6 collegato al server su IPv6: niente NAT di mezzo");
            _v6Target = t;
            return ch;
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
            // numero a 10 cifre: non contiene l'indirizzo (che il client riceve cifrato dal broker)
            string number = Crypto.MakePairingNumber();
            long window = Crypto.PairingWindow(DateTime.UtcNow);
            _pairingSecret = await Task.Run(() => Crypto.PairingSecretFromNumber(number, window), ct);
            string code = Crypto.FormatPairingNumber(number);
            Log('·', $"numero di abbinamento pronto: vale {Crypto.PairingWindowMinutes} minuti e una sola volta");
            lock (_pairTriedSrv) _pairTriedSrv.Clear();
            _pairLastTicks = 0;
            if (_keptUnreachable)
                Log('·', "le porte del server non sono raggiungibili da internet" + (_ports != null && _ports.NatNote().Length > 0 ? $" ({_ports.NatNote()})" : "")
                         + ": se il client non entra, si passa al ciclo delle porte con il collegamento inverso");

            _pairListenCts?.Cancel();
            _pairListenCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var secret = _pairingSecret;
            var lct = _pairListenCts.Token;
            _ = Task.Run(() => PairingRendezvous.ServerListenAsync(secret, PairInfo,
                o => OnPairOfferAsync(o, secret, lct), _s.MqttBrokers, Log, lct));
            // scadenza: dopo 10 minuti il numero non vale più (salvo un ciclo delle porte già in corso)
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromMinutes(Crypto.PairingWindowMinutes), lct); } catch { return; }
                if (!ReferenceEquals(_pairingSecret, secret) || _pairLastTicks != 0) return;
                Log('✗', $"il numero è scaduto ({Crypto.PairingWindowMinutes} minuti): premi Riprova per averne uno nuovo");
                CancelPairing();
                PairingExpired?.Invoke();
            });
            return code;
        }

        public void CancelPairing()
        {
            _pairingSecret = null;
            _pairListenCts?.Cancel();
            _ = ClosePairRoundPortAsync();
        }

        private PairServerInfo PairInfo() =>
            new(CurrentServerAddress, _ports?.UpnpAvailable == true, LanIp() == "—" ? "" : LanIp(), _keptUnreachable, _ports?.CgnatSuspected == true);

        private static string Esito(bool? reach) => reach == true ? "raggiungibile" : reach == false ? "NON raggiungibile" : "non verificabile";

        /// <summary>Perché una connessione non è riuscita, in parole semplici.</summary>
        private static string Why(Exception ex) => ex switch
        {
            OperationCanceledException => "nessuna risposta (i pacchetti vengono scartati: firewall, router senza inoltro o CGNAT)",
            SocketException se when se.SocketErrorCode == SocketError.ConnectionRefused => "connessione rifiutata (nessun programma in ascolto su quella porta)",
            SocketException se => "errore di rete " + se.SocketErrorCode,
            CryptographicException => "il codice di abbinamento non corrisponde",
            IOException => "collegamento chiuso durante lo scambio",
            _ => ex.Message
        };

        /// <summary>
        /// Server, ciclo delle porte in abbinamento: per ogni proposta del client prova a collegarsi alla sua porta
        /// (collegamento inverso); se non riesce apre una porta NUOVA (mai provata prima: a giri alterni una comune
        /// come 443/80/8080, oppure una a caso), la verifica da internet e comunica numero ed esito al client.
        /// </summary>
        private async Task<PairRoundReply?> OnPairOfferAsync(PortOffer o, byte[] secret, CancellationToken ct)
        {
            try { await _pairRoundLock.WaitAsync(ct); } catch { return null; }
            try
            {
                if (!IsActivePairingSecret(secret) || o.Ticks <= _pairLastTicks) return null;
                _pairLastTicks = o.Ticks;   // la stessa proposta arriva da più broker: una sola volta
                Log('◀', $"giro {o.Round}/{MaxPortRounds} · il client propone IP {o.Ip}, porta TCP {(o.ControlPort > 0 ? o.ControlPort.ToString() : "nessuna")}"
                         + (o.Restricted ? $" · dalla sua rete escono solo le porte {string.Join(", ", o.OutboundPorts)}" : ""));

                // 1) collegamento inverso: il server si collega alla porta aperta dal client
                if (o.ControlPort > 0 && IPAddress.TryParse(o.Ip, out var cip) && await PairReverseAsync(cip, o.ControlPort, secret, ct))
                    return null;
                if (!IsActivePairingSecret(secret)) return null;

                // 2) porta nuova del server
                var pm = _ports;
                if (pm == null) return new PairRoundReply(o.Round, "", 0, 0, -1, false, false, "porte del server non pronte");
                var sct = _serverCts?.Token ?? ct;
                int ext, inner;
                lock (_pairTriedSrv) { if (_ctlPort > 0) _pairTriedSrv.Add(_ctlPort); if (_audPort > 0) _pairTriedSrv.Add(_audPort); }
                (ext, inner) = await pm.OpenDistinctTcpAsync(_pairTriedSrv, o.Restricted ? o.OutboundPorts : new List<int>(), o.Round % 2 == 0, "IP-TRX-CTRL abbinamento", ct);
                await ClosePairRoundPortAsync();
                string ip = (pm.ExternalIp ?? CurrentServerAddress?.Ip)?.ToString() ?? "";
                string note = pm.NatNote().Length > 0 ? "server: " + pm.NatNote() : "";
                if (ext == 0)
                {
                    Log('✗', $"giro {o.Round} · il router del server non ha accettato una porta nuova");
                    return new PairRoundReply(o.Round, ip, 0, 0, -1, pm.UpnpAvailable, pm.CgnatSuspected, "il router del server non accetta altre porte");
                }
                TcpListener l;
                try { l = new TcpListener(IPAddress.Any, inner); l.Start(); }
                catch (Exception ex)
                {
                    await pm.ClosePortAsync(ext);
                    Log('✗', $"giro {o.Round} · porta {inner} del PC non apribile ({ex.Message})");
                    return new PairRoundReply(o.Round, ip, 0, 0, -1, pm.UpnpAvailable, pm.CgnatSuspected, "porta del PC occupata");
                }
                _pairRoundPort = ext; _pairRoundListener = l;
                _ = Task.Run(() => AcceptLoopAsync(l, ChannelKind.Control, sct));
                bool? reach = await ProbeOwnAsync(ext, SessionLog.Write, ct);
                Log('▶', $"giro {o.Round} · nuova porta del server TCP {ext}" + (inner != ext ? $" (porta del PC {inner})" : "")
                         + $" · verifica da internet: {Esito(reach)}" + (note.Length > 0 ? " · " + note : ""));
                if (reach == true) PromotePairRoundPort();
                return new PairRoundReply(o.Round, ip, ext, inner, reach == true ? 1 : reach == false ? 0 : -1, pm.UpnpAvailable, pm.CgnatSuspected, note);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log('✗', $"giro {o.Round}: {ex.Message}");
                return null;
            }
            catch { return null; }
            finally { _pairRoundLock.Release(); }
        }

        /// <summary>Server: si collega alla porta del client e fa l'abbinamento su quella connessione.</summary>
        private async Task<bool> PairReverseAsync(IPAddress ip, int port, byte[] secret, CancellationToken ct)
        {
            Log('▶', $"TCP {ip}:{port}  collegamento inverso verso la porta del client");
            var tcp = new TcpClient();
            try
            {
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(6000);
                    await tcp.ConnectAsync(ip, port, t.Token);
                }
                var res = await SecureChannel.AcceptAsync(tcp, ChannelKind.Control, (Func<byte[], byte[]?>?)null, new[] { secret }, ct);
                if (res.NewMaster != null && CompleteServerPairing(res.NewMaster, $" · collegamento inverso sulla porta {port} del client", secret)) return true;
                res.Channel?.Dispose();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log('✗', $"porta {port} del client: {Why(ex)}"); }
            catch { }
            finally { tcp.Dispose(); }
            return false;
        }

        /// <summary>Server: la porta del giro è raggiungibile da internet e diventa la porta di controllo.</summary>
        private void PromotePairRoundPort()
        {
            var l = _pairRoundListener; int p = _pairRoundPort;
            if (l == null || p == 0 || p == _ctlPort) return;
            var oldL = _ctlListener; int oldP = _ctlPort;
            _ctlPort = p; _ctlListener = l;
            _pairRoundPort = 0; _pairRoundListener = null;
            try { oldL?.Stop(); } catch { }
            if (oldP > 0 && _ports != null) _ = _ports.ClosePortAsync(oldP);
            UpdateServerEndpoint();
            PortLog($"PORTE la porta {p} è raggiungibile: ora è la porta di controllo del server (la {oldP} è chiusa)");
        }

        private async Task ClosePairRoundPortAsync()
        {
            var l = _pairRoundListener; int p = _pairRoundPort;
            _pairRoundListener = null; _pairRoundPort = 0;
            try { l?.Stop(); } catch { }
            if (p > 0 && p != _ctlPort && _ports != null) await _ports.ClosePortAsync(p);
        }

        /// <summary>
        /// Server: abbinamento riuscito. Con il numero temporaneo vale il primo (poi il numero non vale più);
        /// con il codice fisso il codice resta valido. In tutti e due i casi il client si aggiunge all'elenco.
        /// </summary>
        private bool CompleteServerPairing(byte[] master, string how, byte[]? usedSecret)
        {
            bool fixedCode = usedSecret != null && ReferenceEquals(usedSecret, _fixedSecret);
            if (!fixedCode)
            {
                lock (_pairDoneLock)
                {
                    if (_pairingSecret == null) return false;
                    _pairingSecret = null;
                }
                _pairListenCts?.Cancel();
            }
            SetMaster(master, fixedCode ? "codice fisso" : "numero temporaneo");
            Log('◀', "handshake ECDH P-256 ok · chiave AES-256 derivata");
            Log('✓', "Abbinamento completato" + how + (fixedCode ? " · con il codice fisso del server" : ""));
            PairingCompleted?.Invoke();
            if (_pairingSecret == null) _ = ClosePairRoundPortAsync();
            var sct = _serverCts?.Token ?? CancellationToken.None;
            _ = PublishNowAsync(sct);
            return true;
        }

        private bool IsActivePairingSecret(byte[] secret) =>
            ReferenceEquals(secret, _pairingSecret) || (ReferenceEquals(secret, _fixedSecret) && _s.FixedCodeEnabled);

        /// <summary>Segreti di abbinamento accettati ora dal server: numero temporaneo e codice fisso (se attivo).</summary>
        private IReadOnlyList<byte[]>? PairingSecrets()
        {
            var l = new List<byte[]>(2);
            if (_pairingSecret is { } p) l.Add(p);
            if (_s.FixedCodeEnabled && _fixedSecret is { } f) l.Add(f);
            return l.Count > 0 ? l : null;
        }

        /// <summary>Server: chiave del client che si presenta con questo identificativo di abbinamento.</summary>
        private byte[]? MasterFor(byte[] pairId)
        {
            lock (_clientKeys)
                foreach (var k in _clientKeys.Values)
                    if (Crypto.Equal(pairId, Crypto.PairId(k))) return k;
            return null;
        }

        private async Task AcceptLoopAsync(TcpListener listener, ChannelKind kind, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                Interlocked.Increment(ref _accepted);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var res = await SecureChannel.AcceptAsync(tcp, kind, MasterFor, PairingSecrets(), ct);
                        if (res.NewMaster != null)
                        {
                            // abbinamento arrivato sulla porta di un giro del ciclo: diventa la porta di controllo
                            if (ReferenceEquals(listener, _pairRoundListener)) PromotePairRoundPort();
                            CompleteServerPairing(res.NewMaster, "", res.PairingSecret);
                            return;
                        }
                        if (res.Channel == null || res.Master == null) return;
                        await ServeChannelAsync(res.Channel, kind, res.Master, ct);
                    }
                    catch { }
                }, ct);
            }
        }

        /// <summary>Server: canale autenticato (in arrivo o collegamento inverso) fino alla sua chiusura.</summary>
        private async Task ServeChannelAsync(SecureChannel ch, ChannelKind kind, byte[] master, CancellationToken ct)
        {
            string cid = KeyIdOf(master);
            if (kind == ChannelKind.Control)
            {
                // un operatore per volta: un altro client collegato riceve «server occupato»;
                // lo stesso client che si ricollega (cambio di rete) prende il posto della sua vecchia sessione
                if (_control != null && _sessionClientId.Length > 0 && _sessionClientId != cid && CanAddListener())
                {
                    await ServeListenerAsync(ch, cid, ct);
                    return;
                }
                if (_control != null && _sessionClientId.Length > 0 && _sessionClientId != cid)
                {
                    string who = _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : ClientName(_sessionClientId);
                    SessionLog.Write($"SESSIONE rifiutata a {ClientName(cid)}: è collegato {who}");
                    try { await ch.SendAsync(new MsgWriter(Msg.Busy).Str(who).Build(), ct); await Task.Delay(500, ct); } catch { }
                    ch.Dispose();
                    return;
                }
                if (_sessionClientId != cid) StopUdp();
                _peerOpus = false;   // fino al saluto dell'altro programma
                _master = master;
                _sessionClientId = cid;
                if (_s.Clients.FirstOrDefault(x => x.Id == cid) is { } ce) { ce.LastUsed = DateTime.Now; _s.Save(); }
                var old = Interlocked.Exchange(ref _control, ch);
                old?.Dispose();
                OnConnected();
                await SendHelloAsync(ch);
                try { await ch.SendAsync(new MsgWriter(Msg.PeerInfo).Str(ShowIp(ch.Remote.Address)).Build(), ct); } catch { }
                Stats.ClientEndpoint = ShowIp(ch.Remote.Address);
                _ = SendDeviceStateAsync();
                SendServerMemories();
                _ = SendRecentNotesAsync(ch);
                _spectrumWanted = false;
                _remotePttRefusedNotified = false;
                _ = Task.Run(() => OfferUdpAsync(ct));
                await ReceiveLoopAsync(ch, ct);
            }
            else
            {
                if (_sessionClientId.Length > 0 && _sessionClientId != cid)
                {
                    // audio di un client in solo ascolto: riceve la radio come l'operatore
                    Listener? li;
                    lock (_listeners) li = _listeners.FirstOrDefault(x => x.Cid == cid);
                    if (li == null) { ch.Dispose(); return; }
                    Interlocked.Exchange(ref li.Aud, ch)?.Dispose();
                    try { while (!ct.IsCancellationRequested && await ch.ReceiveAsync(ct) is { Length: > 0 }) { } } catch { }
                    Interlocked.CompareExchange(ref li.Aud, null, ch);
                    ch.Dispose();
                    return;
                }
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
                        if (ClientCount > 0 && (changed || DateTime.UtcNow - lastPublish > TimeSpan.FromMinutes(10)))
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

        /// <summary>Server: indirizzo cifrato sul broker per ogni client abbinato (ognuno lo legge con la sua chiave).</summary>
        private async Task PublishNowAsync(CancellationToken ct)
        {
            var addr = CurrentServerAddress;
            if (addr == null) return;
            List<(string id, Rendezvous rdv, byte[] key)> all;
            lock (_clientKeys) all = _clientRdvs.Select(kv => (kv.Key, kv.Value, _clientKeys[kv.Key])).ToList();
            if (all.Count == 0) return;
            var v6 = await EnsureV6Async(ct);
            bool anyOk = false;
            int brokers = 0;
            foreach (var (_, rdv, _) in all)
            {
                bool ok = await rdv.PublishAsync(addr.Value, LanIp(), AppVersion.Tag, ct, v6);
                anyOk |= ok;
                brokers = Math.Max(brokers, rdv.LastOkBrokers);
            }
            Stats.MqttOk = brokers;
            // codice di riserva (broker irraggiungibili): per il client usato più di recente
            var recent = _s.Clients.OrderByDescending(c => c.LastUsed).Select(c => all.FirstOrDefault(a => a.id == c.Id)).FirstOrDefault(a => a.key != null);
            FallbackCodeChanged?.Invoke(anyOk || recent.key == null ? null : Crypto.MakeFallbackCode(recent.key, addr.Value));
        }

        private async Task ReplaceAudioPortAsync(IMsgChannel control, CancellationToken ct)
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
            _audPort = p; _audLocal = p;
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
        /// Client: abbinamento. Cerca il server sui broker MQTT (indirizzo aggiornato ed esito della verifica delle
        /// sue porte), prova il collegamento diretto (prima la rete locale se i due PC escono con lo stesso IP),
        /// poi il ciclo delle porte: fino a 20 giri, ognuno con una porta nuova di questo PC e una nuova del server.
        /// </summary>
        public async Task<string?> PairClientAsync(string code, CancellationToken ct)
        {
            if (!Crypto.TryNormalizeAnyCode(code, out string digits))
            {
                Log('✗', $"servono {Crypto.PairingDigits} cifre (numero temporaneo) o {Crypto.FixedCodeDigits} (codice fisso del server): controlla di averlo scritto bene");
                return $"Numero non valido: servono {Crypto.PairingDigits} cifre, o {Crypto.FixedCodeDigits} per il codice fisso del server.";
            }
            bool fixedCode = digits.Length == Crypto.FixedCodeDigits;
            byte[][] secrets;
            if (fixedCode)
            {
                Log('·', $"codice fisso del server: {Crypto.FormatFixedCode(digits)} · calcolo la chiave (qualche secondo)…");
                secrets = new[] { await Task.Run(() => Crypto.PairingSecretFromFixedCode(digits), ct) };
            }
            else
            {
                // il server ha creato il numero in una finestra di 10 minuti: provo quella attuale, le due prima e
                // quella dopo (attesa fino a 10 minuti e orologi dei due PC non perfettamente allineati)
                Log('·', $"numero letto: {Crypto.FormatPairingNumber(digits)} · calcolo le chiavi (qualche secondo)…");
                long now = Crypto.PairingWindow(DateTime.UtcNow);
                long[] windows = { now, now - 1, now - 2, now + 1 };
                secrets = await Task.Run(() => windows.AsParallel().AsOrdered().Select(w => Crypto.PairingSecretFromNumber(digits, w)).ToArray(), ct);
            }
            var found = await PairingRendezvous.ClientFindAsync(secrets, _s.Callsign, _s.MqttBrokers, Log, ct);
            if (found == null)
            {
                if (ct.IsCancellationRequested) return null;
                if (fixedCode)
                {
                    Log('✗', "nessun server ha risposto a questo codice fisso: controlla le cifre, che il server sia acceso e che sul server il codice fisso sia attivo");
                    return "Nessun server ha risposto a questo codice fisso (cifre sbagliate, codice fisso disattivato o cambiato, server spento).";
                }
                Log('✗', "nessun server ha risposto a questo numero: controlla le cifre, che non sia scaduto (vale 10 minuti) e che il server sia in attesa");
                return "Nessun server ha risposto a questo numero (cifre sbagliate, numero scaduto o broker MQTT irraggiungibili).";
            }
            byte[] secret = found.Secret;
            ServerAddress addr;
            string lan = "";
            bool srvUnreach = false;
            {
                addr = found.Addr; lan = found.Lan; srvUnreach = found.Unreachable;
                var (sp, sv) = AppVersion.Parse(found.Version);
                if (!AppVersion.Compatible(sp))
                {
                    string msg = AppVersion.Notes(sp, sv, true);
                    Log('✗', msg);
                    return msg;
                }
                string n = AppVersion.Notes(sp, sv, true);
                Log(n.Length > 0 ? '·' : '✓', n.Length > 0 ? n : $"versioni compatibili: server {sv}, client {AppVersion.Text}");
            }

            // stessa rete? Se questo PC esce su internet con lo stesso IP del server, conta la rete locale
            IPAddress? myPub = null;
            try { myPub = await PortManager.GetPublicIpAsync(ct); } catch { }
            IPAddress? lanIp = IPAddress.TryParse(lan, out var li) ? li : null;
            bool sameNet = myPub != null && myPub.Equals(addr.Ip);
            if (myPub != null) Log('·', $"IP pubblico di questo PC {myPub}" + (sameNet ? " · uguale a quello del server: siete sulla stessa rete, provo prima la rete locale" : ""));

            var direct = new List<(ServerAddress a, string label, bool local)>();
            if (sameNet && lanIp != null) direct.Add((addr with { Ip = lanIp }, "rete locale", true));
            direct.Add((addr, "indirizzo pubblico", false));
            if (!sameNet && lanIp != null) direct.Add((addr with { Ip = lanIp }, "rete locale", true));

            byte[]? master = null;
            ServerAddress used = addr;
            foreach (var (a, label, local) in direct)
            {
                if (ct.IsCancellationRequested) return null;
                Log('▶', $"TCP {a.Ip}:{a.ControlPort}  apertura canale di controllo ({label})");
                try { master = await SecureChannel.PairAsClientAsync(a, secret, ct, 6); used = a; break; }
                catch (CryptographicException)
                {
                    Log('✗', "il server ha risposto ma il codice non corrisponde: ricopia il codice mostrato dal server");
                    return "Codice di abbinamento errato.";
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    bool silent = ex is OperationCanceledException;
                    string hint = local && silent && sameNet ? " → il PC server scarta le connessioni: quasi certamente il firewall di Windows sul server (Impostazioni › Rete › «Consenti nel firewall»)"
                        : local && silent ? " (normale se questo PC non è sulla rete di casa del server)"
                        : !local && silent && srvUnreach ? " (atteso: il server ha verificato che da internet non si entra)"
                        : !local && silent && sameNet ? " (molti router non permettono di rientrare dal proprio IP pubblico)"
                        : "";
                    Log('✗', $"{label}: {Why(ex)}{hint}");
                }
            }

            if (master == null && !ct.IsCancellationRequested)
            {
                var r = await PairPortCycleAsync(secret, addr, lanIp, srvUnreach, found.Cgnat, ct);
                if (r != null) { master = r.Value.master; used = r.Value.addr; }
            }
            if (master == null)
                return ct.IsCancellationRequested ? null : $"Abbinamento non riuscito dopo il ciclo delle porte (vedi il registro).";

            Log('◀', "handshake ECDH P-256 ok · chiave AES-256 derivata");
            SetMaster(master);
            _s.LastServerLan = lan;
            RememberServer(used);
            Log('✓', "Abbinamento completato");
            PairingCompleted?.Invoke();
            StartClient();
            return null;
        }

        /// <summary>
        /// Client: ciclo delle porte in abbinamento. A ogni giro apre una porta NUOVA di questo PC (a giri alterni
        /// una comune come 443/80/8080 o una a caso), la verifica da internet e la propone al server sul broker; il
        /// server prova a collegarsi a lei e intanto apre una porta nuova sua, che il client prova subito dopo.
        /// </summary>
        private async Task<(byte[] master, ServerAddress addr)?> PairPortCycleAsync(byte[] secret, ServerAddress addr, IPAddress? lanIp,
            bool srvUnreach, bool srvCgnat, CancellationToken ct)
        {
            Log('·', $"inizio il ciclo delle porte: fino a {MaxPortRounds} giri, in ognuno una porta nuova di questo PC e una nuova del server");
            await using var session = await PairingRendezvous.ClientSession.OpenAsync(secret, _s.MqttBrokers, Log, ct);
            if (session.Brokers == 0) { Log('✗', "nessun broker MQTT raggiungibile per il ciclo delle porte"); return null; }

            void PLog(string line) { SessionLog.Write(line); PairingLog?.Invoke('·', line.StartsWith("PORTE ") ? line[6..] : line); }
            var pm = new PortManager { Log = PLog };
            bool upnp = await pm.DiscoverAsync(ct);
            if (!upnp) Log('·', ManualPortsSet
                ? $"il router di questo PC non risponde con UPnP: uso le porte scelte in Impostazioni ({_s.ManualControlPort} TCP, {_s.ManualAudioPort} TCP e UDP), che devono essere inoltrate a mano verso {LanIp()}"
                : "il router di questo PC non risponde con UPnP: uso porte del PC, che funzionano solo con DMZ o inoltro manuale. Per inoltrarle a mano scegli due porte fisse in Impostazioni › PORTE APERTE SUL ROUTER");
            _outbound ??= await OutboundProbe.RunAsync(SessionLog.Write, ct);
            if (_outbound.Known && _outbound.Restricted)
                Log('·', $"da questa rete escono solo le porte {string.Join(", ", _outbound.Allowed)}: le comunico al server, che proverà quelle");
            IPAddress? myIp = pm.ExternalIp;
            try { myIp ??= await PortManager.GetPublicIpAsync(ct); } catch { }

            var tried = new HashSet<int>();
            int lastServerPort = addr.ControlPort, noReply = 0, round = 0;
            bool anyMine = false, anySrv = false;
            TcpListener? cur = null;   // porta del giro in corso: chiusa comunque alla fine
            try
            {
                for (round = 1; round <= MaxPortRounds && !ct.IsCancellationRequested; round++)
                {
                    PairingRound?.Invoke(round, MaxPortRounds);
                    Log('·', $"── giro {round} di {MaxPortRounds} ──");
                    var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    TcpListener? l = null;
                    int ext = 0;
                    var (e, inner) = (0, 0);
                    // 0.3.4: la porta di controllo scelta dall'utente (inoltrata a mano) si propone per prima
                    if (ManualPortsSet)
                    {
                        tried.Add(_s.ManualControlPort);
                        int mp = _s.ManualControlPort;
                        if (pm.UpnpAvailable) await pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, mp, mp, "IP-TRX-CTRL abbinamento client");
                        (e, inner) = (mp, mp);
                        if (round == 1) Log('·', $"uso la porta scelta in Impostazioni: TCP {mp} (a ogni giro)");
                    }
                    else (e, inner) = await pm.OpenDistinctTcpAsync(tried, Array.Empty<int>(), round % 2 == 0, "IP-TRX-CTRL abbinamento client", ct);
                    if (e > 0)
                    {
                        try
                        {
                            l = new TcpListener(IPAddress.Any, inner);
                            l.Start();
                            ext = e;
                            cur = l;
                            var lst = l;
                            _ = Task.Run(() => PairListenAsync(lst, secret, tcs, ct));
                        }
                        catch (Exception ex) { Log('✗', $"porta {inner} del PC non apribile ({ex.Message})"); await pm.ClosePortAsync(e); l = null; }
                    }
                    if (ext > 0)
                    {
                        bool? reach = await ProbeOwnAsync(ext, SessionLog.Write, ct);
                        if (reach == true) anyMine = true;
                        Log('·', $"porta di questo PC TCP {ext}" + (inner != ext ? $" (PC {inner})" : "") + $" · verifica da internet: {Esito(reach)}");
                    }
                    else Log('·', "questo PC non ha aperto porte in questo giro: chiedo comunque al server una porta nuova");

                    var offer = new PortOffer(round, myIp?.ToString() ?? "", ext, 0, lastServerPort, DateTime.UtcNow.Ticks)
                    {
                        Restricted = _outbound.Known && _outbound.Restricted,
                        OutboundPorts = _outbound.Allowed
                    };
                    int sent = await session.SendOfferAsync(offer, ct);
                    Log('▶', $"proposta al server: IP {offer.Ip}, porta TCP {(ext > 0 ? ext.ToString() : "nessuna")} ({sent} broker)");

                    // il server si collega alla porta di questo PC, oppure risponde con la sua porta nuova
                    var replyTask = session.WaitRoundAsync(round, TimeSpan.FromSeconds(45), ct);
                    var first = await Task.WhenAny(tcs.Task, replyTask);
                    if (tcs.Task.IsCompletedSuccessfully)
                    {
                        Log('◀', $"il server si è collegato alla porta {ext} di questo PC: collegamento inverso riuscito");
                        return (tcs.Task.Result, addr);
                    }
                    var rep = await replyTask;
                    if (tcs.Task.IsCompletedSuccessfully) return (tcs.Task.Result, addr);
                    if (rep == null)
                    {
                        noReply++;
                        Log('✗', $"giro {round}: nessuna risposta dal server");
                        if (noReply >= 3)
                        {
                            Log('✗', "il server non partecipa al ciclo delle porte: aggiornalo alla 0.2.7 o successiva");
                            break;
                        }
                    }
                    else
                    {
                        noReply = 0;
                        if (rep.Reachable == 1) anySrv = true;
                        Log('◀', $"giro {round} · il server ha aperto la porta TCP {(rep.Port > 0 ? rep.Port.ToString() : "nessuna")}"
                                 + (rep.InnerPort > 0 && rep.InnerPort != rep.Port ? $" (PC {rep.InnerPort})" : "")
                                 + (rep.Port > 0 ? $" · verifica da internet: {(rep.Reachable == 1 ? "raggiungibile" : rep.Reachable == 0 ? "NON raggiungibile" : "non verificabile")}" : "")
                                 + (rep.Note.Length > 0 ? " · " + rep.Note : ""));
                        if (rep.Port > 0)
                        {
                            lastServerPort = rep.Port;
                            var srvIp = IPAddress.TryParse(rep.Ip, out var sip) ? sip : addr.Ip;
                            var tries = new List<(ServerAddress a, string label)> { (addr with { Ip = srvIp, ControlPort = rep.Port }, "porta nuova del server") };
                            if (lanIp != null && rep.InnerPort > 0) tries.Add((addr with { Ip = lanIp, ControlPort = rep.InnerPort }, "porta nuova in rete locale"));
                            foreach (var (a, label) in tries)
                            {
                                if (tcs.Task.IsCompletedSuccessfully) return (tcs.Task.Result, addr);
                                Log('▶', $"TCP {a.Ip}:{a.ControlPort}  {label}");
                                try { return (await SecureChannel.PairAsClientAsync(a, secret, ct, 5), a); }
                                catch (CryptographicException) { Log('✗', "il codice di abbinamento non corrisponde"); return null; }
                                catch (Exception ex) when (!ct.IsCancellationRequested) { Log('✗', $"{label}: {Why(ex)}"); }
                            }
                        }
                    }
                    if (tcs.Task.IsCompletedSuccessfully) return (tcs.Task.Result, addr);
                    try { l?.Stop(); } catch { }
                    if (ext > 0) await pm.ClosePortAsync(ext);
                }
            }
            finally
            {
                try { cur?.Stop(); } catch { }
                await pm.CloseAllAsync();
            }

            if (ct.IsCancellationRequested) return null;
            string why = (srvUnreach || srvCgnat) && !anySrv && !anyMine
                ? (upnp ? "il server è dietro CGNAT o un firewall e nemmeno le porte di questo PC sono raggiungibili da internet (anche qui CGNAT o firewall)"
                        : "il server è dietro CGNAT o un firewall e il router di questo PC non apre porte con UPnP")
                  + ": serve UPnP, DMZ o un inoltro manuale sul router di questo PC, oppure i due PC sulla stessa rete"
                : !anySrv && !anyMine ? "nessuna porta è risultata raggiungibile da internet su nessuno dei due lati"
                : "alcune porte risultavano raggiungibili ma la connessione non è riuscita: controlla il firewall di Windows su entrambi i PC";
            Log('✗', $"ciclo delle porte finito dopo {Math.Min(round, MaxPortRounds)} giri: {why}");
            return null;
        }

        /// <summary>Client: il server si collega alla porta del giro; lo scambio di abbinamento lo avvia comunque il client.</summary>
        private async Task PairListenAsync(TcpListener l, byte[] secret, TaskCompletionSource<byte[]> tcs, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !tcs.Task.IsCompleted)
            {
                TcpClient tcp;
                try { tcp = await l.AcceptTcpClientAsync(ct); } catch { break; }
                Interlocked.Increment(ref _accepted);
                _ = Task.Run(async () =>
                {
                    try { tcs.TrySetResult(await SecureChannel.PairAsClientOnAsync(tcp, secret, ct)); }
                    catch (Exception ex) when (!ct.IsCancellationRequested) { Log('✗', $"collegamento in arrivo non valido: {Why(ex)}"); }
                    catch { }
                    finally { tcp.Dispose(); }
                });
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
            SyncActiveEntry();
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
                var busyWait = _busyUntil - DateTime.UtcNow;   // un altro operatore usa il server
                if (busyWait > TimeSpan.Zero) { try { await Task.Delay(busyWait, ct); } catch { break; } }
                if (_master == null)
                {
                    SetStatus("In attesa dell'abbinamento", false);
                    try { await Task.Delay(1000, ct); } catch { break; }
                    continue;
                }

                // una volta per avvio: da quali porte esce la rete del client? Se è filtrata lo dice subito al server
                if (_outbound == null)
                {
                    _outbound = await OutboundProbe.RunAsync(SessionLog.Write, ct);
                    if (_outbound.Known && _outbound.Restricted && _rdv != null)
                    {
                        var myIp = await PortManager.GetPublicIpAsync(ct);
                        var early = new PortOffer(0, myIp?.ToString() ?? "", 0, 0, 0, DateTime.UtcNow.Ticks)
                        { Restricted = true, OutboundPorts = _outbound.Allowed };
                        bool sentEarly = await _rdv.PublishOfferAsync(early, ct);
                        SessionLog.Write($"PORTE comunico al server sul broker MQTT le porte che escono da questa rete: {string.Join(", ", _outbound.Allowed)} {(sentEarly ? "(consegnato)" : "(nessun broker raggiungibile)")}");
                        SetStatus("Rete filtrata: chiedo al server porte " + string.Join(", ", _outbound.Allowed), false);
                        try { await Task.Delay(TimeSpan.FromSeconds(25), ct); } catch { break; }   // tempo al server per aprirle
                    }
                }

                ServerAddress? target;
                if (_fallbackAddr != null) { target = _fallbackAddr; _fallbackAddr = null; }
                else target = LastServer;

                SecureChannel? ctl = null;
                // 0.3.7: prima IPv6 diretto, se il server lo ha pubblicato e questo PC ha IPv6 (passa anche il CGNAT)
                ctl = await TryV6Async(ct);
                if (ctl != null) target = _v6Target;
                if (ctl == null && target != null) ctl = await TryConnectAsync(target.Value, ChannelKind.Control, ct);

                if (ctl == null && _rdv != null)
                {
                    SetStatus("Cerco il server…", false);
                    var found = await _rdv.LookupAsync(ct);
                    Stats.MqttOk = _rdv.LastOkBrokers;
                    if (_rdv.LastLan.Length > 0 && _rdv.LastLan != _s.LastServerLan) { _s.LastServerLan = _rdv.LastLan; SyncActiveEntry(); _s.Save(); }
                    if (ctl == null) { ctl = await TryV6Async(ct); if (ctl != null) target = _v6Target; }
                    if (ctl == null && found != null && !found.Equals(target))
                    {
                        target = found;
                        ctl = await TryConnectAsync(found.Value, ChannelKind.Control, ct);
                    }
                }

                // stessa rete locale: il router spesso non permette di rientrare dal proprio IP pubblico
                if (ctl == null && target != null && IPAddress.TryParse(_s.LastServerLan, out var lanIp) && !lanIp.Equals(target.Value.Ip))
                {
                    var lanTarget = target.Value with { Ip = lanIp };
                    SessionLog.Write($"PORTE provo il server sulla rete locale {lanIp}:{lanTarget.ControlPort}");
                    ctl = await TryConnectAsync(lanTarget, ChannelKind.Control, ct);
                    if (ctl != null) target = lanTarget;
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
                _peerOpus = false; ListenOnly = false; ListenOnlyOperator = "";
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
                    if (addr.Ip.AddressFamily == AddressFamily.InterNetwork) RememberServer(addr);
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
                if (ManualPortsSet && await OpenManualClientPortsAsync(ct)) { }
                else if (_cportsUpnp)
                {
                    var c = await OpenVerifiedPortAsync(_cports, "IP-TRX-CTRL client controllo", ChannelKind.Control, true, ct);
                    var a = c.port > 0 ? await OpenVerifiedPortAsync(_cports, "IP-TRX-CTRL client audio", ChannelKind.Audio, true, ct) : (0, null);
                    _cCtlPort = c.port; _cCtlListener = c.listener;
                    _cAudPort = a.port; _cAudListener = a.listener;
                    if (_cCtlPort == 0 || _cAudPort == 0) await CloseClientPortsAsync();
                }
                else SessionLog.Write("PORTE il router del client non risponde con UPnP: il client non può proporre porte");

                var ip = _cports.ExternalIp ?? await PortManager.GetPublicIpAsync(ct);
                _outbound ??= await OutboundProbe.RunAsync(SessionLog.Write, ct);
                var offer = new PortOffer(round, ip?.ToString() ?? "", _cCtlPort, _cAudPort, triedServerPort, DateTime.UtcNow.Ticks)
                {
                    Restricted = _outbound.Known && _outbound.Restricted,
                    OutboundPorts = _outbound.Allowed
                };
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

        /// <summary>
        /// Client (0.3.4): per il collegamento inverso usa le due porte scelte dall'utente, inoltrate a mano sul router
        /// (con UPnP si chiede anche al router di aprirle). Si propongono al server anche se la verifica non riesce.
        /// </summary>
        private async Task<bool> OpenManualClientPortsAsync(CancellationToken ct)
        {
            var pm = _cports!;
            int cp = _s.ManualControlPort, ap = _s.ManualAudioPort;
            TcpListener cl, al;
            try { cl = new TcpListener(IPAddress.Any, cp); cl.Start(); }
            catch { SessionLog.Write($"PORTE porta scelta {cp}: già usata da un altro programma di questo PC"); return false; }
            try { al = new TcpListener(IPAddress.Any, ap); al.Start(); }
            catch { try { cl.Stop(); } catch { } SessionLog.Write($"PORTE porta scelta {ap}: già usata da un altro programma di questo PC"); return false; }
            bool mapped = false;
            if (pm.UpnpAvailable)
            {
                mapped = await pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, cp, cp, "IP-TRX-CTRL client controllo");
                if (await pm.OpenFixedAsync(Open.Nat.Protocol.Tcp, ap, ap, "IP-TRX-CTRL client audio"))
                    await pm.OpenFixedAsync(Open.Nat.Protocol.Udp, ap, ap, "IP-TRX-CTRL client audio (UDP)");
            }
            _ = Task.Run(() => ClientAcceptLoopAsync(cl, ChannelKind.Control, ct));
            _ = Task.Run(() => ClientAcceptLoopAsync(al, ChannelKind.Audio, ct));
            _cCtlPort = cp; _cCtlListener = cl;
            _cAudPort = ap; _cAudListener = al;
            bool? r = await ProbeOwnAsync(cp, SessionLog.Write, ct);
            SessionLog.Write($"PORTE il client usa le porte scelte: controllo TCP {cp}, audio TCP e UDP {ap}" + (mapped ? " (aperte anche con UPnP)" : " (inoltro manuale sul router)")
                             + $" · verifica da internet: {Esito(r)}");
            return true;
        }

        /// <summary>Client: il server si collega alle porte del client; l'handshake lo avvia comunque il client.</summary>
        private async Task ClientAcceptLoopAsync(TcpListener listener, ChannelKind kind, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await listener.AcceptTcpClientAsync(ct); }
                catch { break; }
                Interlocked.Increment(ref _accepted);
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

        private async Task SendHelloAsync(IMsgChannel ch)
        {
            try { await ch.SendAsync(new MsgWriter(Msg.Hello).Str(_s.Callsign).Str(Stats.LocalNet).Str(AppVersion.Tag).Build()); } catch { }
        }

        private bool IsServer => _s.Role == NodeRole.Server;

        private async Task ReceiveLoopAsync(IMsgChannel ch, CancellationToken ct)
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
                        case Msg.AudioOpus: { uint sq = r.U32(); OnAudioOpus(sq, r.Rest()); } break;
                        case Msg.Hello:
                            {
                                _s.PeerCallsign = r.Str();
                                if (IsServer && _s.Clients.FirstOrDefault(x => x.Id == _sessionClientId) is { } ce && ce.Callsign != _s.PeerCallsign)
                                {
                                    if (ce.Name.Length == 0 || ce.Name.StartsWith("Client ", StringComparison.Ordinal) || ce.Name == ce.Callsign) ce.Name = _s.PeerCallsign;
                                    ce.Callsign = _s.PeerCallsign;
                                    _s.Save();
                                    ClientsChanged?.Invoke();
                                }
                                if (!IsServer && ActiveServer is { } se && se.Callsign != _s.PeerCallsign)
                                {
                                    // il nominativo del server dà anche il nome al posto in elenco, se non è stato rinominato
                                    if (se.Name.Length == 0 || se.Name.StartsWith("Server ", StringComparison.Ordinal) || se.Name == se.Callsign) se.Name = _s.PeerCallsign;
                                    se.Callsign = _s.PeerCallsign;
                                    _s.Save();
                                    ServersChanged?.Invoke();
                                }
                                Stats.PeerNet = r.AtEnd ? "" : r.Str();
                                var (pp, pv) = AppVersion.Parse(r.AtEnd ? "" : r.Str());
                                Stats.PeerVersion = AppVersion.Show(pv);
                                _peerOpus = AppVersion.HasOpus(pv) && Stats.PeerNet != "browser" && ch is not WebChannel;
                                if (_peerOpus) SessionLog.Write("AUDIO Opus con l'altro PC (16 kbit/s invece di 128)");
                                string note = AppVersion.Notes(pp, pv, !IsServer);
                                SessionLog.Write($"VERSIONE questo PC {AppVersion.Text} · altro PC {Stats.PeerVersion} (protocollo {pp})" + (note.Length > 0 ? " · " + note : " · compatibili"));
                                if (note.Length > 0) VersionWarning?.Invoke(note, AppVersion.Compatible(pp));
                                _s.Save(); StatsChanged?.Invoke();
                            }
                            break;
                        case Msg.Busy:
                            if (!IsServer)
                            {
                                string who = r.Str();
                                _busyUntil = DateTime.UtcNow.AddSeconds(30);
                                SessionLog.Write($"SESSIONE il server è occupato: è collegato {who}");
                                Notice?.Invoke($"Il server è occupato: è collegato {who}. Riprovo da solo tra 30 secondi.");
                            }
                            break;
                        case Msg.Note:
                            if (IsServer) PostNote(OperatorName(), r.Str());
                            else { string who = r.Str(), text = r.Str(); long ms = r.AtEnd ? 0 : r.I64(); AddNote(who, text, ms); }
                            break;
                        case Msg.ListenOnly:
                            if (!IsServer)
                            {
                                ListenOnly = true;
                                ListenOnlyOperator = r.Str();
                                SessionLog.Write($"ASCOLTO questo PC è in solo ascolto: trasmette {ListenOnlyOperator}");
                                StatsChanged?.Invoke();
                            }
                            break;
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
                                if (tel.TxBlocked && !LastTelemetry.TxBlocked)
                                {
                                    // il server ha bloccato la trasmissione per ROS alto: rilascia anche qui PTT e keyer
                                    Audio.Keyer.Stop();
                                    SetPttLocal(false);
                                    SessionLog.Write($"ROS {tel.TxBlockedSwr.ToString("F1", CultureInfo.InvariantCulture)}: il server ha bloccato la trasmissione");
                                }
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
                        case Msg.ServerNotice:
                            if (!IsServer) Notice?.Invoke(r.Str());
                            break;
                        case Msg.SetRadioCtl:
                            if (IsServer) { string name = r.Str(); float v = r.F32(); _ = Rig.SetRadioCtlAsync(name, v); }
                            break;
                        case Msg.Memories:
                            if (!IsServer)
                            {
                                int n = Math.Clamp(r.I32(), 0, 1000);
                                var list = new List<MemoryEntry>(n);
                                for (int i = 0; i < n; i++) list.Add(new MemoryEntry { Id = r.Str(), Name = r.Str(), FreqHz = r.I64(), Mode = r.Str() });
                                ServerMemoriesSeen = list;
                                MemoriesChanged?.Invoke();
                            }
                            break;
                        case Msg.MemorySave:
                            if (IsServer)
                            {
                                string name = r.Str(); long hz = r.I64(); string mode = r.Str();
                                if (SessionCanTransmit(out string why)) SaveServerMemory(name, hz, mode, ClientName(_sessionClientId));
                                else SendNotice("Le memorie del server le cambia solo un client che può trasmettere (" + why + ").");
                            }
                            break;
                        case Msg.MemoryDelete:
                            if (IsServer)
                            {
                                string id = r.Str();
                                if (SessionCanTransmit(out string why)) DeleteServerMemory(id, ClientName(_sessionClientId));
                                else SendNotice("Le memorie del server le cambia solo un client che può trasmettere (" + why + ").");
                            }
                            break;
                        case Msg.SpectrumOn:
                            if (IsServer) { _spectrumWanted = r.Bool(); SessionLog.Write("PANADAPTER " + (_spectrumWanted ? "chiesto" : "non più chiesto") + " dal client"); }
                            break;
                        case Msg.Spectrum:
                            if (!IsServer) SpectrumReceived?.Invoke(SpectrumLine.Decode(r));
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
                                if (!SessionCanTransmit(out string why)) { SendNotice("CW non trasmesso: " + why + "."); break; }
                                SessionLog.Write($"CW dal client ({wpm} WPM): {text}");
                                _ = Rig.SendCwAsync(text, wpm);
                            }
                            break;
                        case Msg.StopMorse: if (IsServer) _ = Rig.StopCwAsync(); break;
                        case Msg.CwKey:
                            if (IsServer)
                            {
                                bool down = r.Bool(); long ms = r.I64();
                                if (down && !SessionCanTransmit(out string why)) { NotifyRefusedOnce(why); break; }
                                Rig.CwKeyEvent(down, ms);
                            }
                            break;
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
                        case Msg.SwrUnlock:
                            if (IsServer)
                            {
                                if (ClientMayTransmit()) Rig.UnlockTx("dal client");
                                else SendNotice("Un client in solo ascolto non può sbloccare la trasmissione.");
                            }
                            break;
                        case Msg.SetSwrProtection:
                            if (IsServer)
                            {
                                bool on = r.Bool(); float warn = r.F32(); float trip = r.F32();
                                if (ClientMayTransmit()) ApplySwrProtection(on, warn, trip, "dal client");
                                else SendNotice("Un client in solo ascolto non può cambiare la protezione ROS.");
                            }
                            break;
                        case Msg.SetAutoUnmute:
                            if (IsServer) { _s.AutoUnmute = r.Bool(); _s.Save(); _ = SendDeviceStateAsync(); }
                            break;
                        case Msg.Ptt:
                            if (IsServer)
                            {
                                bool wasOn = _remotePtt;
                                _remotePtt = r.U8() == 1;
                                _lastRemotePtt = DateTime.UtcNow;
                                if (!_remotePtt) _remotePttRefusedNotified = false;
                                else if (!wasOn && !SessionCanTransmit(out string why)) NotifyRefusedOnce(why);
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
                            if (IsServer && _sessionClientId == WebClientId) { SendNotice("Dalla pagina web non si scambiano i ruoli."); break; }
                            if (IsServer && !ClientMayTransmit()) { SendNotice("Un client in solo ascolto non può chiedere lo scambio dei ruoli."); break; }
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
                                if (IsServer) await _tunnelSrv.DataAsync(id, d);   // i comandi di PTT grezzi vengono scartati dal tunnel
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
                    if (IsServer) CloseListeners("l'operatore si è scollegato: il primo che si ricollega trasmette");
                }
                else if (ReferenceEquals(ch, _audio))
                {
                    Interlocked.CompareExchange(ref _audio, null, ch);
                    ch.Dispose();
                }
                else ch.Dispose();
            }
        }

        // =================================================================== ascoltatori (0.3.6)

        /// <summary>Client in solo ascolto: riceve audio e stato della radio, i suoi comandi non arrivano alla radio.</summary>
        private sealed class Listener
        {
            public string Cid = "";
            public IMsgChannel Ctl = null!;
            public IMsgChannel? Aud;
            public string Call = "";
            public bool Opus;
        }

        private readonly List<Listener> _listeners = new();
        private uint _lsnSeq;
        public const int MaxListeners = 8;

        public int ListenerCount { get { lock (_listeners) return _listeners.Count; } }
        private List<Listener> ListenerSnapshot() { lock (_listeners) return _listeners.ToList(); }
        private bool CanAddListener() => IsServer && _s.AllowListeners && ListenerCount < MaxListeners;
        private string OperatorName() => _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : ClientName(_sessionClientId);

        // ---- 0.3.7: note tra chi opera e chi ascolta (le note di PiRO, la chat delle stanze di rustyrig)
        public sealed record NoteItem(DateTime Utc, string From, string Text);
        private readonly List<NoteItem> _notes = new();
        public event Action<NoteItem>? NoteAdded;
        public List<NoteItem> Notes { get { lock (_notes) return _notes.ToList(); } }

        private void AddNote(string from, string text, long ms)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 300) text = text[..300];
            var n = new NoteItem(ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : DateTime.UtcNow, from, text);
            lock (_notes)
            {
                if (_notes.Any(x => x.Utc == n.Utc && x.From == n.From && x.Text == n.Text)) return;   // già ricevuta (ricollegamento)
                _notes.Add(n);
                _notes.RemoveAll(x => (DateTime.UtcNow - x.Utc).TotalHours > 1);
                while (_notes.Count > 30) _notes.RemoveAt(0);
            }
            NoteAdded?.Invoke(n);
        }

        /// <summary>Server: tiene la nota e la manda all'operatore, a ogni ascoltatore e alle pagine web.</summary>
        private void PostNote(string from, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 300) text = text[..300];
            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AddNote(from, text, ms);
            SessionLog.Write($"NOTA {from}: {text}");
            byte[] msg = new MsgWriter(Msg.Note).Str(from).Str(text).I64(ms).Build();
            Send(msg);
            foreach (var l in ListenerSnapshot()) _ = l.Ctl.SendAsync(msg).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Scrive una nota: sul server la diffonde, sul client la manda al server (che la rimanda a tutti).</summary>
        public void SendNote(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (IsServer) PostNote(_s.Callsign.Length > 0 ? _s.Callsign : "server", text);
            else Send(new MsgWriter(Msg.Note).Str(text).Build());
        }

        private async Task SendRecentNotesAsync(IMsgChannel ch)
        {
            foreach (var n in Notes)
            {
                try { await ch.SendAsync(new MsgWriter(Msg.Note).Str(n.From).Str(n.Text).I64(new DateTimeOffset(n.Utc).ToUnixTimeMilliseconds()).Build()); }
                catch { return; }
            }
        }

        private void UpdateListenerStats()
        {
            var names = ListenerSnapshot().Select(l => l.Call.Length > 0 ? l.Call : ClientName(l.Cid)).ToList();
            Stats.Listeners = names.Count == 0 ? "" : string.Join(", ", names);
            StatsChanged?.Invoke();
        }

        /// <summary>Server: un altro client (o una pagina web) mentre c'è già l'operatore: entra in solo ascolto.</summary>
        private async Task ServeListenerAsync(IMsgChannel ch, string cid, CancellationToken ct, string webName = "")
        {
            var l = new Listener { Cid = cid, Ctl = ch, Call = webName };
            if (ch is WebChannel) l.Aud = ch;   // la pagina web usa un solo WebSocket per tutto
            Listener? old;
            lock (_listeners)
            {
                old = _listeners.FirstOrDefault(x => x.Cid == cid);
                if (old != null) _listeners.Remove(old);
                _listeners.Add(l);
            }
            if (old != null) { try { old.Ctl.Dispose(); } catch { } }
            string op = OperatorName();
            SessionLog.Write($"ASCOLTO entra {(webName.Length > 0 ? webName + " (pagina web)" : ClientName(cid))} da {ch.Remote}: trasmette {op}");
            UpdateListenerStats();
            try
            {
                await SendHelloAsync(ch);
                await ch.SendAsync(new MsgWriter(Msg.ListenOnly).Str(op).Build(), ct);
                await ch.SendAsync(new MsgWriter(Msg.ServerNotice).Str($"Sei in solo ascolto: trasmette {op}. Quando si scollega, il primo che si ricollega prende la radio.").Build(), ct);
                await SendRecentNotesAsync(ch);
                OnRigUpdated();
                while (!ct.IsCancellationRequested)
                {
                    byte[]? data = await ch.ReceiveAsync(ct);
                    if (data == null || data.Length == 0) break;
                    var r = new MsgReader(data);
                    switch (r.Type)
                    {
                        case Msg.Hello:
                            {
                                string call = r.Str();
                                string net = r.AtEnd ? "" : r.Str();
                                var (_, pv) = AppVersion.Parse(r.AtEnd ? "" : r.Str());
                                if (call.Length > 0) l.Call = call;
                                l.Opus = AppVersion.HasOpus(pv) && net != "browser" && ch is not WebChannel;
                                UpdateListenerStats();
                            }
                            break;
                        case Msg.Ping:
                            await ch.SendAsync(new MsgWriter(Msg.Pong).I64(r.I64()).Build(), ct); break;
                        case Msg.Note:
                            PostNote(l.Call.Length > 0 ? l.Call : ClientName(l.Cid), r.Str()); break;
                        default: break;   // comandi, PTT e audio di chi ascolta non arrivano alla radio
                    }
                }
            }
            catch { }
            finally
            {
                lock (_listeners) _listeners.Remove(l);
                try { ch.Dispose(); } catch { }
                if (l.Aud != null && !ReferenceEquals(l.Aud, ch)) { try { l.Aud.Dispose(); } catch { } }
                SessionLog.Write($"ASCOLTO esce {(l.Call.Length > 0 ? l.Call : ClientName(cid))}");
                UpdateListenerStats();
            }
        }

        private void CloseListeners(string why)
        {
            List<Listener> all;
            lock (_listeners) { all = _listeners.ToList(); _listeners.Clear(); }
            if (all.Count == 0) return;
            SessionLog.Write($"ASCOLTO chiudo {all.Count} collegamenti in ascolto: {why}");
            foreach (var l in all)
            {
                try { l.Ctl.Dispose(); } catch { }
                try { l.Aud?.Dispose(); } catch { }
            }
            UpdateListenerStats();
        }

        /// <summary>Server: l'audio della radio anche a chi ascolta (Opus ai programmi 0.3.6, PCM agli altri e al browser).</summary>
        private void SendToListeners(byte[] pcm, int len, ref byte[]? opus)
        {
            if (ListenerCount == 0) return;
            uint seq = ++_lsnSeq;
            byte[]? pcmMsg = null, opusMsg = null;
            foreach (var l in ListenerSnapshot())
            {
                var a = l.Aud;
                if (a == null) continue;
                byte[] msg;
                if (l.Opus)
                {
                    opus ??= _opusTx.Encode(pcm, len);
                    msg = opusMsg ??= new MsgWriter(Msg.AudioOpus).U32(seq).Bytes(opus).Build();
                }
                else msg = pcmMsg ??= new MsgWriter(Msg.Audio).U32(seq).Bytes(pcm, 0, len).Build();
                _ = a.SendAsync(msg).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        // ---- client: questo PC è in solo ascolto
        public bool ListenOnly { get; private set; }
        public string ListenOnlyOperator { get; private set; } = "";

        private void OnAudioPacket(MsgReader r)
        {
            uint seq = r.U32();
            OnAudio(seq, r.Rest());
        }

        // ---- audio Opus (0.3.6): un codificatore per l'audio che parte, un decodificatore per quello che arriva
        private readonly OpusTx _opusTx = new();
        private readonly OpusRx _opusRx = new();
        private volatile bool _peerOpus;   // l'altro programma è 0.3.6 o successivo (non la pagina web)

        private void OnAudioOpus(uint seq, byte[] packet)
        {
            foreach (var (s, pcm) in _opusRx.Decode(seq, packet))
                if (pcm.Length > 0) OnAudio(s, pcm);
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
            if (IsServer && Rig.TxBlocked) return;   // ROS alto: niente audio verso la radio (anche col PTT a VOX)
            if (IsServer && !Stats.PeerTx) return;   // il microfono remoto arriva alla radio solo durante la SUA trasmissione permessa
            if (IsServer)
            {
                // 0.3.7 (PiRO): gli ascoltatori sentono anche l'operatore che trasmette, utile per i net; non nei modi dati
                if (_s.EchoTxToListeners && ListenerCount > 0 && IsVoiceMode(Rig.Telemetry.Mode)) { byte[]? o = null; SendToListeners(pcm, pcm.Length, ref o); }
            }
            else
            {
                NoteRxLevel(pcm);
                string mode = LastTelemetry.Mode ?? "";
                bool cwMode = mode.StartsWith("CW", StringComparison.OrdinalIgnoreCase);
                // 0.3.7: riduzione del rumore neurale (HamNoise) solo in fonia e CW, mai nei modi dati
                if (Denoiser.Enabled && (cwMode || IsVoiceMode(mode))) Denoiser.Process(pcm, pcm.Length, cwMode);
                if (Squelch.Enabled && IsVoiceMode(mode)) Squelch.Process(pcm, pcm.Length);
            }
            Recorder.Write(pcm, pcm.Length);
            Audio.Playback.Write(pcm, 0, pcm.Length);
        }

        /// <summary>Modi in fonia: lo squelch vocale e l'eco agli ascoltatori valgono solo qui (non CW, dati, RTTY).</summary>
        private static bool IsVoiceMode(string? mode)
        {
            string m = (mode ?? "").ToUpperInvariant();
            return m is "USB" or "LSB" or "AM" or "FM" or "WFM" or "SAM" or "AMS" or "DSB" or "FMN" or "AMN" or "ECSSUSB" or "ECSSLSB";
        }

        // ---- 0.3.7: squelch vocale (PiRO) e registrazione (rustyrig)
        public VoiceSquelch Squelch { get; } = new();
        public NeuralDenoiser Denoiser { get; } = new();
        public Recorder Recorder { get; } = new();

        // ---- 0.3.7: VOX del client (remoteAudio): il PTT parte da solo quando si parla
        private volatile bool _voxKeyed;
        private DateTime _voxLastVoice, _rxLevelAt;
        private double _rxLevelDb = -120;
        public bool VoxKeyed => _voxKeyed;

        private static double LevelDb(byte[] pcm, int len)
        {
            double sum = 0; int n = len / 2;
            for (int i = 0; i < n; i++) { double v = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8); sum += v * v; }
            return n == 0 ? -120 : 10 * Math.Log10(sum / n / (32768.0 * 32768.0) + 1e-12);
        }

        private void NoteRxLevel(byte[] pcm) { _rxLevelDb = LevelDb(pcm, pcm.Length); _rxLevelAt = DateTime.UtcNow; }

        private void VoxStep(byte[] pcm, int len)
        {
            var now = DateTime.UtcNow;
            double db = LevelDb(pcm, len);
            // anti-VOX: un livello molto più basso dell'audio ricevuto appena suonato è l'altoparlante che rientra nel microfono
            bool echo = (now - _rxLevelAt).TotalMilliseconds < 300 && db < _rxLevelDb - 10;
            if (db >= _s.VoxThresholdDb && !echo)
            {
                _voxLastVoice = now;
                if (!_localPtt && !_voxKeyed) { _voxKeyed = true; SetPttLocal(true); }
            }
            else if (_voxKeyed && (now - _voxLastVoice).TotalMilliseconds > Math.Clamp(_s.VoxHoldMs, 100, 5000))
            {
                _voxKeyed = false;
                SetPttLocal(false);
            }
        }

        /// <summary>VOX spento o disattivato: se teneva il PTT lo rilascia.</summary>
        public void VoxRelease()
        {
            if (!_voxKeyed) return;
            _voxKeyed = false;
            SetPttLocal(false);
        }

        private void ResetAudioStats()
        {
            _lastSeq = 0; _expected = 0; _lost = 0; _lastArrival = DateTime.MinValue;
            Stats.JitterMs = 0; Stats.LossPercent = 0;
            Audio.Playback.Reset();
            _opusRx.Reset();
            Squelch.Reset();
            Denoiser.Reset();
        }

        private void OnCaptured(byte[] pcm, int len)
        {
            if (!IsServer)
            {
                if (_s.VoxEnabled && !ListenOnly && _control != null && !_digitalPtt) VoxStep(pcm, len);
                else if (_voxKeyed) VoxRelease();
            }
            // Server: invia sempre l'audio ricevuto dalla radio (non durante la trasmissione). Client: solo con PTT.
            bool send = IsServer ? !Stats.PeerTx : _localPtt && !_digitalPtt;
            if (!send) return;
            Recorder.Write(pcm, len);
            SendAudioFrame(pcm, len);
        }

        /// <summary>Audio verso l'altro PC: su UDP se il percorso è stato verificato dai due lati, altrimenti su TCP.</summary>
        private void SendAudioFrame(byte[] pcm, int len)
        {
            byte[]? opus = null;
            if (IsServer) SendToListeners(pcm, len, ref opus);
            var udp = _udp;
            bool useOpus = _peerOpus;
            if (useOpus) opus ??= _opusTx.Encode(pcm, len);
            if (_udpAudio && udp != null)
            {
                if (useOpus) udp.SendAudio(opus, true); else udp.SendAudio(pcm.AsSpan(0, len));
                return;
            }
            var aud = _audio;
            if (aud == null) return;
            byte[] msg = useOpus ? new MsgWriter(Msg.AudioOpus).U32(++_txSeq).Bytes(opus!).Build()
                                 : new MsgWriter(Msg.Audio).U32(++_txSeq).Bytes(pcm, 0, len).Build();
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
            int localPort = IsServer ? _audLocal : _cAudPort;
            int extPort = IsServer ? _audPort : _cAudPort;
            UdpLink link;
            try { link = new UdpLink(_master, localPort); }
            catch { link = new UdpLink(_master, 0); localPort = 0; }
            link.Log += SessionLog.Write;
            link.AudioReceived += (seq, data, opus) => { _lastUdpAudio = DateTime.UtcNow; if (opus) OnAudioOpus(seq, data); else OnAudio(seq, data); };
            _udp = link;

            var cands = new List<string>();
            var nat = await Stun.DiscoverAsync(link, Stun.DefaultServers, SessionLog.Write, ct);
            if (nat.Public != null) cands.Add(nat.Public.ToString());
            var pm = IsServer ? _ports : _cports;
            if (localPort > 0 && extPort > 0 && pm?.ExternalIp != null) cands.Add(new IPEndPoint(pm.ExternalIp, extPort).ToString());
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
            var t = Rig.Telemetry.Clone();
            t.TxAllowed = SessionCanTransmit(out string why);
            t.TxDenied = why;
            t.PanAvailable = Pan.Available;
            t.PanStatus = Pan.Status;
            LastTelemetry = t;
            TelemetryChanged?.Invoke(t);
            var ctl = _control;
            if (ctl != null && IsServer)
                _ = ctl.SendAsync(t.Encode()).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
            if (IsServer && ListenerCount > 0)
            {
                var tl = t.Clone();
                tl.TxAllowed = false;
                tl.TxDenied = "sei in solo ascolto: trasmette " + OperatorName();
                byte[] enc = tl.Encode();
                foreach (var l in ListenerSnapshot())
                    _ = l.Ctl.SendAsync(enc).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
            }
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
        public void SendMorse(string text, int wpm) { wpm = Math.Clamp(wpm, 5, 60); if (IsServer) _ = Rig.SendCwAsync(text, wpm); else Send(new MsgWriter(Msg.SendMorse).Str(text).I32(wpm).Build()); }
        /// <summary>Tasto CW manuale del client: ogni pressione e rilascio vanno al server con l'istante.</summary>
        public void SendCwKey(bool down, long ms)
        {
            if (IsServer) Rig.CwKeyEvent(down, ms);
            else Send(new MsgWriter(Msg.CwKey).Bool(down).I64(ms).Build());
        }

        public void StopMorse() { if (IsServer) _ = Rig.StopCwAsync(); else Send(new MsgWriter(Msg.StopMorse).Build()); }

        /// <summary>Comandi della radio (NR, NB, ANF, COMP: 0/1 · SQL: 0..1 · ATT, PREAMP, AGC: -1 = passo successivo).</summary>
        public void SetRadioCtl(string name, float value)
        {
            if (IsServer) _ = Rig.SetRadioCtlAsync(name, value);
            else Send(new MsgWriter(Msg.SetRadioCtl).Str(name).F32(value).Build());
        }

        // =================================================================== permessi di trasmissione (server)

        private bool _remotePttRefusedNotified;

        /// <summary>Il client collegato è abilitato a trasmettere (non è in solo ascolto).</summary>
        private bool ClientMayTransmit() =>
            _s.Clients.FirstOrDefault(c => c.Id == _sessionClientId) is not { CanTransmit: false };

        /// <summary>Il client collegato può trasmettere adesso: non è in solo ascolto e nessuno trasmette dalla radio.</summary>
        private bool SessionCanTransmit(out string why)
        {
            why = "";
            if (!IsServer) return true;
            if (!ClientMayTransmit()) { why = "il server ti ha impostato in solo ascolto"; return false; }
            if (Rig.LocalLockActive) { why = $"c'è un operatore alla radio (trasmissione remota bloccata ancora {Rig.LocalLockSeconds} s)"; return false; }
            return true;
        }

        private void NotifyRefusedOnce(string why)
        {
            if (_remotePttRefusedNotified) return;
            _remotePttRefusedNotified = true;
            SessionLog.Write("TRASMISSIONE remota rifiutata: " + why);
            SendNotice("Trasmissione non permessa: " + why + ".");
        }

        /// <summary>Server: avviso per l'operatore remoto (e anche qui).</summary>
        private void SendNotice(string text)
        {
            if (IsServer) Send(new MsgWriter(Msg.ServerNotice).Str(text).Build());
        }

        private void OnRigNotice(string text)
        {
            Notice?.Invoke(text);
            SendNotice(text);
        }

        /// <summary>Server: client in solo ascolto o abilitato a trasmettere.</summary>
        public void SetClientCanTransmit(string id, bool can)
        {
            var e = _s.Clients.FirstOrDefault(x => x.Id == id);
            if (e == null || e.CanTransmit == can) return;
            e.CanTransmit = can;
            _s.Save();
            SessionLog.Write($"CLIENT {e.Name}: " + (can ? "può trasmettere" : "solo ascolto"));
            ClientsChanged?.Invoke();
            if (!can && id == _sessionClientId && _remotePtt) { _remotePtt = false; _ = ApplyPttAsync(); }
            Rig.PushTelemetry();
        }

        public void ClearLocalLock() => Rig.ClearLocalLock("dal server");

        // =================================================================== accesso dal browser (dalla 0.3.1)

        public const string WebClientId = "web";
        public WebServer? Web { get; private set; }
        private volatile byte[]? _webSecret;
        private CancellationTokenSource? _webCts;
        public int WebBrokersOk { get; private set; }

        /// <summary>
        /// Server: pagina web HTTPS per il browser. Si entra con il codice fisso del server (creato qui se manca);
        /// gli indirizzi del server vanno cifrati sui broker MQTT per la pagina «Trova il server».
        /// </summary>
        public void RestartWeb()
        {
            _webCts?.Cancel();
            _webCts = null;
            Web?.Stop();
            _webSecret = null;
            if (!IsServer || !_s.WebEnabled || _serverCts == null) return;
            if (_fixedDigits.Length != Crypto.FixedCodeDigits) NewFixedCode();
            Web ??= new WebServer(_s, ServeWebAsync, () => _webSecret is { } k ? WebRendezvous.LoginKey(k) : null, () => AppVersion.Tag);
            int port = Math.Clamp(_s.WebPort, 1024, 65535);
            if (!Web.Start(port)) return;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_serverCts.Token);
            _webCts = cts;
            string digits = _fixedDigits;
            _ = Task.Run(async () =>
            {
                try
                {
                    var secret = await Task.Run(() => Crypto.PairingSecretFromFixedCode(digits), cts.Token);
                    _webSecret = secret;
                    for (int i = 0; i < 120 && _ports == null && !cts.IsCancellationRequested; i++) await Task.Delay(1000, cts.Token);
                    if (_s.WebOpenRouter && _ports != null)
                    {
                        bool ok = false;
                        try { ok = await _ports.OpenFixedAsync(Open.Nat.Protocol.Tcp, port, port, "IP-TRX-CTRL pagina web"); } catch { }
                        SessionLog.Write(ok ? $"WEB porta {port} aperta sul router (UPnP)" : $"WEB porta {port} NON aperta sul router");
                    }
                    while (!cts.IsCancellationRequested)
                    {
                        await PublishWebAsync(secret, port, cts.Token);
                        await Task.Delay(TimeSpan.FromMinutes(10), cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { SessionLog.Write("WEB pubblicazione degli indirizzi interrotta: " + ex.Message); }
            });
        }

        private async Task PublishWebAsync(byte[] secret, int port, CancellationToken ct)
        {
            var w = Web;
            if (w == null) return;
            string pub = _s.WebOpenRouter && _ports?.ExternalIp != null ? _ports.ExternalIp.ToString() : "";
            var vpn = WebServer.VpnAddresses();
            string lan = WebServer.LanAddress();
            var info = new
            {
                c = _s.Callsign, v = AppVersion.Text, p = port, vpn, lan, pub,
                cgnat = _ports?.CgnatSuspected == true, fp = w.Fingerprint,
                t = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            WebBrokersOk = await WebRendezvous.PublishAsync(secret, info, _s.MqttBrokers, ct);
            SessionLog.Write($"WEB indirizzi per la pagina «Trova il server» pubblicati cifrati su {WebBrokersOk} broker MQTT"
                + $" · VPN {(vpn.Count > 0 ? string.Join(", ", vpn) : "nessuna")} · rete locale {(lan.Length > 0 ? lan : "—")}");
        }

        /// <summary>Riga di stato per le Impostazioni.</summary>
        public string WebInfo()
        {
            if (!_s.WebEnabled) return "Spento.";
            var w = Web;
            if (w == null || !w.Running) return w?.Status ?? "In avvio…";
            var vpn = WebServer.VpnAddresses();
            string lan = WebServer.LanAddress();
            return $"Attivo sulla porta {w.Port}. Indirizzi: "
                 + (vpn.Count > 0 ? $"VPN {string.Join(", ", vpn)} · " : "")
                 + (lan.Length > 0 ? $"rete locale {lan}" : "rete locale —")
                 + $". Pubblicati su {WebBrokersOk} broker. Impronta del certificato: {w.Fingerprint}";
        }

        /// <summary>Il browser compare nell'elenco dei client: lì si sceglie se può trasmettere.</summary>
        private ClientEntry EnsureWebClient(string name)
        {
            var e = _s.Clients.FirstOrDefault(x => x.Id == WebClientId);
            if (e == null)
            {
                e = new ClientEntry { Id = WebClientId, Name = "Browser", How = "pagina web", PairedAt = DateTime.Now, CanTransmit = !_s.WebRxOnly };
                _s.Clients.Add(e);
                _s.Save();
                ClientsChanged?.Invoke();
            }
            if (name.Length > 0 && e.Callsign != name) { e.Callsign = name; _s.Save(); ClientsChanged?.Invoke(); }
            return e;
        }

        /// <summary>Server: sessione dalla pagina web. Il WebSocket fa da canale di controllo e da canale audio.</summary>
        private async Task ServeWebAsync(IMsgChannel ch, string name, CancellationToken ct)
        {
            var e = EnsureWebClient(name);
            if (_control != null && _sessionClientId.Length > 0 && CanAddListener())
            {
                await ServeListenerAsync(ch, WebClientId + ":" + name + ":" + ch.Remote, ct, name);
                return;
            }
            if (_control != null && _sessionClientId.Length > 0 && _sessionClientId != WebClientId)
            {
                string who = _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : ClientName(_sessionClientId);
                SessionLog.Write($"SESSIONE rifiutata alla pagina web ({name}): è collegato {who}");
                try { await ch.SendAsync(new MsgWriter(Msg.Busy).Str(who).Build(), ct); await Task.Delay(500, ct); } catch { }
                ch.Dispose();
                return;
            }
            StopUdp();
            _peerOpus = false;   // la pagina web usa il PCM
            _master = null;
            _sessionClientId = WebClientId;
            e.LastUsed = DateTime.Now;
            _s.Save();
            Interlocked.Exchange(ref _control, ch)?.Dispose();
            var oldAud = Interlocked.Exchange(ref _audio, ch);
            if (oldAud != null && !ReferenceEquals(oldAud, ch)) oldAud.Dispose();
            ResetAudioStats();
            OnConnected();
            SessionLog.Write($"SESSIONE dalla pagina web ({name}) da {ch.Remote.Address}");
            await SendHelloAsync(ch);
            try { await ch.SendAsync(new MsgWriter(Msg.PeerInfo).Str(ch.Remote.Address.ToString()).Build(), ct); } catch { }
            Stats.ClientEndpoint = ch.Remote.Address + " (browser)";
            _ = SendDeviceStateAsync();
            SendServerMemories();
            _ = SendRecentNotesAsync(ch);
            _spectrumWanted = false;
            _remotePttRefusedNotified = false;
            await ReceiveLoopAsync(ch, ct);
        }

        /// <summary>Pagina «Trova il server» da salvare su file e aprire sul telefono o sul PC.</summary>
        public static byte[]? FinderPage() => WebServer.ReadWebFile("trova-server.html");

        // =================================================================== memorie

        /// <summary>Client: memorie del server collegato (comuni a tutti i client).</summary>
        public List<MemoryEntry> ServerMemoriesSeen { get; private set; } = new();
        public event Action? MemoriesChanged;

        /// <summary>Memorie del server: sul server quelle salvate qui, sul client quelle ricevute.</summary>
        public IReadOnlyList<MemoryEntry> ServerMemories => IsServer ? _s.ServerMemories : ServerMemoriesSeen;

        private void SendServerMemories()
        {
            if (!IsServer) return;
            var w = new MsgWriter(Msg.Memories).I32(_s.ServerMemories.Count);
            foreach (var m in _s.ServerMemories) w.Str(m.Id).Str(m.Name).I64(m.FreqHz).Str(m.Mode);
            Send(w.Build());
        }

        public void SaveServerMemoryFromUi(string name, long hz, string mode)
        {
            if (IsServer) SaveServerMemory(name, hz, mode, "server");
            else Send(new MsgWriter(Msg.MemorySave).Str(name).I64(hz).Str(mode).Build());
        }

        public void DeleteServerMemoryFromUi(string id)
        {
            if (IsServer) DeleteServerMemory(id, "server");
            else Send(new MsgWriter(Msg.MemoryDelete).Str(id).Build());
        }

        private void SaveServerMemory(string name, long hz, string mode, string who)
        {
            if (hz <= 0) return;
            name = string.IsNullOrWhiteSpace(name) ? $"{hz / 1000.0:0.0} kHz" : name.Trim();
            var e = _s.ServerMemories.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (e == null) { e = new MemoryEntry { Id = Guid.NewGuid().ToString("N")[..8], Name = name }; _s.ServerMemories.Add(e); }
            e.FreqHz = hz; e.Mode = mode ?? "";
            _s.ServerMemories.Sort((a, b) => a.FreqHz.CompareTo(b.FreqHz));
            _s.Save();
            SessionLog.Write($"MEMORIA del server salvata ({who}): {name} {hz} Hz {mode}");
            SendServerMemories();
            MemoriesChanged?.Invoke();
        }

        private void DeleteServerMemory(string id, string who)
        {
            int n = _s.ServerMemories.RemoveAll(m => m.Id == id);
            if (n == 0) return;
            _s.Save();
            SessionLog.Write($"MEMORIA del server cancellata ({who}): {id}");
            SendServerMemories();
            MemoriesChanged?.Invoke();
        }

        // =================================================================== panadapter

        public Panadapter Pan { get; }
        private volatile bool _spectrumWanted;
        private bool _spectrumAsked;
        /// <summary>Riga del panadapter (sul server dalla radio, sul client ricevuta dal server).</summary>
        public event Action<SpectrumLine>? SpectrumReceived;

        private void OnPanLine(SpectrumLine l)
        {
            SpectrumReceived?.Invoke(l);   // anche la finestra del server lo mostra
            if (_spectrumWanted) Send(l.Encode());
        }

        /// <summary>Client: chiede (o non chiede più) il panadapter al server.</summary>
        public void SetSpectrumWanted(bool on)
        {
            if (IsServer) return;
            if (_spectrumAsked == on && _control != null) return;
            _spectrumAsked = on;
            Send(new MsgWriter(Msg.SpectrumOn).Bool(on).Build());
        }

        /// <summary>Server: riavvia il panadapter dopo un cambio delle impostazioni.</summary>
        public void RestartPanadapter()
        {
            if (!IsServer) return;
            Pan.Start();
            Rig.PushTelemetry();
        }

        public void SetRemoteDeviceMute(bool input, bool mute) => Send(new MsgWriter(Msg.SetDeviceMute).U8((byte)(input ? 0 : 1)).Bool(mute).Build());
        public void SetRemoteDeviceVolume(bool input, float v) => Send(new MsgWriter(Msg.SetDeviceVolume).U8((byte)(input ? 0 : 1)).F32(v).Build());
        public void SetRemoteAutoUnmute(bool on) => Send(new MsgWriter(Msg.SetAutoUnmute).Bool(on).Build());

        // ---- protezione ROS: decide il server; dal client si sblocca e si cambiano le impostazioni a distanza

        /// <summary>Sblocca la trasmissione dopo un blocco per ROS alto.</summary>
        public void UnlockSwr()
        {
            if (IsServer) Rig.UnlockTx("dal server");
            else Send(new MsgWriter(Msg.SwrUnlock).Build());
        }

        /// <summary>Attiva o disattiva la protezione ROS e ne imposta le soglie (sul server, anche dal client).</summary>
        public void SetSwrProtection(bool on, double warn, double trip)
        {
            if (IsServer) ApplySwrProtection(on, (float)warn, (float)trip, "dal server");
            else Send(new MsgWriter(Msg.SetSwrProtection).Bool(on).F32((float)warn).F32((float)trip).Build());
        }

        private void ApplySwrProtection(bool on, float warn, float trip, string who)
        {
            var (w, t) = SwrLimits(warn, trip);
            bool changed = on != _s.SwrProtection || Math.Abs(w - _s.SwrWarn) > 0.001 || Math.Abs(t - _s.SwrTrip) > 0.001;
            _s.SwrProtection = on; _s.SwrWarn = w; _s.SwrTrip = t;
            _s.Save();
            if (changed)
                SessionLog.Write($"ROS protezione {(on ? "attiva" : "DISATTIVATA")} {who}: avviso da {w.ToString("F1", CultureInfo.InvariantCulture)}, blocco da {t.ToString("F1", CultureInfo.InvariantCulture)}");
            if (!on) Rig.UnlockTx("(protezione disattivata " + who + ")");
            Rig.PushTelemetry();
        }

        /// <summary>Soglie ammesse: blocco tra 1,2 e 10; avviso tra 1,1 e il blocco.</summary>
        public static (double warn, double trip) SwrLimits(double warn, double trip)
        {
            if (double.IsNaN(trip)) trip = 2.5;
            if (double.IsNaN(warn)) warn = 1.9;
            trip = Math.Clamp(Math.Round(trip, 1), 1.2, 10);
            warn = Math.Clamp(Math.Round(warn, 1), 1.1, trip - 0.1);
            return (warn, trip);
        }

        public void SetPttLocal(bool on)
        {
            if (_localPtt == on) return;
            _localPtt = on;
            if (on && IsServer && _control != null) Rig.NoteLocalOperator("PTT del programma sul server");
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
            bool remote = _remotePtt && SessionCanTransmit(out _);
            bool on = _localPtt || remote;
            Stats.PeerTx = remote;
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

            if (_master != null) { AddClient(_master, "scambio dei ruoli"); _sessionClientId = KeyIdOf(_master); }   // l'altro PC diventa un client abbinato
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
            if (_master != null) SetMaster(_master);              // l'altro PC entra nell'elenco dei server
            RememberServer(newServer);
            _s.Save();
            SessionLog.Write("RUOLI invertiti: questo PC ora è CLIENT");
            StartClient();
            RoleChanged?.Invoke();
        }

        // =================================================================== varie

        private void SetMaster(byte[] master, string how = "")
        {
            if (_s.Role == NodeRole.Server) { AddClient(master, how); return; }
            _master = master;
            {
                // client: ogni server ha la sua chiave; questo diventa quello in uso
                string id = KeyIdOf(master);
                var e = _s.Servers.FirstOrDefault(x => x.Id == id);
                if (e == null)
                {
                    e = new ServerEntry { Id = id, Name = $"Server {_s.Servers.Count + 1}", PairedAt = DateTime.Now };
                    _s.Servers.Add(e);
                }
                KeyStore.SaveMaster(master, id);
                _s.ActiveServerId = id;
                e.LastUsed = DateTime.Now;
                _s.PeerCallsign = e.Callsign;
                _s.Save();
                SessionLog.Write($"SERVER abbinato: {e.Name} (id {id})");
                ServersChanged?.Invoke();
            }
            _rdv = new Rendezvous(master, _s.MqttBrokers);
        }

        public void ForgetPairing()
        {
            if (_s.Role == NodeRole.Client && ActiveServer is { } active) { DeleteServer(active.Id); return; }
            foreach (var c in _s.Clients.ToList()) RemoveClient(c.Id);
            SessionLog.Write("ABBINAMENTO cancellato dall'utente: tutti i client tolti");
        }

        // =================================================================== client abbinati (server)

        public IReadOnlyList<ClientEntry> Clients => _s.Clients;
        public string SessionClientId => _control != null ? _sessionClientId : "";

        private string ClientName(string id) => _s.Clients.FirstOrDefault(x => x.Id == id)?.Name ?? ("client " + id);

        private void AddClient(byte[] master, string how)
        {
            string id = KeyIdOf(master);
            var e = _s.Clients.FirstOrDefault(x => x.Id == id);
            if (e == null)
            {
                // con il codice fisso chi entra parte in solo ascolto (se l'opzione è attiva): lo abilita il server
                e = new ClientEntry { Id = id, Name = $"Client {_s.Clients.Count + 1}", PairedAt = DateTime.Now, How = how,
                                      CanTransmit = !(how == "codice fisso" && _s.FixedCodeRxOnly) };
                _s.Clients.Add(e);
            }
            KeyStore.SaveMaster(master, "cli-" + id);
            lock (_clientKeys)
            {
                _clientKeys[id] = master;
                _clientRdvs[id] = new Rendezvous(master, _s.MqttBrokers);
            }
            e.LastUsed = DateTime.Now;
            _s.Save();
            SessionLog.Write($"CLIENT abbinato: {e.Name} (id {id}" + (how.Length > 0 ? $", {how}" : "") + ")");
            ClientsChanged?.Invoke();
        }

        public void RenameClient(string id, string name)
        {
            var e = _s.Clients.FirstOrDefault(x => x.Id == id);
            if (e == null || string.IsNullOrWhiteSpace(name)) return;
            e.Name = name.Trim();
            _s.Save();
            ClientsChanged?.Invoke();
        }

        /// <summary>Revoca: il client non può più collegarsi; se è collegato viene scollegato.</summary>
        public void RemoveClient(string id)
        {
            var e = _s.Clients.FirstOrDefault(x => x.Id == id);
            lock (_clientKeys) { _clientKeys.Remove(id); _clientRdvs.Remove(id); }
            KeyStore.DeleteMaster("cli-" + id);
            if (e != null) _s.Clients.Remove(e);
            if (_sessionClientId == id)
            {
                CloseChannels();
                _sessionClientId = "";
                _master = null;
            }
            _s.Save();
            SessionLog.Write($"CLIENT tolto dall'elenco: {e?.Name ?? id} (id {id})");
            ClientsChanged?.Invoke();
        }

        private void LoadServerKeys()
        {
            lock (_clientKeys)
            {
                _clientKeys.Clear();
                _clientRdvs.Clear();
                foreach (var c in _s.Clients)
                {
                    var k = KeyStore.LoadMaster("cli-" + c.Id);
                    if (k == null) continue;
                    _clientKeys[c.Id] = k;
                    _clientRdvs[c.Id] = new Rendezvous(k, _s.MqttBrokers);
                }
            }
            var packed = KeyStore.LoadMaster("fixed-code");
            string d = packed == null ? "" : System.Text.Encoding.ASCII.GetString(packed).TrimEnd('\0');
            _fixedDigits = d.Length == Crypto.FixedCodeDigits && d.All(IpTrxCtrl.Core.Compat.IsAsciiDigit) ? d : "";
            _fixedSecret = null;
        }

        // ---- codice fisso del server

        public string FixedCode => _fixedDigits.Length == Crypto.FixedCodeDigits ? Crypto.FormatFixedCode(_fixedDigits) : "";
        public bool FixedCodeEnabled => _s.FixedCodeEnabled;

        public void SetFixedCodeEnabled(bool on)
        {
            _s.FixedCodeEnabled = on;
            if (on && _fixedDigits.Length == 0) NewFixedCode();
            _s.Save();
            SessionLog.Write(on ? "CODICE FISSO attivo: i nuovi client possono abbinarsi con il codice fisso" : "CODICE FISSO disattivato");
            RestartFixedListener();
        }

        /// <summary>Codice fisso nuovo: quello vecchio non vale più; i client già abbinati restano.</summary>
        public string RegenerateFixedCode()
        {
            NewFixedCode();
            RestartFixedListener();
            RestartWeb();   // anche il browser entra con il codice fisso
            return FixedCode;
        }

        private void NewFixedCode()
        {
            string d = Crypto.MakeFixedCode();
            var packed = new byte[32];
            System.Text.Encoding.ASCII.GetBytes(d).CopyTo(packed, 0);
            KeyStore.SaveMaster(packed, "fixed-code");
            _fixedDigits = d;
            _fixedSecret = null;
            SessionLog.Write("CODICE FISSO nuovo generato (quello precedente non vale più)");
        }

        /// <summary>Server: con il codice fisso attivo resta in ascolto sui broker, anche senza la finestra di abbinamento.</summary>
        private void RestartFixedListener()
        {
            _fixedCts?.Cancel();
            _fixedCts = null;
            if (!IsServer || !_s.FixedCodeEnabled || _fixedDigits.Length != Crypto.FixedCodeDigits || _serverCts == null) { _fixedSecret = null; return; }
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_serverCts.Token);
            _fixedCts = cts;
            string digits = _fixedDigits;
            _ = Task.Run(async () =>
            {
                try
                {
                    var secret = await Task.Run(() => Crypto.PairingSecretFromFixedCode(digits), cts.Token);
                    if (cts.IsCancellationRequested) return;
                    _fixedSecret = secret;
                    for (int i = 0; i < 180 && CurrentServerAddress == null && !cts.IsCancellationRequested; i++) await Task.Delay(1000, cts.Token);
                    SessionLog.Write("CODICE FISSO in ascolto sui broker MQTT per nuovi client");
                    await PairingRendezvous.ServerListenAsync(secret, PairInfo, o => OnPairOfferAsync(o, secret, cts.Token),
                        _s.MqttBrokers, (c, t) => SessionLog.Write($"CODICE FISSO {c} {t}"), cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { SessionLog.Write("CODICE FISSO ascolto interrotto: " + ex.Message); }
            });
        }

        // =================================================================== server abbinati (client)

        public IReadOnlyList<ServerEntry> Servers => _s.Servers;
        public ServerEntry? ActiveServer => _s.Servers.FirstOrDefault(x => x.Id == _s.ActiveServerId);

        /// <summary>Identificativo dell'abbinamento ricavato dalla chiave (non la rivela).</summary>
        private static string KeyIdOf(byte[] master) =>
            Convert.ToHexString(Crypto.Hkdf(master, Array.Empty<byte>(), "iptrx-server-id", 6)).ToLowerInvariant();

        /// <summary>Chiave giusta per il ruolo: il server ha una chiave sola, il client quella del server in uso.</summary>
        private void LoadKeyForRole()
        {
            MigrateLegacyKey();
            if (_s.Role == NodeRole.Client)
            {
                var e = ActiveServer ?? _s.Servers.OrderByDescending(x => x.LastUsed).FirstOrDefault();
                if (e != null && e.Id != _s.ActiveServerId) { _s.ActiveServerId = e.Id; _s.Save(); }
                _master = e != null ? KeyStore.LoadMaster(e.Id) : null;
                if (e != null) ApplyEntry(e);
            }
            else { LoadServerKeys(); _master = null; }
            _rdv = _master != null && _s.Role == NodeRole.Client ? new Rendezvous(_master, _s.MqttBrokers) : null;
        }

        /// <summary>
        /// Fino alla 0.2.x c'era una chiave sola: sul client diventa il primo server dell'elenco, sul server il
        /// primo client. Si fa una volta; dopo la copia la chiave unica si cancella.
        /// </summary>
        private void MigrateLegacyKey()
        {
            if (_s.KeysMigratedV3) return;
            try
            {
                var legacy = KeyStore.LoadMaster();
                if (legacy != null)
                {
                    string id = KeyIdOf(legacy);
                    if (_s.Role == NodeRole.Client)
                    {
                        if (_s.Servers.All(x => x.Id != id))
                        {
                            var e = new ServerEntry
                            {
                                Id = id,
                                Name = _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : "Server 1",
                                Callsign = _s.PeerCallsign,
                                Ip = _s.LastServerIp, ControlPort = _s.LastServerControlPort, AudioPort = _s.LastServerAudioPort,
                                Lan = _s.LastServerLan, PairedAt = DateTime.Now, LastUsed = DateTime.Now
                            };
                            KeyStore.SaveMaster(legacy, id);
                            if (KeyStore.LoadMaster(id) == null) return;   // copia non riuscita: la chiave unica resta
                            _s.Servers.Add(e);
                            _s.ActiveServerId = id;
                            SessionLog.Write($"SERVER abbinamento precedente conservato come «{e.Name}» (id {id})");
                        }
                    }
                    else if (_s.Clients.All(x => x.Id != id))
                    {
                        KeyStore.SaveMaster(legacy, "cli-" + id);
                        if (KeyStore.LoadMaster("cli-" + id) == null) return;
                        _s.Clients.Add(new ClientEntry
                        {
                            Id = id, Name = _s.PeerCallsign.Length > 0 ? _s.PeerCallsign : "Client 1", Callsign = _s.PeerCallsign,
                            How = "abbinamento precedente", PairedAt = DateTime.Now, LastUsed = DateTime.Now
                        });
                        SessionLog.Write($"CLIENT abbinamento precedente conservato (id {id})");
                    }
                    KeyStore.DeleteMaster();
                }
                _s.KeysMigratedV3 = true;
                _s.Save();
            }
            catch (Exception ex) { SessionLog.Write("ABBINAMENTO conversione della chiave precedente non riuscita: " + ex.Message); }
        }

        /// <summary>Indirizzi e nominativo del server scelto nei campi usati dal collegamento.</summary>
        private void ApplyEntry(ServerEntry e)
        {
            _s.LastServerIp = e.Ip;
            _s.LastServerControlPort = e.ControlPort;
            _s.LastServerAudioPort = e.AudioPort;
            _s.LastServerLan = e.Lan;
            _s.PeerCallsign = e.Callsign;
        }

        /// <summary>Ultimo indirizzo e rete locale del server in uso, anche nel suo posto in elenco.</summary>
        private void SyncActiveEntry()
        {
            if (_s.Role != NodeRole.Client || ActiveServer is not { } e) return;
            e.Ip = _s.LastServerIp;
            e.ControlPort = _s.LastServerControlPort;
            e.AudioPort = _s.LastServerAudioPort;
            e.Lan = _s.LastServerLan;
            e.LastUsed = DateTime.Now;
        }

        /// <summary>
        /// Client: passa a un altro server abbinato. Il collegamento in corso si chiude prima: il client
        /// è collegato a un solo server per volta.
        /// </summary>
        public bool SwitchServer(string id)
        {
            if (_s.Role != NodeRole.Client) return false;
            var e = _s.Servers.FirstOrDefault(x => x.Id == id);
            if (e == null) return false;
            if (e.Id == _s.ActiveServerId && _master != null) return true;
            var key = KeyStore.LoadMaster(id);
            if (key == null) { Notice?.Invoke($"La chiave del server «{e.Name}» non si legge: abbinalo di nuovo."); return false; }
            Audio.Keyer.Stop();
            SetPttLocal(false);
            StopClient();
            _s.ActiveServerId = id;
            e.LastUsed = DateTime.Now;
            ApplyEntry(e);
            _master = key;
            _rdv = new Rendezvous(key, _s.MqttBrokers);
            _s.Save();
            LastTelemetry = new RigTelemetry();
            TelemetryChanged?.Invoke(LastTelemetry);
            SessionLog.Write($"SERVER scelto: {e.Name} (id {id})");
            ServersChanged?.Invoke();
            _busyUntil = DateTime.MinValue;   // l'attesa per «server occupato» valeva per l'altro server
            StartClient();
            return true;
        }

        public void RenameServer(string id, string name)
        {
            var e = _s.Servers.FirstOrDefault(x => x.Id == id);
            if (e == null || string.IsNullOrWhiteSpace(name)) return;
            e.Name = name.Trim();
            _s.Save();
            ServersChanged?.Invoke();
        }

        /// <summary>Toglie un server dall'elenco e cancella la sua chiave; se era in uso passa al più recente.</summary>
        public void DeleteServer(string id)
        {
            var e = _s.Servers.FirstOrDefault(x => x.Id == id);
            if (e == null) return;
            KeyStore.DeleteMaster(id);
            _s.Servers.Remove(e);
            SessionLog.Write($"SERVER tolto dall'elenco: {e.Name} (id {id})");
            if (_s.ActiveServerId == id)
            {
                StopClient();
                _master = null;
                _rdv = null;
                _s.ActiveServerId = "";
                var next = _s.Servers.OrderByDescending(x => x.LastUsed).FirstOrDefault();
                _s.Save();
                if (next != null) { SwitchServer(next.Id); return; }
                _s.LastServerIp = ""; _s.LastServerControlPort = 0; _s.LastServerAudioPort = 0; _s.LastServerLan = ""; _s.PeerCallsign = "";
            }
            _s.Save();
            ServersChanged?.Invoke();
        }

        private void CloseChannels()
        {
            Interlocked.Exchange(ref _control, null)?.Dispose();
            Interlocked.Exchange(ref _audio, null)?.Dispose();
            Stats.Connected = false;
        }

        private void OnConnected()
        {
            if (!IsServer && _spectrumAsked) { _spectrumAsked = false; SpectrumResend?.Invoke(); }
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

                    // il permesso può cambiare durante la trasmissione (operatore alla radio, solo ascolto)
                    if (IsServer && _remotePtt && Stats.PeerTx && !SessionCanTransmit(out string denied)) { NotifyRefusedOnce(denied); await ApplyPttAsync(); }
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
