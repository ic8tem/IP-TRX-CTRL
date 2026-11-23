// Protocollo di rete delle radio Icom (IC-7610, IC-9700, IC-705, IC-7300MK2, IC-905, IC-R8600…), lo stesso di RS-BA1.
// Portato in C# da wfview (src/radio/icomudp*.cpp, include/packettypes.h), Copyright 2020-2024 Phil Taylor M0VSE
// e gli autori di wfview, GNU GPL 3; wfview a sua volta si basa su «kappanhang» di HA2NON, ES1AKOS e W6EL.
// In IP-TRX-CTRL (GNU AGPL 3 o successiva) il codice GPL 3 si combina come previsto dalla sezione 13 delle due licenze.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Collegamento alla radio Icom in rete: tre flussi UDP (controllo sulla porta 50001, CI-V e audio sulle porte
    /// che la radio comunica dopo l'accesso). Il CI-V va a rigctld tramite un ponte TCP locale; l'audio (PCM 16 bit
    /// a 8 kHz) diventa la «scheda audio» del server.
    /// </summary>
    public sealed class IcomLanLink : IDisposable
    {
        public static IcomLanLink? Current { get; private set; }

        public const int Rate = 8000;
        private readonly string _host, _user, _pass;
        private readonly int _port;
        private readonly CancellationTokenSource _cts = new();
        private IcomControlStream? _ctl;
        private IcomDataStream? _civ, _audio;
        private IPAddress? _radioIp, _localIp;

        public string Status { get; private set; } = "Icom LAN: avvio…";
        public bool CivReady => _civ?.Ready == true;
        public bool AudioReady => _audio?.Ready == true;
        public string RadioName { get; private set; } = "";

        /// <summary>Byte CI-V arrivati dalla radio (risposte e dati non richiesti).</summary>
        public event Action<byte[]>? CivReceived;
        /// <summary>Audio ricevuto dalla radio: campioni a 8 kHz.</summary>
        public event Action<short[]>? AudioReceived;
        public event Action<string>? StatusChanged;

        public IcomLanLink(string host, int port, string user, string pass)
        {
            _host = host.Trim(); _port = port > 0 ? port : 50001; _user = user; _pass = pass;
            Current = this;
            _ = Task.Run(() => RunAsync(_cts.Token));
        }

        private void SetStatus(string s)
        {
            if (s == Status) return;
            Status = s;
            SessionLog.Write("ICOM LAN " + s);
            StatusChanged?.Invoke(s);
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _radioIp = IPAddress.TryParse(_host, out var ip) ? ip
                        : (await Dns.GetHostAddressesAsync(_host, ct)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (_radioIp == null) throw new Exception($"indirizzo {_host} non trovato");
                    _localIp = LocalIpFor(_radioIp);
                    SetStatus($"collegamento a {_radioIp}:{_port}…");
                    using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    _ctl = new IcomControlStream(this, _localIp, _radioIp, _port, _user, _pass);
                    await _ctl.RunAsync(session.Token);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { SetStatus("errore: " + ex.Message); }
                CloseStreams();
                try { await Task.Delay(5000, ct); } catch { break; }
            }
        }

        internal void OnStatus(string s) => SetStatus(s);
        internal void OnRadioName(string name) => RadioName = name;

        /// <summary>La radio ha dato le porte di CI-V e audio: apre i due flussi.</summary>
        internal void OpenStreams(UdpClient civSock, UdpClient audioSock, int civPort, int audioPort)
        {
            CloseStreams();
            _civ = new IcomDataStream(IcomStreamKind.Civ, _localIp!, _radioIp!, civPort, civSock, this);
            _audio = new IcomDataStream(IcomStreamKind.Audio, _localIp!, _radioIp!, audioPort, audioSock, this);
            _civ.Start(_cts.Token);
            _audio.Start(_cts.Token);
        }

        internal void CloseStreams()
        {
            try { _civ?.Close(); } catch { }
            try { _audio?.Close(); } catch { }
            _civ = null; _audio = null;
        }

        internal void OnCiv(byte[] data) { try { CivReceived?.Invoke(data); } catch { } }
        internal void OnAudio(short[] pcm) { try { AudioReceived?.Invoke(pcm); } catch { } }

        public void SendCiv(byte[] data) => _civ?.SendCiv(data);

        /// <summary>Audio da trasmettere: campioni a 8 kHz (si spediscono a pacchetti da 20 ms).</summary>
        public void SendAudio(short[] pcm) => _audio?.SendAudio(pcm);

        private static IPAddress LocalIpFor(IPAddress remote)
        {
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect(remote, 50001);
                return ((IPEndPoint)s.LocalEndPoint!).Address;
            }
            catch { return IPAddress.Loopback; }
        }

        public void Dispose()
        {
            _ctl?.Shutdown();
            CloseStreams();
            _cts.Cancel();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        /// <summary>Codifica di nome utente e password del protocollo Icom (tabella di sostituzione di wfview).</summary>
        internal static byte[] Passcode(string s)
        {
            byte[] seq =
            {
                0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
                0x47,0x5d,0x4c,0x42,0x66,0x20,0x23,0x46,0x4e,0x57,0x45,0x3d,0x67,0x76,0x60,0x41,0x62,0x39,0x59,0x2d,0x68,0x7e,
                0x7c,0x65,0x7d,0x49,0x29,0x72,0x73,0x78,0x21,0x6e,0x5a,0x5e,0x4a,0x3e,0x71,0x2c,0x2a,0x54,0x3c,0x3a,0x63,0x4f,
                0x43,0x75,0x27,0x79,0x5b,0x35,0x70,0x48,0x6b,0x56,0x6f,0x34,0x32,0x6c,0x30,0x61,0x6d,0x7b,0x2f,0x4b,0x64,0x38,
                0x2b,0x2e,0x50,0x40,0x3f,0x55,0x33,0x37,0x25,0x77,0x24,0x26,0x74,0x6a,0x28,0x53,0x4d,0x69,0x22,0x5c,0x44,0x31,
                0x36,0x58,0x3b,0x7a,0x51,0x5f,0x52,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
            };
            byte[] a = Encoding.ASCII.GetBytes(s ?? "");
            var res = new List<byte>();
            for (int i = 0; i < a.Length && i < 16; i++)
            {
                int p = a[i] + i;
                if (p > 126) p = 32 + p % 127;
                res.Add(p < seq.Length ? seq[p] : (byte)0);
            }
            return res.ToArray();
        }

        // ---- password della radio custodita come le chiavi dell'abbinamento (DPAPI, Portachiavi, file cifrato)
        private const string SecretId = "icom-lan";

        public static string LoadPassword()
        {
            var b = KeyStore.LoadMaster(SecretId);
            if (b == null || b[0] > 31) return "";
            return Encoding.UTF8.GetString(b, 1, b[0]);
        }

        public static void SavePassword(string pass)
        {
            var raw = Encoding.UTF8.GetBytes(pass ?? "");
            if (raw.Length == 0) { KeyStore.DeleteMaster(SecretId); return; }
            var b = new byte[32];
            int n = Math.Min(raw.Length, 31);
            b[0] = (byte)n;
            Array.Copy(raw, 0, b, 1, n);
            KeyStore.SaveMaster(b, SecretId);
        }
    }

    internal enum IcomStreamKind { Control, Civ, Audio }

    /// <summary>Parte comune dei tre flussi: numeri di sequenza, ritrasmissioni chieste dalla radio, ping.</summary>
    internal abstract class IcomStreamBase
    {
        protected const int ControlSize = 0x10, PingSize = 0x15, OpenCloseSize = 0x16, TokenSize = 0x40, StatusSize = 0x50,
            LoginResponseSize = 0x60, LoginSize = 0x80, ConnInfoSize = 0x90, CapabilitiesSize = 0x42, RadioCapSize = 0x66;

        protected readonly UdpClient Udp;
        protected readonly IPEndPoint Remote;
        protected readonly uint MyId;
        protected uint RemoteId;
        private ushort _sendSeq, _pingSeq;
        private readonly Dictionary<ushort, byte[]> _txBuf = new();
        private readonly Queue<ushort> _txOrder = new();
        private readonly object _sendLock = new();
        protected readonly Stopwatch Clock = Stopwatch.StartNew();
        protected long LastTracked, LastReceived;
        protected volatile bool Closed;

        protected IcomStreamBase(IPAddress local, IPAddress radio, int port, UdpClient? sock)
        {
            Udp = sock ?? new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            Remote = new IPEndPoint(radio, port);
            int lport = ((IPEndPoint)Udp.Client.LocalEndPoint!).Port;
            byte[] a = local.GetAddressBytes();
            MyId = (uint)(a[2] << 24 | a[3] << 16 | (lport & 0xffff));
        }

        protected void Raw(byte[] d)
        {
            if (Closed) return;
            try { lock (_sendLock) Udp.Send(d, d.Length, Remote); } catch { }
        }

        protected static byte[] Header(int len, ushort type, ushort seq, uint sent, uint rcvd)
        {
            var p = new byte[len];
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0), (uint)len);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4), type);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(6), seq);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8), sent);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(12), rcvd);
            return p;
        }

        public void SendControl(bool tracked, byte type, ushort seq)
        {
            var p = Header(ControlSize, type, seq, MyId, RemoteId);
            if (tracked) SendTracked(p); else Raw(p);
        }

        protected void SendTracked(byte[] d)
        {
            lock (_sendLock)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(6), _sendSeq);
                if (_sendSeq == 0) { _txBuf.Clear(); _txOrder.Clear(); }
                _txBuf[_sendSeq] = d;
                _txOrder.Enqueue(_sendSeq);
                while (_txOrder.Count > 500) _txBuf.Remove(_txOrder.Dequeue());
                _sendSeq++;
                LastTracked = Clock.ElapsedMilliseconds;
                if (!Closed) { try { Udp.Send(d, d.Length, Remote); } catch { } }
            }
        }

        protected void SendPing()
        {
            var p = Header(PingSize, 0x07, _pingSeq, MyId, RemoteId);
            p[0x10] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x11), (uint)DateTime.Now.TimeOfDay.TotalMilliseconds);
            Raw(p);
        }

        /// <summary>Gestione comune: ritrasmissioni, ping, «I am here». Restituisce false se il pacchetto è già consumato.</summary>
        protected bool HandleBase(byte[] r)
        {
            if (r.Length < ControlSize) return false;
            LastReceived = Clock.ElapsedMilliseconds;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4));
            ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(6));
            if (type == 0x01)
            {
                // la radio chiede di nuovo uno o più pacchetti
                var seqs = new List<ushort>();
                if (r.Length == ControlSize) seqs.Add(seq);
                else for (int i = ControlSize; i + 1 < r.Length; i += 2) seqs.Add(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(i)));
                foreach (var s in seqs.Distinct())
                {
                    byte[]? d;
                    lock (_sendLock) _txBuf.TryGetValue(s, out d);
                    if (d != null) Raw(d); else SendControl(false, 0x00, s);
                }
                return false;
            }
            if (r.Length == ControlSize && type == 0x04)
            {
                // «I am here»: la radio dà il suo identificativo, si chiede «are you ready»
                RemoteId = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8));
                OnIAmHere();
                SendControl(false, 0x06, 0x01);
                return false;
            }
            if (r.Length == PingSize && type == 0x07)
            {
                if (r[0x10] == 0x00)
                {
                    var p = Header(PingSize, 0x07, seq, MyId, RemoteId);
                    p[0x10] = 0x01;
                    Array.Copy(r, 0x11, p, 0x11, 4);
                    Raw(p);
                }
                else if (seq == _pingSeq) _pingSeq++;
                return false;
            }
            return true;
        }

        protected virtual void OnIAmHere() { }

        public virtual void Close()
        {
            if (Closed) return;
            SendControl(false, 0x05, 0);
            Closed = true;
            try { Udp.Dispose(); } catch { }
        }
    }

    /// <summary>Flusso di controllo: accesso con nome e password, gettone rinnovato ogni minuto, richiesta dei flussi.</summary>
    internal sealed class IcomControlStream : IcomStreamBase
    {
        private readonly IcomLanLink _link;
        private readonly string _user, _pass;
        private bool _hereReceived, _authenticated, _streamOpen, _radioSelected, _loginSent;
        private ushort _authSeq = 0x30, _tokRequest;
        private uint _token;
        private readonly List<byte[]> _radios = new();
        private byte[] _radioCap = Array.Empty<byte>();
        private UdpClient? _civSock, _audioSock;
        private long _lastToken;
        private readonly TaskCompletionSource<bool> _done = new();

        public IcomControlStream(IcomLanLink link, IPAddress local, IPAddress radio, int port, string user, string pass)
            : base(local, radio, port, null)
        {
            _link = link; _user = user; _pass = pass;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            using var reg = ct.Register(() => _done.TrySetResult(true));
            _ = Task.Run(() => ReceiveLoopAsync(ct));
            long lastAyt = -1000, lastPing = 0;
            while (!ct.IsCancellationRequested && !_done.Task.IsCompleted)
            {
                long now = Clock.ElapsedMilliseconds;
                if (!_hereReceived)
                {
                    if (now - lastAyt >= 500) { SendControl(false, 0x03, 0); lastAyt = now; }
                    if (now > 15000) throw new Exception("la radio non risponde (accesa? rete attiva nel menu? indirizzo e porta giusti?)");
                }
                else
                {
                    if (now - lastPing >= 500) { SendPing(); lastPing = now; }
                    if (now - LastTracked >= 100) SendControl(true, 0x00, 0);   // pacchetto «idle»
                    if (_authenticated && now - _lastToken >= 60000) { SendToken(0x05); _lastToken = now; }
                    if (now - LastReceived > 15000) throw new Exception("la radio non risponde più");
                }
                try { await Task.Delay(50, ct); } catch { break; }
            }
        }

        public void Shutdown()
        {
            if (_authenticated) { try { SendToken(0x01); } catch { } }
            Thread.Sleep(50);
            Close();
            _done.TrySetResult(true);
        }

        protected override void OnIAmHere()
        {
            if (_hereReceived) return;
            _hereReceived = true;
            _link.OnStatus("la radio risponde, accesso in corso…");
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !Closed)
            {
                UdpReceiveResult res;
                try { res = await Udp.ReceiveAsync(ct); }
                catch { break; }
                try { Handle(res.Buffer); } catch (Exception ex) { SessionLog.Write("ICOM LAN pacchetto non valido: " + ex.Message); }
            }
        }

        private void Handle(byte[] r)
        {
            if (!HandleBase(r)) return;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4));
            switch (r.Length)
            {
                case ControlSize:
                    if (type == 0x06 && !_loginSent) { _loginSent = true; SendLogin(); }   // «I am ready»
                    else if (type == 0x05) _link.OnStatus("la radio ha chiuso il collegamento");
                    break;
                case TokenSize:
                    if (r[0x15] == 0x05 && r[0x14] == 0x02 && type != 0x01)
                    {
                        uint resp = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x30));
                        if (resp == 0) { if (!_streamOpen) RequestStreamIfReady(); }
                        else if (resp == 0xffffffff)
                        {
                            RemoteId = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8));
                            _tokRequest = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x1a));
                            _token = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x1c));
                            _streamOpen = false;
                            RequestStreamIfReady();
                        }
                    }
                    break;
                case StatusSize:
                    if (type == 0x01) break;
                    {
                        uint err = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x30));
                        byte disc = r[0x40];
                        int civPort = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(0x42));
                        int audioPort = BinaryPrimitives.ReadUInt16BigEndian(r.AsSpan(0x46));
                        if (err == 0xffffffff && !_streamOpen) _link.OnStatus("collegamento rifiutato dalla radio: riprova tra qualche minuto o riaccendila");
                        else if (err == 0 && disc == 0x01)
                        {
                            _link.OnStatus("la radio ha chiuso i flussi di CI-V e audio");
                            _link.CloseStreams();
                            _streamOpen = false;
                        }
                        else if (!_streamOpen && (err != 0 || civPort == 0 || audioPort == 0))
                            _link.OnStatus("la radio è occupata da un'altra sessione: riprovo al prossimo rinnovo");
                        else if (!_streamOpen && _civSock != null && _audioSock != null)
                        {
                            _streamOpen = true;
                            _link.OpenStreams(_civSock, _audioSock, civPort, audioPort);
                            _link.OnStatus($"collegato a {_link.RadioName} (CI-V {civPort}, audio {audioPort})");
                        }
                    }
                    break;
                case LoginResponseSize:
                    if (type == 0x01) break;
                    {
                        uint err = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x30));
                        if (err == 0xfeffffff) { _link.OnStatus("nome utente o password della radio sbagliati"); break; }
                        if (!_authenticated && BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x1a)) == _tokRequest)
                        {
                            _token = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x1c));
                            SendToken(0x02);
                            _lastToken = Clock.ElapsedMilliseconds;
                            _authenticated = true;
                            _link.OnStatus("accesso riuscito");
                        }
                    }
                    break;
                case ConnInfoSize:
                    {
                        uint busy = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x60));
                        string computer = CStr(r, 0x64, 16);
                        uint ip = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x84));
                        if (!_streamOpen && _radios.Count == 1)
                        {
                            if (busy != 0 && ip != 0 && computer.Length > 0 && !computer.StartsWith(CompName(), StringComparison.Ordinal))
                            {
                                _link.OnStatus($"la radio è in uso da {computer}");
                                SendControl(false, 0x00, BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(6)));
                            }
                            else SelectRadio(0);
                        }
                    }
                    break;
                default:
                    if (type == 0x01 || r.Length < CapabilitiesSize || (r.Length - CapabilitiesSize) % RadioCapSize != 0) break;
                    _radios.Clear();
                    for (int f = CapabilitiesSize; f + RadioCapSize <= r.Length; f += RadioCapSize) _radios.Add(r.AsSpan(f, RadioCapSize).ToArray());
                    if (_radios.Count > 0)
                    {
                        _link.OnRadioName(CStr(_radios[0], 0x10, 32));
                        SessionLog.Write($"ICOM LAN radio: {CStr(_radios[0], 0x10, 32)} · audio {CStr(_radios[0], 0x30, 32)} · CI-V {_radios[0][0x52]:X2}");
                    }
                    if (_radios.Count == 1) SelectRadio(0);
                    break;
            }
        }

        private static string CStr(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && end < b.Length && b[end] != 0) end++;
            return Encoding.ASCII.GetString(b, off, end - off).Trim();
        }

        private static string CompName()
        {
            string n = Environment.MachineName;
            return (n.Length > 8 ? n[..8] : n) + "-iptrx";
        }

        private void SendLogin()
        {
            _tokRequest = (ushort)Random.Shared.Next(1, 65535);
            var p = Header(LoginSize, 0, 0, MyId, RemoteId);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x10), LoginSize - 0x10);
            p[0x14] = 0x01; p[0x15] = 0x00;
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x16), _authSeq++);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x1a), _tokRequest);
            var u = IcomLanLink.Passcode(_user); Array.Copy(u, 0, p, 0x40, Math.Min(16, u.Length));
            var w = IcomLanLink.Passcode(_pass); Array.Copy(w, 0, p, 0x50, Math.Min(16, w.Length));
            var c = Encoding.ASCII.GetBytes(CompName()); Array.Copy(c, 0, p, 0x60, Math.Min(16, c.Length));
            SendTracked(p);
        }

        private void SendToken(byte magic)
        {
            var p = Header(TokenSize, 0, 0, MyId, RemoteId);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x10), TokenSize - 0x10);
            p[0x14] = 0x01; p[0x15] = magic;
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x16), _authSeq++);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x1a), _tokRequest);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x1c), _token);
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x24), 0x0798);
            SendTracked(p);
        }

        private void RequestStreamIfReady()
        {
            if (_radioSelected) SendRequestStream();
            else if (_radios.Count == 1) SelectRadio(0);
        }

        private void SelectRadio(int i)
        {
            if (i >= _radios.Count || _streamOpen) return;
            _radioCap = _radios[i];
            _civSock ??= new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            _audioSock ??= new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            _radioSelected = true;
            SendRequestStream();
        }

        private void SendRequestStream()
        {
            if (_radioCap.Length < RadioCapSize || _civSock == null || _audioSock == null) return;
            var p = Header(ConnInfoSize, 0, 0, MyId, RemoteId);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x10), ConnInfoSize - 0x10);
            p[0x14] = 0x01; p[0x15] = 0x03;
            ushort commoncap = BinaryPrimitives.ReadUInt16LittleEndian(_radioCap.AsSpan(0x07));
            if (commoncap == 0x8010)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x27), 0x8010);
                Array.Copy(_radioCap, 0x0a, p, 0x2a, 6);
            }
            else Array.Copy(_radioCap, 0x00, p, 0x20, 16);
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x16), _authSeq++);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x1a), _tokRequest);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0x1c), _token);
            Array.Copy(_radioCap, 0x10, p, 0x40, 32);                       // nome della radio
            var u = IcomLanLink.Passcode(_user); Array.Copy(u, 0, p, 0x60, Math.Min(16, u.Length));
            ushort txRates = BinaryPrimitives.ReadUInt16LittleEndian(_radioCap.AsSpan(0x55));
            p[0x70] = 1;                                                     // ricezione
            p[0x71] = (byte)(txRates > 1 ? 1 : 0);                           // trasmissione
            p[0x72] = 0x04;                                                  // LPCM 16 bit mono in ricezione
            p[0x73] = (byte)(txRates > 1 ? 0x04 : 0);                        // e in trasmissione
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x74), IcomLanLink.Rate);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x78), (uint)(txRates > 1 ? IcomLanLink.Rate : 0));
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x7c), (uint)((IPEndPoint)_civSock.Client.LocalEndPoint!).Port);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x80), (uint)((IPEndPoint)_audioSock.Client.LocalEndPoint!).Port);
            BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(0x84), 150);    // buffer di trasmissione della radio (ms)
            p[0x88] = 1;
            SendTracked(p);
        }

        public override void Close()
        {
            base.Close();
            try { _civSock?.Dispose(); } catch { }
            try { _audioSock?.Dispose(); } catch { }
        }
    }

    /// <summary>Flusso del CI-V o dell'audio.</summary>
    internal sealed class IcomDataStream : IcomStreamBase
    {
        private readonly IcomStreamKind _kind;
        private readonly IcomLanLink _link;
        private ushort _seqB;          // numerazione interna (big endian) di CI-V e audio
        private bool _here, _civStarted;
        public bool Ready { get; private set; }
        private readonly List<short> _txPending = new();
        private readonly object _txLock = new();

        public IcomDataStream(IcomStreamKind kind, IPAddress local, IPAddress radio, int port, UdpClient sock, IcomLanLink link)
            : base(local, radio, port, sock)
        {
            _kind = kind; _link = link;
        }

        public void Start(CancellationToken ct)
        {
            _ = Task.Run(() => ReceiveLoopAsync(ct));
            _ = Task.Run(() => TimerLoopAsync(ct));
        }

        protected override void OnIAmHere() => _here = true;

        private async Task TimerLoopAsync(CancellationToken ct)
        {
            long lastAyt = -1000, lastPing = 0, lastOpen = 0;
            while (!ct.IsCancellationRequested && !Closed)
            {
                long now = Clock.ElapsedMilliseconds;
                if (!_here) { if (now - lastAyt >= 500) { SendControl(false, 0x03, 0); lastAyt = now; } }
                else
                {
                    if (now - lastPing >= 500) { SendPing(); lastPing = now; }
                    if (_kind == IcomStreamKind.Civ)
                    {
                        if (now - LastTracked >= 100) SendControl(true, 0x00, 0);
                        // nessun dato CI-V da 2 s (o non ancora partito): si chiede di nuovo l'apertura
                        if (_civStarted && (now - LastReceived > 2000 || !Ready) && now - lastOpen >= 500) { SendOpenClose(false); lastOpen = now; }
                    }
                }
                try { await Task.Delay(50, ct); } catch { break; }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !Closed)
            {
                UdpReceiveResult res;
                try { res = await Udp.ReceiveAsync(ct); }
                catch { break; }
                try { Handle(res.Buffer); } catch { }
            }
        }

        private void Handle(byte[] r)
        {
            if (!HandleBase(r)) return;
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4));
            if (r.Length == ControlSize)
            {
                if (type == 0x06 && _kind == IcomStreamKind.Civ)
                {
                    RemoteId = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8));
                    _civStarted = true;
                    SendOpenClose(false);
                }
                else if (type == 0x06 && _kind == IcomStreamKind.Audio) Ready = true;
                return;
            }
            if (type == 0x01) return;
            if (_kind == IcomStreamKind.Civ)
            {
                if (r.Length <= 0x15) return;
                int datalen = BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x11));
                uint len = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0));
                if ((ushort)(datalen + 0x15) != (ushort)len) return;
                Ready = true;
                _link.OnCiv(r.AsSpan(0x15).ToArray());
            }
            else
            {
                uint len = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0));
                if (len < 0x20 || r.Length <= 0x18) return;
                Ready = true;
                int n = (r.Length - 0x18) / 2;
                var pcm = new short[n];
                for (int i = 0; i < n; i++) pcm[i] = BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(0x18 + 2 * i));
                _link.OnAudio(pcm);
            }
        }

        private void SendOpenClose(bool close)
        {
            var p = Header(OpenCloseSize, 0, 0, MyId, RemoteId);
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x10), 0x01c0);
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x13), _seqB++);
            p[0x15] = (byte)(close ? 0x00 : 0x04);
            SendTracked(p);
        }

        public void SendCiv(byte[] d)
        {
            if (_kind != IcomStreamKind.Civ || Closed || d.Length == 0) return;
            var p = Header(0x15 + d.Length, 0, 0, MyId, RemoteId);
            p[0x10] = 0xc1;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x11), (ushort)d.Length);
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x13), _seqB++);
            Array.Copy(d, 0, p, 0x15, d.Length);
            SendTracked(p);
        }

        /// <summary>Audio verso la radio a pacchetti da 20 ms (160 campioni, 320 byte) come fa RS-BA1.</summary>
        public void SendAudio(short[] pcm)
        {
            if (_kind != IcomStreamKind.Audio || Closed || !_here) return;
            const int per = IcomLanLink.Rate / 50;
            lock (_txLock)
            {
                _txPending.AddRange(pcm);
                while (_txPending.Count >= per)
                {
                    var p = Header(0x18 + per * 2, 0, 0, MyId, RemoteId);
                    BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0x10), (ushort)(per * 2 == 0xa0 ? 0x9781 : 0x0080));
                    BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x12), _seqB++);
                    BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0x16), (ushort)(per * 2));
                    for (int i = 0; i < per; i++) BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0x18 + 2 * i), _txPending[i]);
                    _txPending.RemoveRange(0, per);
                    SendTracked(p);
                }
            }
        }

        public override void Close()
        {
            if (Closed) return;
            if (_kind == IcomStreamKind.Civ && _civStarted) { try { SendOpenClose(true); } catch { } }
            base.Close();
        }
    }

    /// <summary>
    /// Ponte TCP locale per rigctld: Hamlib apre «127.0.0.1:porta» come se fosse la seriale CI-V della radio
    /// (Hamlib tratta host:porta come una porta di rete), e i byte passano dal flusso CI-V del protocollo Icom.
    /// </summary>
    public sealed class IcomCivBridge : IDisposable
    {
        private readonly IcomLanLink _link;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private NetworkStream? _client;
        public int Port { get; }

        public IcomCivBridge(IcomLanLink link)
        {
            _link = link;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _link.CivReceived += OnCiv;
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        private void OnCiv(byte[] d)
        {
            var c = _client;
            if (c == null) return;
            try { c.Write(d, 0, d.Length); } catch { }
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await _listener.AcceptTcpClientAsync(ct); } catch { break; }
                tcp.NoDelay = true;
                var s = tcp.GetStream();
                Interlocked.Exchange(ref _client, s)?.Dispose();
                _ = Task.Run(async () =>
                {
                    var buf = new byte[1024];
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            int n = await s.ReadAsync(buf.AsMemory(0, buf.Length), ct);
                            if (n <= 0) break;
                            _link.SendCiv(buf.AsSpan(0, n).ToArray());
                        }
                    }
                    catch { }
                    Interlocked.CompareExchange(ref _client, null, s);
                    try { tcp.Dispose(); } catch { }
                }, ct);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _link.CivReceived -= OnCiv;
            try { _listener.Stop(); } catch { }
            try { _client?.Dispose(); } catch { }
        }
    }
}
