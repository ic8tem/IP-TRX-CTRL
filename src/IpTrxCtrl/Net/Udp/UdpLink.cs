using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Net.Udp
{
    /// <summary>
    /// Collegamento UDP cifrato tra server e client, con perforazione del NAT (hole punching).
    /// Traduzione del prototipo provato in prove/udp-lab/peer.py.
    ///
    /// Datagramma: 0xA5 | nonce(12) | AES-256-GCM( tipo | dati ) | tag(16)
    ///   PUNCH / PUNCH_ACK  perforazione (l'ACK riporta l'identificativo di chi ha bussato)
    ///   AUDIO              seq(u32) | PCM 16 bit 8 kHz, senza ritrasmissione
    ///   CTRL / ACK         seq(u32) | messaggio, consegna garantita (ritrasmissione 200 ms × 1,5ⁿ, 12 tentativi)
    ///   KEEPALIVE          ogni 15 s, tiene aperta la mappatura del NAT
    ///   PROBE / PROBE_ACK  verifica dall'esterno di una porta UDP aperta con UPnP
    /// </summary>
    public sealed class UdpLink : IDisposable
    {
        private const byte Mark = 0xA5;
        public const byte Punch = 1, PunchAck = 2, Audio = 0x10, Ctrl = 0x20, Ack = 0x21, KeepAlive = 0x30, Probe = 0x40, ProbeAck = 0x41;

        private readonly UdpClient _udp;
        private readonly AesGcm _aes;
        private readonly byte[] _id = RandomNumberGenerator.GetBytes(8);
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IPEndPoint?>> _stunWaits = new();
        private readonly ConcurrentDictionary<uint, (byte[] data, int tries, DateTime next)> _pending = new();
        private readonly HashSet<uint> _ctrlSeen = new();
        private readonly HashSet<IPEndPoint> _candidates = new();
        private readonly object _lock = new();
        private uint _audioSeq, _ctrlSeq;
        private DateTime _lastKeepAlive = DateTime.UtcNow;
        private TaskCompletionSource<IPEndPoint> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _probeWaits = new();

        public IPEndPoint? Remote { get; private set; }
        public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        public long Retransmits;
        public event Action<string>? Log;
        public event Action<uint, byte[]>? AudioReceived;     // seq, PCM
        public event Action<byte[]>? ControlReceived;          // messaggio già senza duplicati

        public UdpLink(byte[] master, int localPort = 0)
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
            if (OperatingSystem.IsWindows())
            {
                // senza questo, un ICMP "porta irraggiungibile" chiude il socket UDP su Windows
                const int SIO_UDP_CONNRESET = -1744830452;
                try { _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null); } catch { }
            }
            _aes = new AesGcm(Crypto.Hkdf(master, Array.Empty<byte>(), "iptrx-udp"), 16);
            _ = Task.Run(ReceiveLoopAsync);
            _ = Task.Run(MaintainLoopAsync);
        }

        private void L(string s) => Log?.Invoke("UDP " + s);

        // ------------------------------------------------------------------ cifratura
        private byte[] Seal(byte type, ReadOnlySpan<byte> payload)
        {
            byte[] outp = new byte[1 + 12 + 1 + payload.Length + 16];
            outp[0] = Mark;
            var nonce = outp.AsSpan(1, 12);
            RandomNumberGenerator.Fill(nonce);
            byte[] plain = new byte[1 + payload.Length];
            plain[0] = type;
            payload.CopyTo(plain.AsSpan(1));
            _aes.Encrypt(nonce, plain, outp.AsSpan(13, plain.Length), outp.AsSpan(13 + plain.Length, 16));
            return outp;
        }

        private bool TryOpen(byte[] d, out byte type, out byte[] payload)
        {
            type = 0; payload = Array.Empty<byte>();
            if (d.Length < 1 + 12 + 1 + 16 || d[0] != Mark) return false;
            int n = d.Length - 13 - 16;
            byte[] plain = new byte[n];
            try { _aes.Decrypt(d.AsSpan(1, 12), d.AsSpan(13, n), d.AsSpan(13 + n, 16), plain); }
            catch (CryptographicException) { return false; }      // estraneo o falsificato
            type = plain[0];
            payload = plain.AsSpan(1).ToArray();
            return true;
        }

        private void Send(byte type, ReadOnlySpan<byte> payload, IPEndPoint? to = null)
        {
            to ??= Remote;
            if (to == null) return;
            byte[] d = Seal(type, payload);
            try { _udp.Send(d, d.Length, to); } catch { }
        }

        // ------------------------------------------------------------------ ricezione
        private async Task ReceiveLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await _udp.ReceiveAsync(_cts.Token); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                byte[] d = r.Buffer;
                var src = r.RemoteEndPoint;

                if (Stun.IsStun(d))
                {
                    foreach (var kv in _stunWaits)
                    {
                        var ep = Stun.Parse(d, Convert.FromHexString(kv.Key));
                        if (ep != null) kv.Value.TrySetResult(ep);
                    }
                    continue;
                }
                if (!TryOpen(d, out byte type, out byte[] p)) continue;

                switch (type)
                {
                    case Punch:
                        bool added;
                        lock (_lock) added = _candidates.Add(src);
                        if (added) L($"PUNCH da indirizzo nuovo {src}: aggiunto ai candidati");
                        Send(PunchAck, p.Take(8).Concat(_id).ToArray(), src);
                        if (Remote == null) Select(src, "PUNCH ricevuto");
                        break;
                    case PunchAck:
                        if (p.Length >= 8 && p.AsSpan(0, 8).SequenceEqual(_id) && Remote == null) Select(src, "PUNCH_ACK ricevuto");
                        break;
                    case Audio:
                        if (p.Length >= 4) AudioReceived?.Invoke((uint)(p[0] << 24 | p[1] << 16 | p[2] << 8 | p[3]), p.AsSpan(4).ToArray());
                        break;
                    case Ctrl:
                        if (p.Length < 4) break;
                        uint seq = (uint)(p[0] << 24 | p[1] << 16 | p[2] << 8 | p[3]);
                        Send(Ack, p.AsSpan(0, 4), src);
                        bool fresh;
                        lock (_ctrlSeen) { fresh = _ctrlSeen.Add(seq); if (_ctrlSeen.Count > 4096) _ctrlSeen.Clear(); }
                        if (fresh) ControlReceived?.Invoke(p.AsSpan(4).ToArray());
                        break;
                    case Ack:
                        if (p.Length >= 4) _pending.TryRemove((uint)(p[0] << 24 | p[1] << 16 | p[2] << 8 | p[3]), out _);
                        break;
                    case Probe:
                        Send(ProbeAck, p, src);
                        L($"PROBE ricevuto da {src}: la porta {LocalPort} è raggiungibile da internet");
                        break;
                    case ProbeAck:
                        if (_probeWaits.TryRemove(Convert.ToHexString(p), out var w)) w.TrySetResult(true);
                        break;
                }
            }
        }

        private void Select(IPEndPoint ep, string why)
        {
            Remote = ep;
            L($"collegato a {ep} ({why})");
            _connected.TrySetResult(ep);
        }

        // ------------------------------------------------------------------ STUN
        public async Task<IPEndPoint?> StunAsync(IPEndPoint server, CancellationToken ct, int tries = 3)
        {
            for (int i = 0; i < tries; i++)
            {
                var (pkt, tid) = Stun.Request();
                string key = Convert.ToHexString(tid);
                var tcs = new TaskCompletionSource<IPEndPoint?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stunWaits[key] = tcs;
                try
                {
                    await _udp.SendAsync(pkt, pkt.Length, server);
                    var done = await Task.WhenAny(tcs.Task, Task.Delay(1000, ct));
                    if (done == tcs.Task) return tcs.Task.Result;
                }
                catch (OperationCanceledException) { return null; }
                catch (SocketException) { }
                finally { _stunWaits.TryRemove(key, out _); }
            }
            return null;
        }

        // ------------------------------------------------------------------ perforazione
        /// <summary>
        /// Bussa verso tutti i candidati ogni 50 ms finché uno risponde (al massimo timeout).
        /// remoteDelta ≠ 0: l'altro NAT è simmetrico, si provano anche le porte previste.
        /// </summary>
        public async Task<bool> PunchAsync(IEnumerable<IPEndPoint> remote, int remoteDelta, TimeSpan timeout, CancellationToken ct)
        {
            var list = remote.ToList();
            lock (_lock)
            {
                foreach (var ep in list) _candidates.Add(ep);
                if (remoteDelta != 0 && list.Count > 0)
                {
                    int step = Math.Abs(remoteDelta) < 16 ? remoteDelta : 1;
                    for (int k = 1; k <= 32; k++)
                    {
                        int port = list[0].Port + step * k;
                        if (port > 0 && port < 65536) _candidates.Add(new IPEndPoint(list[0].Address, port));
                    }
                    L($"previsione porta: provo 32 porte da {list[0].Port + step} con passo {step}");
                }
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Remote == null && sw.Elapsed < timeout && !ct.IsCancellationRequested)
            {
                IPEndPoint[] eps;
                lock (_lock) eps = _candidates.ToArray();
                foreach (var ep in eps) Send(Punch, _id, ep);
                try { await Task.Delay(50, ct); } catch { break; }
            }
            if (Remote != null) { L($"perforazione riuscita in {sw.Elapsed.TotalSeconds:F2} s"); return true; }
            L($"perforazione non riuscita dopo {timeout.TotalSeconds:F0} s");
            return false;
        }

        // ------------------------------------------------------------------ verifica di una porta
        /// <summary>Manda un PROBE a un indirizzo e attende la risposta: la porta è raggiungibile da qui.</summary>
        public async Task<bool> ProbeAsync(IPEndPoint target, CancellationToken ct, int tries = 5)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(8);
            string key = Convert.ToHexString(nonce);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _probeWaits[key] = tcs;
            try
            {
                for (int i = 0; i < tries && !ct.IsCancellationRequested; i++)
                {
                    Send(Probe, nonce, target);
                    if (await Task.WhenAny(tcs.Task, Task.Delay(400, ct)) == tcs.Task) return true;
                }
                return false;
            }
            catch (OperationCanceledException) { return false; }
            finally { _probeWaits.TryRemove(key, out _); }
        }

        // ------------------------------------------------------------------ canale
        public void SendAudio(ReadOnlySpan<byte> pcm)
        {
            uint seq = ++_audioSeq;
            byte[] p = new byte[4 + pcm.Length];
            p[0] = (byte)(seq >> 24); p[1] = (byte)(seq >> 16); p[2] = (byte)(seq >> 8); p[3] = (byte)seq;
            pcm.CopyTo(p.AsSpan(4));
            Send(Audio, p);
        }

        public void SendControl(ReadOnlySpan<byte> message)
        {
            uint seq = Interlocked.Increment(ref _ctrlSeq);
            byte[] p = new byte[4 + message.Length];
            p[0] = (byte)(seq >> 24); p[1] = (byte)(seq >> 16); p[2] = (byte)(seq >> 8); p[3] = (byte)seq;
            message.CopyTo(p.AsSpan(4));
            _pending[seq] = (p, 0, DateTime.UtcNow.AddMilliseconds(200));
            Send(Ctrl, p);
        }

        private async Task MaintainLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(20, _cts.Token); } catch { break; }
                var now = DateTime.UtcNow;
                foreach (var kv in _pending)
                {
                    var (data, tries, next) = kv.Value;
                    if (now < next) continue;
                    if (tries >= 12) { _pending.TryRemove(kv.Key, out _); L($"messaggio di controllo {kv.Key} perso dopo 12 tentativi"); continue; }
                    _pending[kv.Key] = (data, tries + 1, now.AddMilliseconds(200 * Math.Pow(1.5, tries + 1)));
                    Interlocked.Increment(ref Retransmits);
                    Send(Ctrl, data);
                }
                if (Remote != null && now - _lastKeepAlive > TimeSpan.FromSeconds(15))
                {
                    _lastKeepAlive = now;
                    Send(KeepAlive, ReadOnlySpan<byte>.Empty);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _udp.Dispose(); } catch { }
            _aes.Dispose();
        }
    }
}
