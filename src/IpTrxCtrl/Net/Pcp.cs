using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net
{
    /// <summary>
    /// PCP, Port Control Protocol (RFC 6887), dalla 0.3.7: idea di rigcontrol di K6BP, che apre un varco nel firewall
    /// con NAT-PMP o PCP. Qui serve soprattutto per IPv6: senza NAT ogni PC ha un indirizzo pubblico, ma il router
    /// di solito blocca le connessioni in arrivo; una richiesta MAP al router apre la porta per questo PC.
    /// Funziona con i router che hanno PCP (FRITZ!Box e molti altri); altrimenti il collegamento resta su IPv4.
    /// </summary>
    public static class Pcp
    {
        public const int ServerPort = 5351;

        public sealed record Result(bool Ok, int ResultCode, IPAddress? ExternalIp, int ExternalPort, int Lifetime);

        /// <summary>Indirizzo IPv6 globale stabile (non temporaneo, se il sistema lo dice) e router IPv6 della stessa scheda.</summary>
        public static (IPAddress? addr, IPAddress? gateway) StableIpv6()
        {
            IPAddress? best = null, bestGw = null, any = null, anyGw = null;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    var gw = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6 && !a.Equals(IPAddress.IPv6Any));
                    if (gw != null && gw.IsIPv6LinkLocal && gw.ScopeId == 0)
                    {
                        try { gw = new IPAddress(gw.GetAddressBytes(), props.GetIPv6Properties().Index); } catch { }
                    }
                    foreach (var ua in props.UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetworkV6 || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || IPAddress.IsLoopback(a) || a.IsIPv6Teredo) continue;
                        byte b0 = a.GetAddressBytes()[0];
                        if ((b0 & 0xE0) != 0x20) continue;   // solo unicast globale 2000::/3
                        bool temporary = false;
                        try { temporary = ua.SuffixOrigin == SuffixOrigin.Random; } catch { }
                        if (!temporary && best == null) { best = a; bestGw = gw; }
                        if (any == null) { any = a; anyGw = gw; }
                    }
                }
            }
            catch { }
            return best != null ? (best, bestGw) : (any, anyGw);
        }

        /// <summary>
        /// Richiesta MAP: apre (lifetime &gt; 0) o chiude (0) la porta interna verso l'esterno. Per IPv6 il router
        /// risponde con la stessa porta e lo stesso indirizzo (nessuna traduzione).
        /// </summary>
        public static async Task<Result> MapAsync(IPAddress gateway, IPAddress client, int internalPort, bool tcp, int lifetimeSeconds, CancellationToken ct)
        {
            byte[] req = new byte[60];
            req[0] = 2;            // versione
            req[1] = 1;            // MAP, richiesta
            WriteU32(req, 4, (uint)lifetimeSeconds);
            client16(client).CopyTo(req, 8);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            nonce.CopyTo(req, 24);
            req[36] = (byte)(tcp ? 6 : 17);
            req[40] = (byte)(internalPort >> 8); req[41] = (byte)internalPort;
            req[42] = (byte)(internalPort >> 8); req[43] = (byte)internalPort;   // porta esterna suggerita: la stessa
            // indirizzo esterno suggerito: tutto zero (per IPv4 ::ffff:0.0.0.0)
            if (client.AddressFamily == AddressFamily.InterNetwork) { req[54] = 0xFF; req[55] = 0xFF; }

            using var udp = new UdpClient(gateway.AddressFamily);
            int wait = 250;
            for (int attempt = 0; attempt < 4; attempt++)   // ritrasmissioni come da RFC (250 ms, poi il doppio)
            {
                ct.ThrowIfCancellationRequested();
                await udp.SendAsync(req, req.Length, new IPEndPoint(gateway, ServerPort));
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(wait);
                try
                {
                    while (true)
                    {
                        var r = await udp.ReceiveAsync(t.Token);
                        var b = r.Buffer;
                        if (b.Length < 60 || b[0] != 2 || b[1] != 0x81) continue;
                        if (!b.AsSpan(24, 12).SequenceEqual(nonce)) continue;
                        int code = b[3];
                        uint life = ReadU32(b, 4);
                        int extPort = (b[42] << 8) | b[43];
                        var ext = new IPAddress(b.AsSpan(44, 16));
                        if (ext.IsIPv4MappedToIPv6) ext = ext.MapToIPv4();
                        return new Result(code == 0, code, ext, extPort, (int)life);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (SocketException) { return new Result(false, -1, null, 0, 0); }   // porta 5351 chiusa: niente PCP
                wait *= 2;
            }
            return new Result(false, -1, null, 0, 0);
        }

        public static string Describe(int code) => code switch
        {
            -1 => "il router non risponde a PCP",
            0 => "ok",
            1 => "versione non supportata",
            2 => "non autorizzato (PCP disattivato o vietato per questo PC nel router)",
            3 => "richiesta non valida",
            7 => "il router non può aprire la porta (in uso o politica)",
            8 => "risorse esaurite sul router",
            11 => "nessuna risorsa libera",
            _ => "codice " + code
        };

        private static byte[] client16(IPAddress a) => (a.AddressFamily == AddressFamily.InterNetwork ? a.MapToIPv6() : a).GetAddressBytes();
        private static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        private static uint ReadU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    }
}
