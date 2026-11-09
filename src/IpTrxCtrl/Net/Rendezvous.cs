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

        public async Task<bool> PublishAsync(ServerAddress addr, CancellationToken ct)
        {
            byte[] plain = Crypto.Concat(addr.ToBytes(), BitConverter.GetBytes(DateTime.UtcNow.Ticks));
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
                    if (addr.IsValid && ticks > bestTicks) { best = addr; bestTicks = ticks; }
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            LastOkBrokers = ok;
            return best;
        }
    }

    /// <summary>
    /// Proposta di porte del client (collegamento inverso): giro, IP pubblico, porte aperte con UPnP
    /// (0 = nessuna) e porta di controllo del server che il client non è riuscito a raggiungere.
    /// </summary>
    public sealed record PortOffer(int Round, string Ip, int ControlPort, int AudioPort, int TriedServerPort, long Ticks)
    {
        public byte[] ToBytes() => new MsgWriter(0).I32(Round).Str(Ip).I32(ControlPort).I32(AudioPort).I32(TriedServerPort).I64(Ticks).Build();

        public static PortOffer? FromBytes(byte[] b)
        {
            try
            {
                var r = new MsgReader(b);
                return new PortOffer(r.I32(), r.Str(), r.I32(), r.I32(), r.I32(), r.I64());
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

        private static byte[] ServerPayload(byte[] secret, byte[] nonce, ServerAddress addr, bool upnp) =>
            Crypto.Seal(Key(secret), Crypto.Concat(nonce, addr.ToBytes(), new[] { (byte)(upnp ? 1 : 0) }, Encoding.UTF8.GetBytes(Version)));

        /// <summary>
        /// Server: resta in ascolto su tutti i broker finché l'abbinamento non è completato (ct annullato).
        /// </summary>
        public static async Task ServerListenAsync(byte[] secret, Func<(ServerAddress? addr, bool upnp)> current,
            IReadOnlyList<string> brokers, MqttLog log, CancellationToken ct)
        {
            string tCli = Base(secret) + "/cli", tSrv = Base(secret) + "/srv";
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
                            if (e.ApplicationMessage.Topic != tCli) return;
                            byte[] raw = e.ApplicationMessage.PayloadSegment.ToArray();
                            if (raw.Length == 0) return;
                            byte[] plain = Crypto.Open(key, raw);
                            if (plain.Length < 16) return;
                            byte[] nonce = plain.AsSpan(0, 16).ToArray();
                            string who = Encoding.UTF8.GetString(plain, 16, plain.Length - 16);
                            log('◀', $"PUBLISH  {Mqtt.Short(tCli)}  HELLO dal client {who} ({raw.Length} B)");
                            var (addr, upnp) = current();
                            if (addr == null) { log('✗', "indirizzo pubblico del server non ancora disponibile"); return; }
                            byte[] payload = ServerPayload(secret, nonce, addr.Value, upnp);
                            await c.PublishAsync(Mqtt.Message(tSrv, payload, false), ct);
                            log('▶', $"PUBLISH  {Mqtt.Short(tSrv)}  IP {addr.Value.Ip} · porte {addr.Value.ControlPort} / {addr.Value.AudioPort}");
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
        public static async Task<ServerAddress?> ClientFindAsync(byte[] secret, string callsign, IReadOnlyList<string> brokers,
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
                    bool upnp = plain[24] == 1;
                    string ver = Encoding.UTF8.GetString(plain, 25, plain.Length - 25);
                    log('◀', $"PUBLISH  {Mqtt.Short(tSrv)}  server presente · IP {addr.Ip}");
                    log(' ', $"porte {addr.ControlPort} / {addr.AudioPort} · UPnP {(upnp ? "ok" : "non disponibile")} · versione {ver}");
                    return addr;
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
    }
}
