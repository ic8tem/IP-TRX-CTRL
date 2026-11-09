using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net
{
    /// <summary>Riconosce se il PC ha connettività IPv4 e/o IPv6 verso internet.</summary>
    public static class NetInfo
    {
        private static readonly HttpClient Http4 = MakeClient(AddressFamily.InterNetwork);
        private static readonly HttpClient Http6 = MakeClient(AddressFamily.InterNetworkV6);

        /// <summary>Client HTTP che si collega solo con la famiglia di indirizzi indicata.</summary>
        private static HttpClient MakeClient(AddressFamily family)
        {
            var h = new SocketsHttpHandler
            {
                ConnectCallback = async (ctx, ct) =>
                {
                    var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, family, ct);
                    if (addrs.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
                    var s = new Socket(family, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try { await s.ConnectAsync(addrs[0], ctx.DnsEndPoint.Port, ct); return new NetworkStream(s, true); }
                    catch { s.Dispose(); throw; }
                }
            };
            return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(6) };
        }

        /// <summary>Indirizzo IPv6 globale configurato su una scheda di rete attiva, se c'è.</summary>
        public static IPAddress? GlobalIpv6()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetworkV6 || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || IPAddress.IsLoopback(a) || a.IsIPv6Teredo) continue;
                        byte b0 = a.GetAddressBytes()[0];
                        if ((b0 & 0xFE) == 0xFC) continue;      // indirizzi locali unici fc00::/7
                        if ((b0 & 0xE0) == 0x20) return a;      // unicast globale 2000::/3
                    }
                }
            }
            catch { }
            return null;
        }

        private static async Task<string?> AskAsync(HttpClient c, string url, CancellationToken ct)
        {
            try { return (await c.GetStringAsync(url, ct)).Trim(); } catch { return null; }
        }

        /// <summary>Esempio: «IPv4 sì (93.41.127.18) · IPv6 sì (2a01:…)» oppure «IPv4 sì · IPv6 no».</summary>
        public static async Task<string> DescribeAsync(CancellationToken ct)
        {
            var t4 = AskAsync(Http4, "https://api.ipify.org", ct);
            var t6 = AskAsync(Http6, "https://api6.ipify.org", ct);
            await Task.WhenAll(t4, t6);
            string? v4 = t4.Result, v6 = t6.Result;
            var local6 = GlobalIpv6();
            string p4 = v4 != null && IPAddress.TryParse(v4, out _) ? $"IPv4 sì ({v4})" : "IPv4 no";
            string p6;
            if (v6 != null && IPAddress.TryParse(v6, out var a6)) p6 = $"IPv6 sì ({Short(a6)})";
            else if (local6 != null) p6 = $"IPv6 solo locale ({Short(local6)}, internet non raggiungibile)";
            else p6 = "IPv6 no";
            return p4 + " · " + p6;
        }

        private static string Short(IPAddress a)
        {
            string s = a.ToString();
            var parts = s.Split(':');
            return parts.Length > 3 ? string.Join(':', parts.Take(3)) + ":…" : s;
        }
    }
}
