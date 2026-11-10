using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Net.Udp
{
    /// <summary>
    /// Client: quali porte TCP riesce a raggiungere in USCITA? Molte reti (aziende, alberghi, Wi-Fi pubblici,
    /// alcune reti mobili) lasciano uscire solo poche porte, di solito 80 e 443.
    /// Si usa portquiz.net, che risponde su tutte le porte TCP (tranne 21, 22 e 25) con una pagina web:
    /// la porta passa solo se arriva davvero la sua pagina (un proxy o un portale che intercetta non basta).
    /// </summary>
    public static class OutboundProbe
    {
        /// <summary>Porte che il server può chiedere al router come porte esterne, in ordine di preferenza.</summary>
        public static readonly int[] Candidates = { 443, 80, 8443, 8080, 993, 587, 465, 1935 };
        /// <summary>Porte alte qualsiasi: se passano, la rete non filtra e non serve nulla di speciale.</summary>
        private static readonly int[] HighPorts = { 47123, 51234 };

        public sealed record Result(bool Known, bool Restricted, List<int> Allowed)
        {
            public string Describe() => !Known ? "non verificabile (portquiz.net non risponde)"
                : !Restricted ? "nessun filtro: escono anche le porte alte"
                : Allowed.Count == 0 ? "filtrata: non esce nessuna delle porte provate"
                : "filtrata: escono solo le porte " + string.Join(", ", Allowed);
        }

        public static async Task<Result> RunAsync(Action<string>? log, CancellationToken ct)
        {
            var ports = Candidates.Concat(HighPorts).ToArray();
            var tasks = ports.Select(p => TryAsync(p, ct)).ToArray();
            await Task.WhenAll(tasks);
            var ok = ports.Where((p, i) => tasks[i].Result).ToList();
            Result r;
            if (ok.Count == 0) r = new Result(false, false, new List<int>());
            else
            {
                bool highOk = HighPorts.All(ok.Contains);
                r = new Result(true, !highOk, Candidates.Where(ok.Contains).ToList());
            }
            log?.Invoke("PORTE uscita del client: " + r.Describe());
            return r;
        }

        private static async Task<bool> TryAsync(int port, CancellationToken ct)
        {
            using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
            t.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var tcp = new TcpClient(AddressFamily.InterNetwork);
                await tcp.ConnectAsync("portquiz.net", port, t.Token);
                var s = tcp.GetStream();
                byte[] req = Encoding.ASCII.GetBytes($"GET / HTTP/1.0\r\nHost: portquiz.net:{port}\r\nUser-Agent: IP-TRX-CTRL\r\nConnection: close\r\n\r\n");
                await s.WriteAsync(req, t.Token);
                var buf = new byte[4096];
                var sb = new StringBuilder();
                while (sb.Length < 16384)
                {
                    int n = await s.ReadAsync(buf, t.Token);
                    if (n <= 0) break;
                    sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                    if (sb.ToString().Contains("Outgoing Port Tester", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return false; }
        }
    }
}
