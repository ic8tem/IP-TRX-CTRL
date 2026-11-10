using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net.Udp
{
    /// <summary>
    /// Verifica che le porte aperte siano davvero raggiungibili da internet.
    /// TCP: due servizi esterni indipendenti provano a collegarsi alla porta (deve esserci un programma in ascolto):
    ///   - ifconfig.co (echoip, MIT)         GET /port/N            → JSON {"ip","port","reachable"} · attesa 2 s
    ///   - portchecker.io (GPL-3, open source) GET /api/me/N        → testo "True"/"False" · attesa 1 s
    /// Si interrogano insieme: basta un «sì» per dire raggiungibile; «no» solo se nessuno dice sì.
    /// Le richieste partono sempre in IPv4, perché le porte del router sono IPv4.
    /// UDP: non esistono servizi pubblici affidabili; lo verifica l'altro PC con la perforazione del NAT.
    /// </summary>
    public static class PortProbe
    {
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var addrs = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, AddressFamily.InterNetwork, ct);
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { await s.ConnectAsync(addrs, ctx.DnsEndPoint.Port, ct); return new NetworkStream(s, true); }
                catch { s.Dispose(); throw; }
            }
        })
        { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>true = raggiungibile, false = chiusa, null = verifica impossibile (servizi non disponibili).</summary>
        public static async Task<bool?> TcpReachableAsync(int port, Action<string>? log, CancellationToken ct)
        {
            var a = IfconfigAsync(port, log, ct);
            var b = PortcheckerAsync(port, log, ct);
            await Task.WhenAll(a, b);
            bool? ra = a.Result, rb = b.Result;
            bool? res = ra == true || rb == true ? true : (ra == false || rb == false ? false : null);
            log?.Invoke($"PORTE verifica esterna TCP {port}: " + res switch
            {
                true => "RAGGIUNGIBILE",
                false => "NON raggiungibile",
                _ => "non verificabile (servizi esterni non disponibili): la considero buona"
            } + $" (ifconfig.co {Txt(ra)}, portchecker.io {Txt(rb)})");
            return res;
        }

        private static string Txt(bool? r) => r == true ? "sì" : r == false ? "no" : "non risponde";

        private static async Task<bool?> IfconfigAsync(int port, Action<string>? log, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://ifconfig.co/port/{port}");
                req.Headers.Accept.ParseAdd("application/json");
                req.Headers.UserAgent.ParseAdd("IP-TRX-CTRL");
                using var res = await Http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) { log?.Invoke($"PORTE ifconfig.co: risposta {(int)res.StatusCode}"); return null; }
                string body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("reachable", out var r)) return null;
                string ip = doc.RootElement.TryGetProperty("ip", out var i) ? i.GetString() ?? "" : "";
                log?.Invoke($"PORTE ifconfig.co ha provato {ip}:{port}: {(r.GetBoolean() ? "sì" : "no")}");
                return r.GetBoolean();
            }
            catch (Exception ex)
            {
                log?.Invoke($"PORTE ifconfig.co non disponibile: {ex.Message}");
                return null;
            }
        }

        private static async Task<bool?> PortcheckerAsync(int port, Action<string>? log, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://portchecker.io/api/me/{port}");
                req.Headers.UserAgent.ParseAdd("IP-TRX-CTRL");
                using var res = await Http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) { log?.Invoke($"PORTE portchecker.io: risposta {(int)res.StatusCode}"); return null; }
                string body = (await res.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
                bool? r = body.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
                        : body.Equals("false", StringComparison.OrdinalIgnoreCase) ? false : null;
                log?.Invoke($"PORTE portchecker.io ha provato la porta {port}: {Txt(r)}");
                return r;
            }
            catch (Exception ex)
            {
                log?.Invoke($"PORTE portchecker.io non disponibile: {ex.Message}");
                return null;
            }
        }
    }
}
