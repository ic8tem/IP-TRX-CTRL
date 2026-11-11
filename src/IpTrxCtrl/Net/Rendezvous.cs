using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace IpTrxCtrl.Net
{
    /// <summary>Riga del registro del dialogo MQTT: '▶' inviato, '◀' ricevuto, '✗' errore, '✓' completato, '·' nota.</summary>
    public delegate void MqttLog(char symbol, string text);

    internal static class Mqtt
    {
        public static (string host, int port) Split(string broker)
        {
            string host = broker.Trim();
            int port = 1883;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && int.TryParse(host[(colon + 1)..], out int p)) { port = p; host = host[..colon]; }
            return (host, port);
        }

        public static MqttClientOptions Options(string broker, string clientId)
        {
            var (host, port) = Split(broker);
            return new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .WithClientId(clientId)
                .WithCleanSession(true)
                .WithTimeout(TimeSpan.FromSeconds(8))
                .Build();
        }

        public static string NewClientId() => "iptrx-" + Guid.NewGuid().ToString("N")[..10];

        public static MqttApplicationMessage Message(string topic, byte[] payload, bool retain) =>
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

        public static MqttClientSubscribeOptions Subscribe(string topic) =>
            new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                .Build();

        public static string Short(string topic)
        {
            // aaa/bbb/0123456789abcdef/cli -> aaa/bbb/0123…ef/cli
            var parts = topic.Split('/');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 10) parts[i] = parts[i][..4] + "…" + parts[i][^2..];
            return string.Join('/', parts);
        }

        public static async Task SafeDisconnect(IMqttClient c)
        {
            try { if (c.IsConnected) await c.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None); } catch { }
        }
    }

    /// <summary>
    /// Punto d'incontro su broker MQTT pubblico dopo l'abbinamento. Il server lascia IP e porte cifrati
    /// (messaggio "retained") su un canale il cui nome deriva dalla chiave di abbinamento.
    /// </summary>
    public sealed class Rendezvous
    {
        private readonly byte[] _key;
        private readonly string _topic;
        private readonly IReadOnlyList<string> _brokers;

        public Rendezvous(byte[] master, IReadOnlyList<string> brokers)
        {
            _key = Crypto.Hkdf(master, Array.Empty<byte>(), "aal-rendezvous");
            _topic = "aal/" + Convert.ToHexString(Crypto.Hkdf(master, Array.Empty<byte>(), "aal-topic", 12)).ToLowerInvariant();
            _brokers = brokers;
        }

        /// <summary>Numero di broker che hanno accettato l'indirizzo.</summary>
        public int LastOkBrokers { get; private set; }

        /// <summary>IP di rete locale del server letto nell'ultima ricerca ("" se assente).</summary>
        public string LastLan { get; private set; } = "";

        /// <summary>Versione del server letta nell'ultima ricerca («P2 0.2.3», "" se il server è più vecchio).</summary>
        public string LastVersion { get; private set; } = "";

        public async Task<bool> PublishAsync(ServerAddress addr, string lan, string versionTag, CancellationToken ct)
        {
            // indirizzo · ora · IP locale (4 byte, 0.0.0.0 se manca) · versione
            byte[] lanBytes = System.Net.IPAddress.TryParse(lan, out var l) && l.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? l.GetAddressBytes() : new byte[4];
            byte[] plain = Crypto.Concat(addr.ToBytes(), BitConverter.GetBytes(DateTime.UtcNow.Ticks), lanBytes, Encoding.UTF8.GetBytes(versionTag));
            byte[] payload = Crypto.Seal(_key, plain);
            int ok = 0;
            foreach (string broker in _brokers)
            {
                using var client = new MqttFactory().CreateMqttClient();
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await client.PublishAsync(Mqtt.Message(_topic, payload, true), t.Token);
                    ok++;
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            LastOkBrokers = ok;
            return ok > 0;
        }

        /// <summary>Client: lascia sui broker le porte che ha aperto (giro del ciclo delle porte).</summary>
        public async Task<bool> PublishOfferAsync(PortOffer offer, CancellationToken ct)
        {
            byte[] payload = Crypto.Seal(_key, offer.ToBytes());
            int ok = 0;
            foreach (string broker in _brokers)
            {
                using var client = new MqttFactory().CreateMqttClient();
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await client.PublishAsync(Mqtt.Message(_topic + "/c", payload, true), t.Token);
                    ok++;
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            return ok > 0;
        }

        /// <summary>Server: legge la proposta di porte più recente del client.</summary>
        public async Task<PortOffer?> LookupOfferAsync(CancellationToken ct)
        {
            PortOffer? best = null;
            foreach (string broker in _brokers)
            {
                using var client = new MqttFactory().CreateMqttClient();
                try
                {
                    var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    client.ApplicationMessageReceivedAsync += e =>
                    {
                        received.TrySetResult(e.ApplicationMessage.PayloadSegment.ToArray());
                        return Task.CompletedTask;
                    };
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(8));
                    await client.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await client.SubscribeAsync(Mqtt.Subscribe(_topic + "/c"), t.Token);
                    var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(3), t.Token));
                    if (done != received.Task) continue;
                    var o = PortOffer.FromBytes(Crypto.Open(_key, received.Task.Result));
                    if (o != null && (best == null || o.Ticks > best.Ticks)) best = o;
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            return best;
        }

        /// <summary>Legge l'indirizzo più recente tra quelli lasciati sui broker.</summary>
        public async Task<ServerAddress?> LookupAsync(CancellationToken ct)
        {
            ServerAddress? best = null;
            string bestLan = "", bestVer = "";
            long bestTicks = 0;
            int ok = 0;
            foreach (string broker in _brokers)
            {
                using var client = new MqttFactory().CreateMqttClient();
                try
                {
                    var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    client.ApplicationMessageReceivedAsync += e =>
                    {
                        received.TrySetResult(e.ApplicationMessage.PayloadSegment.ToArray());
                        return Task.CompletedTask;
                    };
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    ok++;
                    await client.SubscribeAsync(Mqtt.Subscribe(_topic), t.Token);
                    var done = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5), t.Token));
                    if (done != received.Task) continue;
                    byte[] plain = Crypto.Open(_key, received.Task.Result);
                    var addr = ServerAddress.FromBytes(plain, 0);
                    long ticks = BitConverter.ToInt64(plain, 8);
                    if (addr.IsValid && ticks > bestTicks)
                    {
                        best = addr; bestTicks = ticks;
                        bestLan = plain.Length >= 20 ? new System.Net.IPAddress(plain.AsSpan(16, 4)).ToString() : "";
                        if (bestLan == "0.0.0.0") bestLan = "";
                        bestVer = plain.Length > 20 ? Encoding.UTF8.GetString(plain, 20, plain.Length - 20) : "";
                    }
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            LastOkBrokers = ok;
            LastLan = bestLan;
            LastVersion = bestVer;
            return best;
        }
    }

    /// <summary>
    /// Proposta di porte del client (collegamento inverso): giro, IP pubblico, porte aperte con UPnP
    /// (0 = nessuna) e porta di controllo del server che il client non è riuscito a raggiungere.
    /// </summary>
    public sealed record PortOffer(int Round, string Ip, int ControlPort, int AudioPort, int TriedServerPort, long Ticks)
    {
        /// <summary>Dalla 0.2.4: la rete del client filtra le uscite e queste sono le porte che passano.</summary>
        public bool Restricted { get; init; }
        public List<int> OutboundPorts { get; init; } = new();

        public byte[] ToBytes()
        {
            var w = new MsgWriter(0).I32(Round).Str(Ip).I32(ControlPort).I32(AudioPort).I32(TriedServerPort).I64(Ticks);
            w.Bool(Restricted).I32(OutboundPorts.Count);
            foreach (int p in OutboundPorts) w.I32(p);
            return w.Build();
        }

        public static PortOffer? FromBytes(byte[] b)
        {
            try
            {
                var r = new MsgReader(b);
                var o = new PortOffer(r.I32(), r.Str(), r.I32(), r.I32(), r.I32(), r.I64());
                if (r.AtEnd) return o;
                bool restricted = r.Bool();
                int n = Math.Clamp(r.I32(), 0, 32);
                var list = new List<int>();
                for (int i = 0; i < n; i++) list.Add(r.I32());
                return o with { Restricted = restricted, OutboundPorts = list };
            }
            catch { return null; }
        }
    }

    /// <summary>Stato del server mandato al client in abbinamento.</summary>
    public sealed record PairServerInfo(ServerAddress? Addr, bool Upnp, string Lan, bool Unreachable, bool Cgnat);

    /// <summary>Risposta del server trovata dal client sul broker.</summary>
    public sealed record PairFound(ServerAddress Addr, string Lan, string Version, bool Upnp, bool Unreachable, bool Cgnat);

    /// <summary>
    /// Un giro del ciclo delle porte in abbinamento, dal server al client: la porta nuova che il server ha aperto
    /// (esterna e del PC, per la rete locale) e l'esito della verifica da internet (1 sì, 0 no, -1 non verificabile).
    /// </summary>
    public sealed record PairRoundReply(int Round, string Ip, int Port, int InnerPort, int Reachable, bool Upnp, bool Cgnat, string Note)
    {
        public byte[] ToBytes() => new MsgWriter(0).I32(Round).Str(Ip).I32(Port).I32(InnerPort).I32(Reachable).Bool(Upnp).Bool(Cgnat).Str(Note).Build();
        public static PairRoundReply? FromBytes(byte[] b)
        {
            try
            {
                var r = new MsgReader(b);
                return new PairRoundReply(r.I32(), r.Str(), r.I32(), r.I32(), r.I32(), r.Bool(), r.Bool(), r.Str());
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Abbinamento tramite broker MQTT. Il nome del canale e la chiave derivano dal segreto contenuto
    /// nel codice di abbinamento: il client annuncia la sua presenza, il server risponde con l'indirizzo
    /// aggiornato (IP pubblico e porte), poi il client apre il collegamento diretto cifrato.
    /// </summary>
    public static class PairingRendezvous
    {
        public const string Version = "1.0";

        private static string Base(byte[] secret) =>
            "iptrx/pair/" + Convert.ToHexString(Crypto.Hkdf(secret, Array.Empty<byte>(), "iptrx-pair-topic", 10)).ToLowerInvariant();

        private static byte[] Key(byte[] secret) => Crypto.Hkdf(secret, Array.Empty<byte>(), "iptrx-pair-key");

        // dopo la versione: «|IP di rete locale» (i client vecchi lo mostrano come parte della versione).
        // Byte dei flag (dalla 0.2.7): 1 = UPnP, 2 = porte NON raggiungibili da internet, 4 = CGNAT probabile.
        private static byte[] ServerPayload(byte[] secret, byte[] nonce, PairServerInfo info) =>
            Crypto.Seal(Key(secret), Crypto.Concat(nonce, info.Addr!.Value.ToBytes(),
                new[] { (byte)((info.Upnp ? 1 : 0) | (info.Unreachable ? 2 : 0) | (info.Cgnat ? 4 : 0)) },
                Encoding.UTF8.GetBytes(AppVersion.Tag + "|" + info.Lan)));

        private static string TopicOffer(byte[] secret) => Base(secret) + "/off";   // client -> server: porte proposte
        private static string TopicRound(byte[] secret) => Base(secret) + "/rnd";   // server -> client: esito del giro

        /// <summary>
        /// Server: resta in ascolto su tutti i broker finché l'abbinamento non è completato (ct annullato).
        /// </summary>
        public static async Task ServerListenAsync(byte[] secret, Func<PairServerInfo> current,
            Func<PortOffer, Task<PairRoundReply?>> onOffer, IReadOnlyList<string> brokers, MqttLog log, CancellationToken ct)
        {
            string tCli = Base(secret) + "/cli", tSrv = Base(secret) + "/srv", tOff = TopicOffer(secret), tRnd = TopicRound(secret);
            var key = Key(secret);
            var clients = new List<IMqttClient>();
            try
            {
                foreach (string broker in brokers)
                {
                    if (ct.IsCancellationRequested) break;
                    var c = new MqttFactory().CreateMqttClient();
                    string id = Mqtt.NewClientId();
                    c.ApplicationMessageReceivedAsync += async e =>
                    {
                        try
                        {
                            if (e.ApplicationMessage.Topic == tOff)
                            {
                                // ciclo delle porte: proposta del client, risposta con la porta nuova del server
                                byte[] rawO = e.ApplicationMessage.PayloadSegment.ToArray();
                                if (rawO.Length == 0) return;
                                var offer = PortOffer.FromBytes(Crypto.Open(key, rawO));
                                if (offer == null) return;
                                var rep = await onOffer(offer);
                                if (rep == null) return;
                                await c.PublishAsync(Mqtt.Message(tRnd, Crypto.Seal(key, rep.ToBytes()), false), ct);
                                return;
                            }
                            if (e.ApplicationMessage.Topic != tCli) return;
                            byte[] raw = e.ApplicationMessage.PayloadSegment.ToArray();
                            if (raw.Length == 0) return;
                            byte[] plain = Crypto.Open(key, raw);
                            if (plain.Length < 16) return;
                            byte[] nonce = plain.AsSpan(0, 16).ToArray();
                            string who = Encoding.UTF8.GetString(plain, 16, plain.Length - 16);
                            log('◀', $"PUBLISH  {Mqtt.Short(tCli)}  HELLO dal client {who} ({raw.Length} B)");
                            var info = current();
                            if (info.Addr == null) { log('✗', "indirizzo pubblico del server non ancora disponibile"); return; }
                            var addr = info.Addr.Value;
                            byte[] payload = ServerPayload(secret, nonce, info);
                            await c.PublishAsync(Mqtt.Message(tSrv, payload, false), ct);
                            log('▶', $"PUBLISH  {Mqtt.Short(tSrv)}  IP {addr.Ip} · porte {addr.ControlPort} / {addr.AudioPort}" + (info.Lan.Length > 0 ? $" · LAN {info.Lan}" : "")
                                     + (info.Unreachable ? " · porte NON raggiungibili da internet" + (info.Cgnat ? " (CGNAT)" : "") : ""));
                        }
                        catch (Exception ex) { log('✗', "messaggio non valido: " + ex.Message); }
                    };
                    try
                    {
                        log('▶', $"{broker}  CONNECT  id={id}");
                        using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        t.CancelAfter(TimeSpan.FromSeconds(8));
                        var res = await c.ConnectAsync(Mqtt.Options(broker, id), t.Token);
                        log('◀', $"CONNACK  {(res.ResultCode == MqttClientConnectResultCode.Success ? "accettato" : res.ResultCode.ToString())}");
                        log('▶', $"SUBSCRIBE  {Mqtt.Short(tCli)}  QoS 1");
                        await c.SubscribeAsync(Mqtt.Subscribe(tCli), t.Token);
                        await c.SubscribeAsync(Mqtt.Subscribe(tOff), t.Token);
                        log('◀', "SUBACK  QoS 1");
                        clients.Add(c);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        log('✗', $"{broker}: nessuna risposta in 8 s → broker successivo");
                        c.Dispose();
                    }
                    catch (Exception ex)
                    {
                        log('✗', $"{broker}: {ex.Message}");
                        c.Dispose();
                    }
                }
                if (clients.Count == 0) log('✗', "nessun broker disponibile: il client userà l'indirizzo scritto nel codice");
                else log('·', $"in attesa del client su {clients.Count} broker…");
                try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            }
            finally
            {
                foreach (var c in clients) { await Mqtt.SafeDisconnect(c); c.Dispose(); }
            }
        }

        /// <summary>Client: cerca il server sui broker. Restituisce l'indirizzo aggiornato o null.</summary>
        public static async Task<PairFound?> ClientFindAsync(byte[] secret, string callsign, IReadOnlyList<string> brokers,
            MqttLog log, CancellationToken ct)
        {
            string tCli = Base(secret) + "/cli", tSrv = Base(secret) + "/srv";
            var key = Key(secret);
            foreach (string broker in brokers)
            {
                if (ct.IsCancellationRequested) break;
                using var c = new MqttFactory().CreateMqttClient();
                byte[] nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
                var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                c.ApplicationMessageReceivedAsync += e =>
                {
                    if (e.ApplicationMessage.Topic == tSrv) reply.TrySetResult(e.ApplicationMessage.PayloadSegment.ToArray());
                    return Task.CompletedTask;
                };
                string id = Mqtt.NewClientId();
                try
                {
                    log('▶', $"{broker}  CONNECT  id={id}");
                    using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        t.CancelAfter(TimeSpan.FromSeconds(8));
                        try
                        {
                            var res = await c.ConnectAsync(Mqtt.Options(broker, id), t.Token);
                            log('◀', $"CONNACK  {(res.ResultCode == MqttClientConnectResultCode.Success ? "accettato" : res.ResultCode.ToString())}");
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            log('✗', "nessuna risposta in 8 s → broker successivo");
                            continue;
                        }
                        log('▶', $"SUBSCRIBE  {Mqtt.Short(tSrv)}  QoS 1");
                        await c.SubscribeAsync(Mqtt.Subscribe(tSrv), t.Token);
                        log('◀', "SUBACK  QoS 1");
                        byte[] hello = Crypto.Seal(key, Crypto.Concat(nonce, Encoding.UTF8.GetBytes(callsign)));
                        await c.PublishAsync(Mqtt.Message(tCli, hello, false), t.Token);
                        log('▶', $"PUBLISH  {Mqtt.Short(tCli)}  HELLO cifrato {hello.Length} B");
                    }
                    var done = await Task.WhenAny(reply.Task, Task.Delay(TimeSpan.FromSeconds(8), ct));
                    if (done != reply.Task)
                    {
                        log('✗', "il server non risponde su questo broker (programma chiuso o non in attesa di abbinamento)");
                        continue;
                    }
                    byte[] plain = Crypto.Open(key, reply.Task.Result);
                    if (plain.Length < 16 + 9 || !Crypto.Equal(plain.AsSpan(0, 16).ToArray(), nonce))
                    {
                        log('✗', "risposta non valida");
                        continue;
                    }
                    var addr = ServerAddress.FromBytes(plain, 16);
                    byte flags = plain[24];
                    bool upnp = (flags & 1) != 0, unreachable = (flags & 2) != 0, cgnat = (flags & 4) != 0;
                    string ver = Encoding.UTF8.GetString(plain, 25, plain.Length - 25);
                    string lan = "";
                    int bar = ver.IndexOf('|');
                    if (bar >= 0) { lan = ver[(bar + 1)..]; ver = ver[..bar]; }
                    var (proto, sv) = AppVersion.Parse(ver);
                    log('◀', $"PUBLISH  {Mqtt.Short(tSrv)}  server presente · IP {addr.Ip}" + (lan.Length > 0 ? $" · LAN {lan}" : ""));
                    log(' ', $"porte {addr.ControlPort} / {addr.AudioPort} · UPnP {(upnp ? "ok" : "non disponibile")} · versione del server {AppVersion.Show(sv)} (protocollo {proto})");
                    if (unreachable)
                        log('·', "il server ha verificato da internet che le sue porte NON sono raggiungibili"
                                 + (cgnat ? ": il suo operatore usa un IP condiviso (CGNAT) o c'è un doppio NAT" : ""));
                    return new PairFound(addr, lan, ver, upnp, unreachable, cgnat);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    log('✗', $"{broker}: {ex.Message}");
                }
                finally
                {
                    await Mqtt.SafeDisconnect(c);
                }
            }
            return null;
        }

        /// <summary>
        /// Client, ciclo delle porte in abbinamento: resta collegato ai broker, manda le proposte di porte e
        /// riceve per ogni giro la porta nuova del server con l'esito della sua verifica.
        /// </summary>
        public sealed class ClientSession : IAsyncDisposable
        {
            private readonly byte[] _key;
            private readonly string _tOff, _tRnd;
            private readonly List<IMqttClient> _clients = new();
            private readonly System.Collections.Concurrent.ConcurrentDictionary<int, PairRoundReply> _replies = new();
            private readonly SemaphoreSlim _signal = new(0);

            private ClientSession(byte[] secret)
            {
                _key = Key(secret);
                _tOff = TopicOffer(secret);
                _tRnd = TopicRound(secret);
            }

            public int Brokers => _clients.Count;

            public static async Task<ClientSession> OpenAsync(byte[] secret, IReadOnlyList<string> brokers, MqttLog log, CancellationToken ct)
            {
                var s = new ClientSession(secret);
                foreach (string broker in brokers)
                {
                    if (ct.IsCancellationRequested) break;
                    var c = new MqttFactory().CreateMqttClient();
                    c.ApplicationMessageReceivedAsync += e =>
                    {
                        try
                        {
                            if (e.ApplicationMessage.Topic != s._tRnd) return Task.CompletedTask;
                            var r = PairRoundReply.FromBytes(Crypto.Open(s._key, e.ApplicationMessage.PayloadSegment.ToArray()));
                            if (r != null && s._replies.TryAdd(r.Round, r)) s._signal.Release();
                        }
                        catch { }
                        return Task.CompletedTask;
                    };
                    try
                    {
                        using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        t.CancelAfter(TimeSpan.FromSeconds(8));
                        await c.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                        await c.SubscribeAsync(Mqtt.Subscribe(s._tRnd), t.Token);
                        s._clients.Add(c);
                    }
                    catch { c.Dispose(); }
                }
                log('·', $"ciclo delle porte: collegato a {s._clients.Count} broker MQTT per lo scambio con il server");
                return s;
            }

            /// <summary>Manda la proposta su tutti i broker; restituisce su quanti è partita.</summary>
            public async Task<int> SendOfferAsync(PortOffer offer, CancellationToken ct)
            {
                byte[] payload = Crypto.Seal(_key, offer.ToBytes());
                int n = 0;
                foreach (var c in _clients)
                {
                    try { await c.PublishAsync(Mqtt.Message(_tOff, payload, false), ct); n++; } catch { }
                }
                return n;
            }

            /// <summary>Aspetta la risposta del server per il giro indicato (null se non arriva in tempo).</summary>
            public async Task<PairRoundReply?> WaitRoundAsync(int round, TimeSpan timeout, CancellationToken ct)
            {
                var end = DateTime.UtcNow + timeout;
                while (!ct.IsCancellationRequested)
                {
                    if (_replies.TryGetValue(round, out var r)) return r;
                    var left = end - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return null;
                    try { await _signal.WaitAsync(left, ct); } catch { return null; }
                }
                return null;
            }

            public async ValueTask DisposeAsync()
            {
                foreach (var c in _clients) { await Mqtt.SafeDisconnect(c); c.Dispose(); }
                _clients.Clear();
            }
        }
    }
}
