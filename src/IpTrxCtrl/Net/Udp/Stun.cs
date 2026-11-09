using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net.Udp
{
    /// <summary>Risultato della scoperta dell'indirizzo pubblico con STUN.</summary>
    public sealed record NatInfo(IPEndPoint? Public, string Type, int PortDelta)
    {
        public bool Symmetric => PortDelta != 0;
    }

    /// <summary>
    /// STUN (RFC 5389), solo la richiesta Binding: chiede a un server pubblico con quale IP e porta
    /// lo vede internet. Interrogando due server dallo stesso socket si capisce il tipo di NAT:
    /// stessa porta = NAT "a cono" (perforabile); porte diverse = NAT simmetrico (serve la previsione
    /// della porta o una porta UDP aperta con UPnP sul server).
    /// Provato nel laboratorio prove/udp-lab (peer.py, discover).
    /// </summary>
    public static class Stun
    {
        public const uint Magic = 0x2112A442;

        public static readonly string[] DefaultServers =
        {
            "stun.l.google.com:19302", "stun1.l.google.com:19302", "stun.cloudflare.com:3478"
        };

        public static bool IsStun(ReadOnlySpan<byte> d) =>
            d.Length >= 20 && d[0] < 2 && (uint)(d[4] << 24 | d[5] << 16 | d[6] << 8 | d[7]) == Magic;

        public static (byte[] packet, byte[] tid) Request()
        {
            byte[] tid = RandomNumberGenerator.GetBytes(12);
            byte[] p = new byte[20];
            p[0] = 0x00; p[1] = 0x01;                      // Binding Request, lunghezza 0
            p[4] = 0x21; p[5] = 0x12; p[6] = 0xA4; p[7] = 0x42;
            tid.CopyTo(p, 8);
            return (p, tid);
        }

        /// <summary>XOR-MAPPED-ADDRESS (IPv4) della risposta, o null.</summary>
        public static IPEndPoint? Parse(ReadOnlySpan<byte> d, ReadOnlySpan<byte> tid)
        {
            if (d.Length < 20 || d[0] != 0x01 || d[1] != 0x01 || !d.Slice(8, 12).SequenceEqual(tid)) return null;
            int len = d[2] << 8 | d[3];
            int pos = 20;
            while (pos + 4 <= Math.Min(d.Length, 20 + len))
            {
                int type = d[pos] << 8 | d[pos + 1], alen = d[pos + 2] << 8 | d[pos + 3];
                var v = d.Slice(pos + 4, Math.Min(alen, d.Length - pos - 4));
                if (type == 0x0020 && alen >= 8 && v[1] == 0x01)
                {
                    int port = (v[2] << 8 | v[3]) ^ (int)(Magic >> 16);
                    uint ip = (uint)(v[4] << 24 | v[5] << 16 | v[6] << 8 | v[7]) ^ Magic;
                    var bytes = new[] { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip };
                    return new IPEndPoint(new IPAddress(bytes), port);
                }
                pos += 4 + alen + ((4 - alen % 4) % 4);
            }
            return null;
        }

        public static async Task<IPEndPoint?> ResolveAsync(string server, CancellationToken ct)
        {
            string host = server; int port = 3478;
            int c = server.LastIndexOf(':');
            if (c > 0 && int.TryParse(server[(c + 1)..], out int p)) { host = server[..c]; port = p; }
            try
            {
                var a = (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)).FirstOrDefault();
                return a == null ? null : new IPEndPoint(a, port);
            }
            catch { return null; }
        }

        /// <summary>
        /// Interroga due server STUN dallo stesso socket del collegamento. Le risposte arrivano dal ciclo
        /// di ricezione di <see cref="UdpLink"/>, che le consegna con <see cref="UdpLink.StunAsync"/>.
        /// </summary>
        public static async Task<NatInfo> DiscoverAsync(UdpLink link, IReadOnlyList<string> servers, Action<string>? log, CancellationToken ct)
        {
            var found = new List<IPEndPoint>();
            foreach (string s in servers)
            {
                if (found.Count == 2) break;
                var ep = await ResolveAsync(s, ct);
                if (ep == null) { log?.Invoke($"STUN {s}: nome non risolto"); continue; }
                var r = await link.StunAsync(ep, ct);
                log?.Invoke(r == null ? $"STUN {s}: nessuna risposta" : $"STUN {s}: visto come {r}");
                if (r != null) found.Add(r);
            }
            if (found.Count == 0) return new NatInfo(null, "UDP bloccato o STUN non raggiungibile", 0);
            if (found.Count == 1) return new NatInfo(found[0], "sconosciuto (un solo server STUN)", 0);
            if (found[0].Equals(found[1])) return new NatInfo(found[0], "a cono (mappatura stabile)", 0);
            int delta = found[1].Port - found[0].Port;
            return new NatInfo(found[1], $"simmetrico (porte {found[0].Port} / {found[1].Port})", delta == 0 ? 1 : delta);
        }
    }
}
