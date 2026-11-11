using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Assistenza: rapporto per segnalare un problema (registri con gli indirizzi IP mascherati) e controllo
    /// di una versione nuova sulla pagina delle release di GitHub.
    /// </summary>
    public static class Support
    {
        public const string Repo = "ic8tem/IP-TRX-CTRL";
        public const string IssuesUrl = "https://github.com/" + Repo + "/issues/new";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

        // ================================================================ rapporto

        private static readonly Regex Ipv4 = new(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])", RegexOptions.Compiled);
        // IPv6: almeno tre gruppi esadecimali separati da «:» (anche abbreviati con «::»)
        private static readonly Regex Ipv6 = new(@"(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{1,4}:){2,7}(?::|[0-9A-Fa-f]{1,4})(?:%\w+)?(?![0-9A-Fa-f:])|(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{1,4}:){1,6}:(?:[0-9A-Fa-f]{1,4}:?){1,6}(?![0-9A-Fa-f:])", RegexOptions.Compiled);

        /// <summary>
        /// Maschera gli indirizzi IP pubblici: ognuno diventa «[IP-1]», «[IP-2]»… (lo stesso indirizzo ha sempre lo
        /// stesso nome, così il rapporto resta leggibile). Restano visibili solo gli indirizzi privati e locali
        /// (192.168.x.x, 10.x.x.x, 172.16-31.x.x, 100.64-127.x.x del CGNAT, 127.x.x.x, 169.254.x.x), che da
        /// internet non portano a nessuno. Gli IPv6 sono tutti mascherati.
        /// </summary>
        public static string MaskIps(string text)
        {
            var names = new Dictionary<string, string>();
            string Name(string ip, string prefix)
            {
                if (!names.TryGetValue(ip, out var n)) { n = $"[{prefix}-{names.Count + 1}]"; names[ip] = n; }
                return n;
            }
            text = Ipv4.Replace(text, m =>
            {
                var o = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    if (!int.TryParse(m.Groups[i + 1].Value, out o[i]) || o[i] > 255) return m.Value;   // numero di versione o altro
                }
                bool priv = o[0] == 10 || o[0] == 127 || (o[0] == 192 && o[1] == 168) || (o[0] == 172 && o[1] >= 16 && o[1] <= 31)
                            || (o[0] == 100 && o[1] >= 64 && o[1] <= 127) || (o[0] == 169 && o[1] == 254) || o[0] == 0;
                return priv ? m.Value : Name(m.Value, "IP");
            });
            text = Ipv6.Replace(text, m =>
            {
                string v = m.Value;
                if (v.Count(c => c == ':') < 2 || !IPAddress.TryParse(v.Split('%')[0], out var a)) return v;   // orari e simili
                if (IPAddress.IsLoopback(a)) return v;
                return Name(v, "IPv6");
            });
            return text;
        }

        private static int Count(this string s, Func<char, bool> f) { int n = 0; foreach (char c in s) if (f(c)) n++; return n; }

        private static string Tail(string path, int maxBytes)
        {
            try
            {
                if (!File.Exists(path)) return "(file assente)\n";
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long start = Math.Max(0, fs.Length - maxBytes);
                fs.Seek(start, SeekOrigin.Begin);
                using var r = new StreamReader(fs, Encoding.UTF8);
                string t = r.ReadToEnd();
                if (start > 0)
                {
                    int nl = t.IndexOf('\n');
                    t = $"(… solo l'ultima parte: {maxBytes / 1024} kB …)\n" + (nl >= 0 ? t[(nl + 1)..] : t);
                }
                return t;
            }
            catch (Exception ex) { return $"(non leggibile: {ex.Message})\n"; }
        }

        /// <summary>Rapporto completo da allegare a una segnalazione (Issue su GitHub), con gli IP pubblici mascherati.</summary>
        public static string BuildReport(AppSettings s, string extra)
        {
            var sb = new StringBuilder();
            sb.AppendLine("IP-TRX-CTRL · RAPPORTO PER SEGNALARE UN PROBLEMA");
            sb.AppendLine("Gli indirizzi IP pubblici sono sostituiti da [IP-1], [IP-2]… (lo stesso indirizzo ha sempre lo stesso nome).");
            sb.AppendLine();
            sb.AppendLine($"Versione: {AppVersion.Text} (protocollo {AppVersion.Protocol})");
            sb.AppendLine($"Sistema: {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture} · .NET {Environment.Version}");
            sb.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm} (ora locale) · {DateTime.UtcNow:yyyy-MM-dd HH:mm}Z");
            sb.AppendLine($"Ruolo: {(s.Role == NodeRole.Server ? "server" : "client")} · nominativo {s.Callsign}");
            if (s.Role == NodeRole.Server)
            {
                sb.AppendLine($"Radio: {(s.CatViaTci ? $"CAT via TCI {s.TciHost}:{s.TciPort}" : s.UseExternalRigctld ? $"rigctld esterno {s.ExternalRigctldHost}:{s.ExternalRigctldPort}" : $"{s.RigModelName} (modello {s.RigModel}) su {s.RigCom} a {s.RigBaud}")}");
                sb.AppendLine($"PTT: {s.PttMethod} {s.PttCom} · ritardo {s.PttDelayMs} ms · CW: {s.CwMethod} {s.CwCom}");
                sb.AppendLine($"Sicurezza: ROS {(s.SwrProtection ? $"{s.SwrWarn}/{s.SwrTrip}" : "spenta")} · tempo massimo {(s.TxTimeoutEnabled ? s.TxTimeoutSec + " s" : "spento")} · blocco QSY in TX {(s.QsyLockInTx ? "sì" : "no")} · precedenza locale {(s.LocalPriority ? s.LocalPriorityMin + " min" : "no")}");
                sb.AppendLine($"Panadapter: {(s.PanEnabled ? (s.CatViaTci ? "TCI" : $"Icom CI-V {s.PanCom} {s.PanBaud} indirizzo {s.PanCivAddr}") : "spento")}");
                sb.AppendLine($"Client abbinati: {s.Clients.Count} · codice fisso {(s.FixedCodeEnabled ? "attivo" : "spento")}");
            }
            else sb.AppendLine($"Server abbinati: {s.Servers.Count} · programmi digitali {(s.DigitalEnabled ? "sì" : "no")} · porta seriale {(s.InputCom.Length > 0 ? s.InputCom : "nessuna")}");
            sb.AppendLine($"Audio: ingresso «{s.InputDevice}» · uscita «{s.OutputDevice}» · pre-riempimento {s.PrefillMs} ms");
            sb.AppendLine($"Broker MQTT: {string.Join(", ", s.MqttBrokers)}");
            if (!string.IsNullOrWhiteSpace(extra)) { sb.AppendLine(); sb.AppendLine("DESCRIZIONE DEL PROBLEMA"); sb.AppendLine(extra.Trim()); }
            sb.AppendLine();
            sb.AppendLine("==================== errori.log ====================");
            sb.Append(Tail(Path.Combine(AppSettings.Folder, "errori.log"), 200 * 1024));
            sb.AppendLine();
            sb.AppendLine("==================== sessioni.log ====================");
            sb.Append(Tail(SessionLog.FilePath, 400 * 1024));
            return MaskIps(sb.ToString());
        }

        // ================================================================ versione nuova

        /// <summary>
        /// Ultima release pubblicata su GitHub (api.github.com, senza account). Restituisce la versione («0.3.1») se è
        /// più nuova di questa, altrimenti null. GitHub vede l'indirizzo IP di chi chiede, come per qualunque pagina web.
        /// </summary>
        public static async Task<string?> CheckNewerAsync(CancellationToken ct)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"IP-TRX-CTRL/{AppVersion.Text}");
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
                using var doc = JsonDocument.Parse(json);
                string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                string v = tag.TrimStart('v', 'V').Trim();
                return IsNewer(v, AppVersion.Text) ? v : null;
            }
            catch (Exception ex)
            {
                SessionLog.Write("AGGIORNAMENTI controllo non riuscito: " + ex.Message);
                return null;
            }
        }

        public static bool IsNewer(string candidate, string current)
        {
            static int[] P(string v)
            {
                var r = new int[3];
                var parts = v.Split('.', '-', '+');
                for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out r[i]);
                return r;
            }
            if (candidate.Length == 0) return false;
            var a = P(candidate); var b = P(current);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }
    }
}
