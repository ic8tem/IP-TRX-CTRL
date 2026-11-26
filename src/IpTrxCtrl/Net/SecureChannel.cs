using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Net
{
    public enum ChannelKind : byte { Control = 1, Audio = 2 }

    /// <summary>
    /// Canale di messaggi tra server e operatore: TCP cifrato (programma client) oppure WebSocket su HTTPS
    /// (pagina web, dalla 0.3.1). Stessi messaggi binari in tutti e due i casi.
    /// </summary>
    public interface IMsgChannel : IDisposable
    {
        ChannelKind Kind { get; }
        IPEndPoint Remote { get; }
        IPEndPoint? Local { get; }
        Task SendAsync(byte[] plain, CancellationToken ct = default);
        /// <summary>Restituisce null quando la connessione si chiude.</summary>
        Task<byte[]?> ReceiveAsync(CancellationToken ct);
    }

    /// <summary>
    /// Canale TCP cifrato AES-256-GCM. Ogni frame: lunghezza(4, big endian) | nonce(12) | tag(16) | dati.
    /// Handshake di sessione autenticato con la chiave master (HMAC-SHA256) e chiave di sessione derivata con HKDF.
    /// </summary>
    public sealed class SecureChannel : IMsgChannel
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AAL1");
        private const byte KindPairing = 0, KindSession = 1, KindSessionPfs = 2;

        /// <summary>
        /// Dalla 0.3.8: abbinamenti il cui server ha già accettato, in questa esecuzione del programma, l'handshake
        /// con chiavi effimere; per loro niente ritorno a quello vecchio (un attacco non può togliere la segretezza futura).
        /// </summary>
        private static readonly HashSet<string> PfsPeers = new();
        private static bool PfsKnown(byte[] master) { lock (PfsPeers) return PfsPeers.Contains(Convert.ToHexString(Crypto.PairId(master))); }
        private static void SetPfsKnown(byte[] master) { lock (PfsPeers) PfsPeers.Add(Convert.ToHexString(Crypto.PairId(master))); }
        private const int MaxFrame = 64 * 1024;

        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly byte[] _key;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public ChannelKind Kind { get; }
        public IPEndPoint Remote { get; }
        public IPEndPoint? Local { get; }

        private SecureChannel(TcpClient tcp, byte[] key, ChannelKind kind)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
            _key = key;
            Kind = kind;
            Remote = (IPEndPoint)tcp.Client.RemoteEndPoint!;
            Local = tcp.Client.LocalEndPoint as IPEndPoint;
        }

        // ------------------------------------------------------------------ client

        public static async Task<SecureChannel> ConnectAsync(ServerAddress addr, ChannelKind kind, byte[] master, CancellationToken ct)
        {
            var tcp = new TcpClient(addr.Ip.AddressFamily);   // dalla 0.3.7 anche IPv6
            try
            {
                int port = kind == ChannelKind.Control ? addr.ControlPort : addr.AudioPort;
                using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    t.CancelAfter(TimeSpan.FromSeconds(6));
                    await tcp.ConnectAsync(addr.Ip, port, t.Token);
                }
                return await HandshakeAsInitiatorAsync(tcp, kind, master, ct, pfs: true);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException && !ct.IsCancellationRequested && !PfsKnown(master))
            {
                // server precedente alla 0.3.8: chiude la connessione al tipo di sessione nuovo; si riprova con quello di prima
                tcp.Dispose();
                SessionLog.Write($"SESSIONE {addr.Ip}: il server non conosce le chiavi effimere (versione precedente alla 0.3.8), uso l'handshake di prima");
                var tcp2 = new TcpClient(addr.Ip.AddressFamily);
                try
                {
                    int port = kind == ChannelKind.Control ? addr.ControlPort : addr.AudioPort;
                    using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        t.CancelAfter(TimeSpan.FromSeconds(6));
                        await tcp2.ConnectAsync(addr.Ip, port, t.Token);
                    }
                    return await HandshakeAsInitiatorAsync(tcp2, kind, master, ct, pfs: false);
                }
                catch { tcp2.Dispose(); throw; }
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Handshake di sessione dal lato «client» su una connessione TCP già aperta. Serve anche al
        /// collegamento inverso: il server si collega alle porte aperte dal client, ma il client resta
        /// chi avvia l'handshake.
        /// </summary>
        public static async Task<SecureChannel> HandshakeAsInitiatorAsync(TcpClient tcp, ChannelKind kind, byte[] master, CancellationToken ct, bool? pfs = null)
        {
            bool usePfs = pfs ?? PfsKnown(master);
            try
            {
                var s = tcp.GetStream();
                using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
                hs.CancelAfter(TimeSpan.FromSeconds(10));
                ct = hs.Token;

                byte[] nC = RandomNumberGenerator.GetBytes(16);
                if (usePfs)
                {
                    // 0.3.8: ECDH P-256 effimero autenticato con la chiave dell'abbinamento. Chi rubasse un giorno la
                    // chiave dell'abbinamento non potrebbe decifrare le sessioni registrate prima.
                    using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                    byte[] pubC = ecdh.ExportSubjectPublicKeyInfo();
                    await s.WriteAsync(Crypto.Concat(Magic, new[] { KindSessionPfs, (byte)kind }, Crypto.PairId(master), nC, U16(pubC.Length), pubC), ct);
                    byte[] nS2 = await ReadExactAsync(s, 16, ct);
                    int lenS = ToU16(await ReadExactAsync(s, 2, ct));
                    if (lenS <= 0 || lenS > 512) throw new CryptographicException("Risposta di sessione non valida");
                    byte[] pubS = await ReadExactAsync(s, lenS, ct);
                    byte[] macS2 = await ReadExactAsync(s, 32, ct);
                    if (!Crypto.Equal(macS2, Crypto.Hmac(master, Encoding.ASCII.GetBytes("S2"), new[] { (byte)kind }, nC, nS2, pubC, pubS)))
                        throw new CryptographicException("Il server non conosce la chiave di abbinamento");
                    await s.WriteAsync(Crypto.Hmac(master, Encoding.ASCII.GetBytes("C2"), new[] { (byte)kind }, nC, nS2, pubC, pubS), ct);
                    using var other = ECDiffieHellman.Create();
                    other.ImportSubjectPublicKeyInfo(pubS, out _);
                    byte[] shared = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
                    byte[] key2 = Crypto.Hkdf(Crypto.Concat(shared, master), Crypto.Concat(nC, nS2), "aal-session2-" + (byte)kind);
                    CryptographicOperations.ZeroMemory(shared);
                    var ch2 = new SecureChannel(tcp, key2, kind);
                    SetPfsKnown(master);
                    SessionLog.Write($"SESSIONE client->{ch2.Remote} canale={kind} chiavi effimere (segretezza futura)");
                    return ch2;
                }
                await s.WriteAsync(Crypto.Concat(Magic, new[] { KindSession, (byte)kind }, Crypto.PairId(master), nC), ct);

                byte[] reply = await ReadExactAsync(s, 16 + 32, ct);
                byte[] nS = reply.AsSpan(0, 16).ToArray();
                byte[] macS = reply.AsSpan(16, 32).ToArray();
                if (!Crypto.Equal(macS, Crypto.Hmac(master, Encoding.ASCII.GetBytes("S"), new[] { (byte)kind }, nC, nS)))
                    throw new CryptographicException("Il server non conosce la chiave di abbinamento");
                await s.WriteAsync(Crypto.Hmac(master, Encoding.ASCII.GetBytes("C"), new[] { (byte)kind }, nC, nS), ct);

                byte[] key = Crypto.Hkdf(master, Crypto.Concat(nC, nS), "aal-session-" + (byte)kind);
                var ch = new SecureChannel(tcp, key, kind);
                SessionLog.Write($"SESSIONE client->{ch.Remote} canale={kind} nC={Convert.ToHexString(nC)} nS={Convert.ToHexString(nS)}");
                return ch;
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        /// <summary>Abbinamento lato client: ECDH P-256 autenticato con il segreto del codice. Restituisce la chiave master.</summary>
        public static async Task<byte[]> PairAsClientAsync(ServerAddress addr, byte[] secret, CancellationToken ct, int connectSeconds = 8)
        {
            using var tcp = new TcpClient();
            using (var t = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                t.CancelAfter(TimeSpan.FromSeconds(connectSeconds));
                await tcp.ConnectAsync(addr.Ip, addr.ControlPort, t.Token);
            }
            return await PairAsClientOnAsync(tcp, secret, ct);
        }

        /// <summary>
        /// Abbinamento lato client su una connessione già aperta: serve anche al collegamento inverso durante
        /// l'abbinamento (il server si collega alla porta del client, ma il client avvia comunque lo scambio).
        /// </summary>
        public static async Task<byte[]> PairAsClientOnAsync(TcpClient tcp, byte[] secret, CancellationToken ct)
        {
            var remote = tcp.Client.RemoteEndPoint as IPEndPoint;
            var s = tcp.GetStream();
            using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hs.CancelAfter(TimeSpan.FromSeconds(15));
            ct = hs.Token;

            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] pubC = ecdh.ExportSubjectPublicKeyInfo();
            byte[] macC = Crypto.Hmac(secret, Encoding.ASCII.GetBytes("PC"), pubC);
            await s.WriteAsync(Crypto.Concat(Magic, new[] { KindPairing, (byte)ChannelKind.Control }, U16(pubC.Length), pubC, macC), ct);

            int lenS = ToU16(await ReadExactAsync(s, 2, ct));
            if (lenS <= 0 || lenS > 512) throw new CryptographicException("Risposta di abbinamento non valida");
            byte[] pubS = await ReadExactAsync(s, lenS, ct);
            byte[] macS = await ReadExactAsync(s, 32, ct);
            if (!Crypto.Equal(macS, Crypto.Hmac(secret, Encoding.ASCII.GetBytes("PS"), pubS, pubC)))
                throw new CryptographicException("Codice di abbinamento errato");

            using var other = ECDiffieHellman.Create();
            other.ImportSubjectPublicKeyInfo(pubS, out _);
            byte[] shared = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
            byte[] master = Crypto.Hkdf(shared, secret, "aal-master");
            SessionLog.Write($"ABBINAMENTO client con {remote?.Address.MapToIPv4()} impronta={Crypto.Fingerprint(master)}");
            return master;
        }

        // ------------------------------------------------------------------ server

        public sealed class AcceptResult
        {
            public SecureChannel? Channel;
            public byte[]? NewMaster;   // valorizzato se la connessione era un abbinamento
            public byte[]? Master;      // chiave del client che ha aperto la sessione (server con più client)
            public byte[]? PairingSecret; // segreto usato nell'abbinamento (numero temporaneo o codice fisso)
        }

        public static Task<AcceptResult> AcceptAsync(TcpClient tcp, ChannelKind expected, byte[]? master, byte[]? pairingSecret, CancellationToken ct) =>
            AcceptAsync(tcp, expected,
                master == null ? null : new Func<byte[], byte[]?>(pid => Crypto.Equal(pid, Crypto.PairId(master)) ? master : null),
                pairingSecret == null ? null : (IReadOnlyList<byte[]>)new[] { pairingSecret }, ct);

        /// <summary>
        /// Gestisce una connessione in arrivo: abbinamento (se pairingSecret != null) oppure sessione autenticata.
        /// </summary>
        /// <param name="masterFor">dato l'identificativo dell'abbinamento mandato dal client, la sua chiave (null se sconosciuto)</param>
        /// <param name="pairingSecrets">segreti di abbinamento accettati in questo momento (numero temporaneo, codice fisso)</param>
        public static async Task<AcceptResult> AcceptAsync(TcpClient tcp, ChannelKind expected, Func<byte[], byte[]?>? masterFor,
            IReadOnlyList<byte[]>? pairingSecrets, CancellationToken ct)
        {
            var s = tcp.GetStream();
            using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hs.CancelAfter(TimeSpan.FromSeconds(10));
            ct = hs.Token;
            try
            {
                byte[] head = await ReadExactAsync(s, 6, ct);
                if (!Crypto.Equal(head.AsSpan(0, 4).ToArray(), Magic)) throw new IOException("Protocollo sconosciuto");
                byte kindByte = head[4];
                var kind = (ChannelKind)head[5];
                if (kind != expected) throw new IOException("Canale errato");

                if (kindByte == KindPairing)
                {
                    if (pairingSecrets == null || pairingSecrets.Count == 0 || kind != ChannelKind.Control) throw new IOException("Abbinamento non attivo");
                    if (CodeGuard.Locked) throw new IOException("Abbinamento sospeso: troppi codici sbagliati");
                    int lenC = ToU16(await ReadExactAsync(s, 2, ct));
                    if (lenC <= 0 || lenC > 512) throw new IOException("Abbinamento non valido");
                    byte[] pubC = await ReadExactAsync(s, lenC, ct);
                    byte[] macC = await ReadExactAsync(s, 32, ct);
                    byte[]? pairingSecret = null;
                    foreach (var cand in pairingSecrets)
                        if (Crypto.Equal(macC, Crypto.Hmac(cand, Encoding.ASCII.GetBytes("PC"), pubC))) { pairingSecret = cand; break; }
                    if (pairingSecret == null)
                    {
                        CodeGuard.Fail("abbinamento da " + tcp.Client.RemoteEndPoint);
                        throw new CryptographicException("Codice di abbinamento errato");
                    }

                    using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                    byte[] pubS = ecdh.ExportSubjectPublicKeyInfo();
                    byte[] macS = Crypto.Hmac(pairingSecret, Encoding.ASCII.GetBytes("PS"), pubS, pubC);
                    await s.WriteAsync(Crypto.Concat(U16(pubS.Length), pubS, macS), ct);

                    using var other = ECDiffieHellman.Create();
                    other.ImportSubjectPublicKeyInfo(pubC, out _);
                    byte[] shared = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
                    byte[] newMaster = Crypto.Hkdf(shared, pairingSecret, "aal-master");
                    SessionLog.Write($"ABBINAMENTO server con {tcp.Client.RemoteEndPoint} impronta={Crypto.Fingerprint(newMaster)}");
                    tcp.Dispose();
                    return new AcceptResult { NewMaster = newMaster, PairingSecret = pairingSecret };
                }

                if (kindByte == KindSessionPfs && masterFor != null)
                {
                    byte[] rest2 = await ReadExactAsync(s, 8 + 16 + 2, ct);
                    byte[] pairId2 = rest2.AsSpan(0, 8).ToArray();
                    byte[] nC2 = rest2.AsSpan(8, 16).ToArray();
                    int lenC = (rest2[24] << 8) | rest2[25];
                    if (lenC <= 0 || lenC > 512) throw new IOException("Sessione non valida");
                    byte[] pubC = await ReadExactAsync(s, lenC, ct);
                    byte[] master2 = masterFor(pairId2) ?? throw new CryptographicException("Peer non abbinato");
                    using var other = ECDiffieHellman.Create();
                    other.ImportSubjectPublicKeyInfo(pubC, out _);
                    using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                    byte[] pubS = ecdh.ExportSubjectPublicKeyInfo();
                    byte[] nS2 = RandomNumberGenerator.GetBytes(16);
                    await s.WriteAsync(Crypto.Concat(nS2, U16(pubS.Length), pubS,
                        Crypto.Hmac(master2, Encoding.ASCII.GetBytes("S2"), new[] { (byte)kind }, nC2, nS2, pubC, pubS)), ct);
                    byte[] macC3 = await ReadExactAsync(s, 32, ct);
                    if (!Crypto.Equal(macC3, Crypto.Hmac(master2, Encoding.ASCII.GetBytes("C2"), new[] { (byte)kind }, nC2, nS2, pubC, pubS)))
                        throw new CryptographicException("Autenticazione del client fallita");
                    byte[] shared = ecdh.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256);
                    byte[] key2 = Crypto.Hkdf(Crypto.Concat(shared, master2), Crypto.Concat(nC2, nS2), "aal-session2-" + (byte)kind);
                    CryptographicOperations.ZeroMemory(shared);
                    var ch2 = new SecureChannel(tcp, key2, kind);
                    SessionLog.Write($"SESSIONE server<-{ch2.Remote} canale={kind} chiavi effimere (segretezza futura)");
                    return new AcceptResult { Channel = ch2, Master = master2 };
                }

                if (kindByte != KindSession || masterFor == null) throw new IOException("Sessione non valida");
                byte[] rest = await ReadExactAsync(s, 8 + 16, ct);
                byte[] pairId = rest.AsSpan(0, 8).ToArray();
                byte[] nC = rest.AsSpan(8, 16).ToArray();
                byte[] master = masterFor(pairId) ?? throw new CryptographicException("Peer non abbinato");

                byte[] nS = RandomNumberGenerator.GetBytes(16);
                await s.WriteAsync(Crypto.Concat(nS, Crypto.Hmac(master, Encoding.ASCII.GetBytes("S"), new[] { (byte)kind }, nC, nS)), ct);
                byte[] macC2 = await ReadExactAsync(s, 32, ct);
                if (!Crypto.Equal(macC2, Crypto.Hmac(master, Encoding.ASCII.GetBytes("C"), new[] { (byte)kind }, nC, nS)))
                    throw new CryptographicException("Autenticazione del client fallita");

                byte[] key = Crypto.Hkdf(master, Crypto.Concat(nC, nS), "aal-session-" + (byte)kind);
                var ch = new SecureChannel(tcp, key, kind);
                SessionLog.Write($"SESSIONE server<-{ch.Remote} canale={kind} nC={Convert.ToHexString(nC)} nS={Convert.ToHexString(nS)}");
                return new AcceptResult { Channel = ch, Master = master };
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        // ------------------------------------------------------------------ frames

        public async Task SendAsync(byte[] plain, CancellationToken ct = default)
        {
            byte[] sealedData = Crypto.Seal(_key, plain);
            byte[] packet = Crypto.Concat(U32(sealedData.Length), sealedData);
            await _writeLock.WaitAsync(ct);
            try { await _stream.WriteAsync(packet, ct); }
            finally { _writeLock.Release(); }
        }

        /// <summary>Restituisce null quando la connessione si chiude.</summary>
        public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
        {
            byte[]? lenBuf = await ReadExactOrEofAsync(_stream, 4, ct);
            if (lenBuf == null) return null;
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len < Crypto.NonceSize + Crypto.TagSize || len > MaxFrame) throw new IOException("Frame non valido");
            byte[]? data = await ReadExactOrEofAsync(_stream, len, ct);
            if (data == null) return null;
            return Crypto.Open(_key, data);
        }

        public void Dispose()
        {
            try { _tcp.Close(); } catch { }
            _tcp.Dispose();
        }

        // ------------------------------------------------------------------ util

        private static byte[] U16(int v) => new[] { (byte)(v >> 8), (byte)v };
        private static int ToU16(byte[] b) => (b[0] << 8) | b[1];
        private static byte[] U32(int v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
            => await ReadExactOrEofAsync(s, count, ct) ?? throw new IOException("Connessione chiusa");

        private static async Task<byte[]?> ReadExactOrEofAsync(Stream s, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
                if (n == 0) return null;
                read += n;
            }
            return buf;
        }
    }

    /// <summary>
    /// Dalla 0.3.8: limite su tutti i tentativi di codice insieme (abbinamento e pagina web), oltre a quello per
    /// indirizzo. Chi provasse codici da molti indirizzi diversi viene fermato: dopo 30 errori in 10 minuti i
    /// codici non si accettano per 15 minuti (i client già abbinati si collegano come sempre).
    /// </summary>
    public static class CodeGuard
    {
        private static readonly System.Collections.Generic.Queue<DateTime> Fails = new();
        private static DateTime _lockedUntil = DateTime.MinValue;
        private const int MaxFails = 30;
        public static event Action<string>? LockedNow;

        public static bool Locked { get { lock (Fails) return DateTime.UtcNow < _lockedUntil; } }

        public static void Fail(string where)
        {
            bool nowLocked = false;
            lock (Fails)
            {
                var now = DateTime.UtcNow;
                Fails.Enqueue(now);
                while (Fails.Count > 0 && now - Fails.Peek() > TimeSpan.FromMinutes(10)) Fails.Dequeue();
                if (Fails.Count >= MaxFails && now >= _lockedUntil)
                {
                    _lockedUntil = now.AddMinutes(15);
                    Fails.Clear();
                    nowLocked = true;
                }
            }
            if (nowLocked)
            {
                string msg = $"Troppi codici sbagliati ({MaxFails} in 10 minuti, ultimo: {where}): abbinamenti e accessi dal browser sospesi per 15 minuti. I client già abbinati si collegano normalmente.";
                SessionLog.Write("SICUREZZA " + msg);
                LockedNow?.Invoke(msg);
            }
        }
    }
}
