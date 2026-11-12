using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Net
{
    /// <summary>
    /// Accesso dal browser (dalla 0.3.1): il server fa da piccolo sito HTTPS. Pagine dalla cartella «web» incorporata
    /// nel programma (o, se c'è, dalla cartella «web» dei dati, per chi vuole modificarle), certificato fatto dal
    /// server, WebSocket «/ws» con accesso tramite il codice fisso del server (sfida e risposta HMAC: il codice non
    /// passa mai in rete). Dopo l'accesso il WebSocket porta gli stessi messaggi binari del programma client.
    /// </summary>
    public sealed class WebServer : IDisposable
    {
        private readonly AppSettings _s;
        private readonly Func<IMsgChannel, string, CancellationToken, Task> _onSession;
        private readonly Func<byte[]?> _loginKey;
        private readonly Func<string> _serverTag;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, (int fails, DateTime until)> _fails = new();

        public X509Certificate2? Certificate { get; private set; }
        public string Fingerprint { get; private set; } = "";
        public int Port { get; private set; }
        public string Status { get; private set; } = "spento";
        public bool Running => _listener != null;

        public WebServer(AppSettings s, Func<IMsgChannel, string, CancellationToken, Task> onSession, Func<byte[]?> loginKey, Func<string> serverTag)
        {
            _s = s;
            _onSession = onSession;
            _loginKey = loginKey;
            _serverTag = serverTag;
        }

        // ================================================================ avvio

        public bool Start(int port)
        {
            Stop();
            try
            {
                Certificate = LoadOrCreateCertificate(_s.Callsign);
                Fingerprint = FingerprintOf(Certificate);
                TcpListener l;
                try
                {
                    l = new TcpListener(IPAddress.IPv6Any, port);
                    l.Server.DualMode = true;
                    l.Start();
                }
                catch
                {
                    l = new TcpListener(IPAddress.Any, port);
                    l.Start();
                }
                _listener = l;
                Port = port;
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _ = Task.Run(() => AcceptLoopAsync(l, ct));
                Status = $"in ascolto su HTTPS porta {port}";
                SessionLog.Write($"WEB pagina per il browser attiva: HTTPS porta {port} · certificato {Fingerprint}");
                return true;
            }
            catch (Exception ex)
            {
                Status = $"non avviata: {ex.Message}";
                SessionLog.Write("WEB pagina per il browser non avviata: " + ex.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            try { _listener?.Stop(); } catch { }
            if (_listener != null) SessionLog.Write("WEB pagina per il browser fermata");
            _listener = null;
            Status = "spento";
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await l.AcceptTcpClientAsync(ct); }
                catch { break; }
                _ = Task.Run(() => ServeConnectionAsync(tcp, ct));
            }
        }

        // ================================================================ HTTP

        private async Task ServeConnectionAsync(TcpClient tcp, CancellationToken ct)
        {
            var remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
            var local = tcp.Client.LocalEndPoint as IPEndPoint;
            string ip = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4().ToString() : remote.Address.ToString();
            bool upgraded = false;
            SslStream? ssl = null;
            try
            {
                tcp.NoDelay = true;
                ssl = new SslStream(tcp.GetStream(), false);
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(TimeSpan.FromSeconds(15));
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = Certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificateRequired = false
                    }, t.Token);
                }
                for (int requests = 0; requests < 200 && !ct.IsCancellationRequested; requests++)
                {
                    var req = await ReadRequestAsync(ssl, ct);
                    if (req == null) break;
                    if (req.Path == "/ws" && req.Header("upgrade").Equals("websocket", StringComparison.OrdinalIgnoreCase))
                    {
                        upgraded = true;
                        await WebSocketAsync(ssl, req, tcp, remote, local, ip, ct);
                        return;
                    }
                    bool keep = await ServeFileAsync(ssl, req);
                    if (!keep) break;
                }
            }
            catch { }
            finally
            {
                if (!upgraded)
                {
                    try { ssl?.Dispose(); } catch { }
                    try { tcp.Dispose(); } catch { }
                }
            }
        }

        private sealed class Request
        {
            public string Method = "", Path = "";
            public readonly Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
            public string Header(string n) => Headers.TryGetValue(n, out var v) ? v : "";
        }

        /// <summary>Intestazione della richiesta (fino a 16 kB, corpo ignorato: solo GET).</summary>
        private static async Task<Request?> ReadRequestAsync(Stream s, CancellationToken ct)
        {
            var buf = new List<byte>(1024);
            var one = new byte[1];
            using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
            t.CancelAfter(TimeSpan.FromSeconds(60));
            while (true)
            {
                int n;
                try { n = await s.ReadAsync(one, t.Token); } catch { return null; }
                if (n == 0) return null;
                buf.Add(one[0]);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == '\r' && buf[c - 3] == '\n' && buf[c - 2] == '\r' && buf[c - 1] == '\n') break;
                if (c > 16 * 1024) return null;
            }
            string head = Encoding.ASCII.GetString(buf.ToArray());
            var lines = head.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var r = new Request { Method = first[0], Path = Uri.UnescapeDataString(first[1].Split('?')[0]) };
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon > 0) r.Headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
            return r;
        }

        private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
            [".svg"] = "image/svg+xml", [".png"] = "image/png", [".ico"] = "image/x-icon", [".json"] = "application/json",
            [".woff2"] = "font/woff2", [".webmanifest"] = "application/manifest+json"
        };

        // nessuna risorsa esterna tranne i caratteri di Google Fonts; niente script in linea
        private const string Csp = "default-src 'self'; script-src 'self'; style-src 'self' https://fonts.googleapis.com; "
            + "font-src 'self' https://fonts.gstatic.com; connect-src 'self' wss:; img-src 'self' data:; media-src 'self' blob:; "
            + "worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        private async Task<bool> ServeFileAsync(Stream s, Request req)
        {
            bool keep = !req.Header("connection").Equals("close", StringComparison.OrdinalIgnoreCase);
            if (req.Method != "GET" && req.Method != "HEAD")
            {
                await WriteAsync(s, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("metodo non permesso"), keep, req.Method == "HEAD");
                return keep;
            }
            if (req.Path == "/certificato.cer" && Certificate != null)
            {
                // per l'iPhone: il certificato da installare come profilo
                await WriteAsync(s, 200, "application/x-x509-ca-cert", Certificate.Export(X509ContentType.Cert), keep, req.Method == "HEAD",
                    "Content-Disposition: attachment; filename=\"IP-TRX-CTRL.cer\"\r\n");
                return keep;
            }
            if (req.Path == "/trova-server.html")
            {
                // la pagina «Trova il server» si scarica (va tenuta come file sul telefono o sul PC)
                var finder = ReadWebFile("trova-server.html");
                if (finder != null)
                {
                    await WriteAsync(s, 200, "text/html; charset=utf-8", finder, keep, req.Method == "HEAD",
                        "Content-Disposition: attachment; filename=\"trova-server.html\"\r\n");
                    return keep;
                }
            }
            string path = req.Path == "/" ? "/index.html" : req.Path;
            byte[]? body = ReadWebFile(path.TrimStart('/'));
            if (body == null)
            {
                await WriteAsync(s, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("pagina non trovata"), keep, req.Method == "HEAD");
                return keep;
            }
            string type = Types.TryGetValue(Path.GetExtension(path), out var tt) ? tt : "application/octet-stream";
            await WriteAsync(s, 200, type, body, keep, req.Method == "HEAD");
            return keep;
        }

        private static async Task WriteAsync(Stream s, int code, string type, byte[] body, bool keep, bool head, string extra = "")
        {
            string reason = code switch { 200 => "OK", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
            string h = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n"
                     + $"Content-Security-Policy: {Csp}\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n"
                     + "Cache-Control: no-store\r\nPermissions-Policy: microphone=(self)\r\n"
                     + extra + (keep ? "Connection: keep-alive\r\n" : "Connection: close\r\n") + "\r\n";
            await s.WriteAsync(Encoding.ASCII.GetBytes(h));
            if (!head) await s.WriteAsync(body);
            await s.FlushAsync();
        }

        /// <summary>
        /// File della pagina: prima la cartella «web» nei dati del programma (pagine modificate da chi vuole),
        /// poi quelli incorporati. Solo nomi semplici: niente sottocartelle né «..».
        /// </summary>
        public static byte[]? ReadWebFile(string name)
        {
            if (name.Length == 0 || name.Length > 80 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') || name.Contains("..")) return null;
            try
            {
                string local = Path.Combine(AppSettings.Folder, "web", name);
                if (File.Exists(local)) return File.ReadAllBytes(local);
            }
            catch { }
            using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name);
            if (st == null) return null;
            using var ms = new MemoryStream();
            st.CopyTo(ms);
            return ms.ToArray();
        }

        // ================================================================ WebSocket e accesso

        private async Task WebSocketAsync(SslStream ssl, Request req, TcpClient tcp, IPEndPoint remote, IPEndPoint? local, string ip, CancellationToken ct)
        {
            WebSocket? ws = null;
            bool handed = false;
            try
            {
                string key = req.Header("sec-websocket-key");
                if (key.Length == 0) return;
                string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                string resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
                await ssl.WriteAsync(Encoding.ASCII.GetBytes(resp), ct);
                await ssl.FlushAsync(ct);
                ws = WebSocket.CreateFromStream(ssl, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });

                // troppi tentativi sbagliati da questo indirizzo: aspetta
                if (_fails.TryGetValue(ip, out var f) && f.until > DateTime.UtcNow)
                {
                    await SendTextAsync(ws, new { t = "denied", why = "Troppi tentativi sbagliati: riprova tra qualche minuto." }, ct);
                    return;
                }
                var lk = _loginKey();
                if (lk == null)
                {
                    await SendTextAsync(ws, new { t = "denied", why = "Il server sta preparando l'accesso: riprova tra qualche secondo." }, ct);
                    return;
                }
                string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                await SendTextAsync(ws, new { t = "challenge", nonce, call = _s.Callsign, tag = _serverTag() }, ct);
                string? text;
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(TimeSpan.FromSeconds(60));
                    text = await ReceiveTextAsync(ws, t.Token);
                }
                if (text == null) return;
                string mac = "", name = "";
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.GetProperty("t").GetString() != "login") return;
                    mac = doc.RootElement.GetProperty("mac").GetString() ?? "";
                    name = doc.RootElement.TryGetProperty("name", out var nn) ? (nn.GetString() ?? "") : "";
                }
                catch { return; }
                byte[] expected = Crypto.Hmac(lk, Encoding.UTF8.GetBytes("IP-TRX-CTRL web login|" + nonce));
                byte[] got;
                try { got = Convert.FromBase64String(mac); } catch { got = Array.Empty<byte>(); }
                if (got.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(got, expected))
                {
                    var cur = _fails.TryGetValue(ip, out var ff) ? ff.fails + 1 : 1;
                    _fails[ip] = (cur, cur >= 10 ? DateTime.UtcNow.AddMinutes(10) : DateTime.MinValue);
                    SessionLog.Write($"WEB accesso rifiutato da {ip}: codice sbagliato (tentativo {cur})");
                    await Task.Delay(1500, ct);
                    await SendTextAsync(ws, new { t = "denied", why = "Codice sbagliato." }, ct);
                    return;
                }
                _fails.TryRemove(ip, out _);
                name = new string(name.Where(c => !char.IsControl(c)).Take(40).ToArray()).Trim();
                if (name.Length == 0) name = "Browser";
                SessionLog.Write($"WEB accesso dal browser ({name}) da {ip}");
                await SendTextAsync(ws, new { t = "ok" }, ct);
                handed = true;
                var ch = new WebChannel(ws, tcp, ssl, remote, local);
                await _onSession(ch, name, ct);
            }
            catch { }
            finally
            {
                if (!handed)
                {
                    try { if (ws != null && ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                    try { ws?.Dispose(); } catch { }
                    try { ssl.Dispose(); } catch { }
                    try { tcp.Dispose(); } catch { }
                }
            }
        }

        private static async Task SendTextAsync(WebSocket ws, object o, CancellationToken ct)
        {
            byte[] b = JsonSerializer.SerializeToUtf8Bytes(o);
            await ws.SendAsync(b, WebSocketMessageType.Text, true, ct);
        }

        private static async Task<string?> ReceiveTextAsync(WebSocket ws, CancellationToken ct)
        {
            var buf = new byte[4096];
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 8192) return null;
                if (r.EndOfMessage) return r.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(ms.ToArray()) : null;
            }
        }

        // ================================================================ certificato

        /// <summary>
        /// Certificato del server (ECDSA P-256, 800 giorni: le regole di Apple chiedono al massimo 825). Salvato in
        /// «web-certificato.pfx» nella cartella dei dati, con la parola d'ordine nel deposito sicuro delle chiavi.
        /// </summary>
        private static X509Certificate2 LoadOrCreateCertificate(string callsign)
        {
            string path = Path.Combine(AppSettings.Folder, "web-certificato.pfx");
            byte[] pass = KeyStore.LoadMaster("web-cert") ?? NewPass();
            string pw = Convert.ToBase64String(pass);
            if (File.Exists(path))
            {
                try
                {
                    var c = new X509Certificate2(File.ReadAllBytes(path), pw, X509KeyStorageFlags.Exportable);
                    if (c.NotAfter > DateTime.Now.AddDays(30) && c.HasPrivateKey) return c;
                }
                catch { }
            }
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string cn = "IP-TRX-CTRL " + new string((callsign ?? "").Where(char.IsLetterOrDigit).Take(12).ToArray());
            var req = new CertificateRequest("CN=" + cn.Trim(), key, HashAlgorithmName.SHA256);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddIpAddress(IPAddress.Loopback);
            foreach (var ip in LocalAddresses()) san.AddIpAddress(ip);
            req.CertificateExtensions.Add(san.Build());
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            using var created = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(800));
            byte[] pfx = created.Export(X509ContentType.Pkcs12, pw);
            File.WriteAllBytes(path, pfx);
            SessionLog.Write($"WEB nuovo certificato del server creato ({cn}, valido 800 giorni)");
            return new X509Certificate2(pfx, pw, X509KeyStorageFlags.Exportable);
        }

        private static byte[] NewPass()
        {
            byte[] p = RandomNumberGenerator.GetBytes(32);
            KeyStore.SaveMaster(p, "web-cert");
            return p;
        }

        public static string FingerprintOf(X509Certificate2 c) =>
            string.Join(":", SHA256.HashData(c.RawData).Select(b => b.ToString("X2")));

        /// <summary>Indirizzi IPv4 di questo PC (rete locale e VPN).</summary>
        public static List<IPAddress> LocalAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address)) list.Add(ua.Address);
                }
            }
            catch { }
            return list.Distinct().ToList();
        }

        /// <summary>Indirizzi di VPN (Tailscale, WireGuard, ZeroTier…): rete 100.64.0.0/10 o nome dell'interfaccia.</summary>
        public static List<string> VpnAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string n = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    bool vpnName = n.Contains("tailscale") || n.Contains("wireguard") || n.Contains("zerotier") || n.StartsWith("wg") || n.Contains(" wg") || n.StartsWith("zt");
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = ua.Address.GetAddressBytes();
                        bool cgnatRange = b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                        if (vpnName || cgnatRange) list.Add(ua.Address.ToString());
                    }
                }
            }
            catch { }
            return list.Distinct().ToList();
        }

        /// <summary>Indirizzo di rete locale (192.168.x.x, 10.x.x.x, 172.16-31.x.x), escluse le VPN.</summary>
        public static string LanAddress()
        {
            var vpn = VpnAddresses();
            foreach (var ip in LocalAddresses())
            {
                byte[] b = ip.GetAddressBytes();
                bool priv = b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
                if (priv && !vpn.Contains(ip.ToString())) return ip.ToString();
            }
            return "";
        }
    }

    /// <summary>WebSocket di una pagina web collegata: controllo e audio insieme, messaggi binari.</summary>
    public sealed class WebChannel : IMsgChannel
    {
        private readonly WebSocket _ws;
        private readonly TcpClient _tcp;
        private readonly Stream _stream;
        private readonly SemaphoreSlim _send = new(1, 1);
        private int _pending;
        private int _disposed;

        public ChannelKind Kind => ChannelKind.Control;
        public IPEndPoint Remote { get; }
        public IPEndPoint? Local { get; }

        public WebChannel(WebSocket ws, TcpClient tcp, Stream stream, IPEndPoint remote, IPEndPoint? local)
        {
            _ws = ws; _tcp = tcp; _stream = stream;
            Remote = remote.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(remote.Address.MapToIPv4(), remote.Port) : remote;
            Local = local;
        }

        public async Task SendAsync(byte[] plain, CancellationToken ct = default)
        {
            // un browser lento non deve far accumulare l'audio: oltre 25 messaggi in attesa l'audio si scarta
            if (plain.Length > 0 && plain[0] == Msg.Audio && Volatile.Read(ref _pending) > 25) return;
            Interlocked.Increment(ref _pending);
            try
            {
                await _send.WaitAsync(ct);
                try { await _ws.SendAsync(plain, WebSocketMessageType.Binary, true, ct); }
                finally { _send.Release(); }
            }
            finally { Interlocked.Decrement(ref _pending); }
        }

        public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            using var ms = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult r;
                try { r = await _ws.ReceiveAsync(buf, ct); }
                catch { return null; }
                if (r.MessageType == WebSocketMessageType.Close) return null;
                ms.Write(buf, 0, r.Count);
                if (ms.Length > 256 * 1024) return null;
                if (!r.EndOfMessage) continue;
                if (r.MessageType != WebSocketMessageType.Binary) { ms.SetLength(0); continue; }
                return ms.ToArray();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _ws.Abort(); } catch { }
            try { _ws.Dispose(); } catch { }
            try { _stream.Dispose(); } catch { }
            try { _tcp.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Indirizzi del server per la pagina «Trova il server»: messaggio «retained» sui broker MQTT, cifrato con una
    /// chiave ricavata dal codice fisso. La pagina lo legge via MQTT su WebSocket sicuro con lo stesso codice.
    /// </summary>
    public static class WebRendezvous
    {
        public static string Topic(byte[] secret) =>
            "iptrxweb/" + Convert.ToHexString(Crypto.Hkdf(secret, Array.Empty<byte>(), "iptrx-web-topic", 16)).ToLowerInvariant();

        public static byte[] AddressKey(byte[] secret) => Crypto.Hkdf(secret, Array.Empty<byte>(), "iptrx-web-address");
        public static byte[] LoginKey(byte[] secret) => Crypto.Hkdf(secret, Array.Empty<byte>(), "iptrx-web-login");

        public static async Task<int> PublishAsync(byte[] secret, object info, IReadOnlyList<string> brokers, CancellationToken ct)
        {
            byte[] payload = Crypto.Seal(AddressKey(secret), JsonSerializer.SerializeToUtf8Bytes(info));
            string topic = Topic(secret);
            int ok = 0;
            foreach (string broker in brokers)
            {
                using var client = new MQTTnet.MqttFactory().CreateMqttClient();
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync(Mqtt.Options(broker, Mqtt.NewClientId()), t.Token);
                    await client.PublishAsync(Mqtt.Message(topic, payload, true), t.Token);
                    ok++;
                }
                catch { }
                finally { await Mqtt.SafeDisconnect(client); }
            }
            return ok;
        }
    }
}
