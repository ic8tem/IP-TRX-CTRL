using System;
using System.Reflection;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Versione del programma e del protocollo tra server e client.
    /// Il PROTOCOLLO cambia solo quando server e client devono essere aggiornati insieme:
    ///   1 = 0.1.0 – 0.1.3
    ///   2 = 0.2.0 e successive
    /// Dentro lo stesso protocollo le versioni si parlano; alcune funzioni richiedono una versione minima
    /// su entrambi i PC (vedi <see cref="Notes"/>).
    /// Formato scambiato (abbinamento MQTT, indirizzo sul broker, saluto sul canale cifrato): «P2 0.2.3».
    /// </summary>
    public static class AppVersion
    {
        public const int Protocol = 2;

        public static string Text
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
        }

        public static string Tag => $"P{Protocol} {Text}";

        /// <summary>Legge «P2 0.2.3». Le versioni fino alla 0.2.2 non mandano nulla (o «1.0»): protocollo 2, versione ignota.</summary>
        public static (int proto, string version) Parse(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("P")) return (2, "");
            var parts = tag.Trim().Split(' ', 2);
            int p = int.TryParse(parts[0].AsSpan(1), out int n) ? n : 0;
            return (p, parts.Length > 1 ? parts[1].Trim() : "");
        }

        public static string Show(string version) => version.Length > 0 ? version : "0.2.2 o precedente";

        public static bool Compatible(int peerProto) => peerProto == Protocol;

        private static bool AtLeast(string v, int a, int b, int c)
        {
            if (v.Length == 0) return false;
            var p = v.Split('.');
            int x = p.Length > 0 && int.TryParse(p[0], out var i0) ? i0 : 0;
            int y = p.Length > 1 && int.TryParse(p[1], out var i1) ? i1 : 0;
            int z = p.Length > 2 && int.TryParse(p[2], out var i2) ? i2 : 0;
            return (x, y, z).CompareTo((a, b, c)) >= 0;
        }

        /// <summary>Testo di compatibilità con l'altro PC («" se è tutto a posto).</summary>
        public static string Notes(int peerProto, string peerVersion, bool peerIsServer)
        {
            string other = peerIsServer ? "il server" : "il client";
            if (!Compatible(peerProto))
                return $"{other} usa la versione {Show(peerVersion)} (protocollo {peerProto}), questo PC la {Text} (protocollo {Protocol}): " +
                       "non sono compatibili. Aggiorna tutti e due i PC alla stessa versione.";
            if (peerVersion.Length == 0)
                return $"{other} usa una versione precedente alla 0.2.3: funziona, ma non dice la sua versione. " +
                       "Se è la 0.2.0 mancano la scelta delle schede audio del server (0.2.1) e il collegamento sulla rete locale (0.2.2). Conviene aggiornarlo.";
            if (!AtLeast(peerVersion, 0, 2, 2))
                return $"{other} usa la {peerVersion}: funziona, ma mancano alcune funzioni nuove. Conviene aggiornarlo alla {Text}.";
            return "";
        }
    }
}
