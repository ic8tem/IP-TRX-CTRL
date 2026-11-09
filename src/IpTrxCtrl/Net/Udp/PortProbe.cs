using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net.Udp
{
    /// <summary>
    /// Verifica che le porte aperte con UPnP siano davvero raggiungibili da internet.
    /// TCP: un servizio esterno prova a collegarsi alla porta (deve esserci un programma in ascolto).
    /// UDP: non esistono servizi pubblici affidabili; la verifica la fa l'altro PC con
    ///      <see cref="UdpLink.ProbeAsync"/> dopo lo scambio degli indirizzi sul broker MQTT.
    /// </summary>
    public static class PortProbe
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// true = raggiungibile, false = chiusa, null = verifica impossibile (servizio non disponibile).
        /// Usa l'API pubblica di ifconfig.co (/port/N), che si collega all'IP di chi chiede.
        /// </summary>
        public static async Task<bool?> TcpReachableAsync(int port, Action<string>? log, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"https://ifconfig.co/port/{port}");
                req.Headers.Accept.ParseAdd("application/json");
                req.Headers.UserAgent.ParseAdd("IP-TRX-CTRL");
                using var res = await Http.SendAsync(req, ct);
                string body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                bool ok = doc.RootElement.TryGetProperty("reachable", out var r) && r.GetBoolean();
                string ip = doc.RootElement.TryGetProperty("ip", out var i) ? i.GetString() ?? "" : "";
                log?.Invoke($"PORTE verifica esterna TCP {ip}:{port}: {(ok ? "raggiungibile" : "NON raggiungibile")}");
                return ok;
            }
            catch (Exception ex)
            {
                log?.Invoke($"PORTE verifica esterna TCP {port} non disponibile: {ex.Message}");
                return null;
            }
        }
    }
}
