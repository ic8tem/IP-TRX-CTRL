using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace IpTrxCtrl.Core
{
    /// <summary>Un collegamento del registro.</summary>
    public sealed class Qso
    {
        public DateTime OnUtc, OffUtc;
        public string Call = "", Mode = "", Band = "", RstSent = "", RstRcvd = "", Name = "", Qth = "", Grid = "", Comment = "", Operator = "", StationCall = "", MyGrid = "";
        public long FreqHz;

        public string Line => $"{OnUtc:yyyy-MM-dd HH:mm}  {Call,-10} {FreqHz / 1e6,10:F4} MHz {Mode,-5} {RstSent}/{RstRcvd}  {Name} {Grid}".TrimEnd();
    }

    /// <summary>
    /// Registro dei QSO (0.3.6): file ADIF nella cartella dei dati, esportazione per LoTW, QRZ e Club Log, e invio di
    /// ogni QSO ai programmi che ascoltano il protocollo UDP di WSJT-X (GridTracker, JTAlert, Log4OM, N1MM…),
    /// con gli stessi due messaggi che manda WSJT-X: «QSO Logged» (5) e «Logged ADIF» (12).
    /// </summary>
    public static class QsoLog
    {
        public static string FilePath => Path.Combine(AppSettings.Folder, "registro.adi");
        private static readonly object Lock = new();
        private static List<Qso>? _cache;
        public static event Action? Changed;

        public static IReadOnlyList<Qso> All()
        {
            lock (Lock)
            {
                if (_cache == null)
                {
                    try { _cache = File.Exists(FilePath) ? ParseAdif(File.ReadAllText(FilePath)) : new List<Qso>(); }
                    catch { _cache = new List<Qso>(); }
                }
                return _cache.ToList();
            }
        }

        public static void Add(Qso q, AppSettings s)
        {
            lock (Lock)
            {
                All();
                _cache!.Add(q);
                bool fresh = !File.Exists(FilePath);
                using (var w = new StreamWriter(FilePath, true, new UTF8Encoding(false)))
                {
                    if (fresh) w.Write(Header());
                    w.Write(Record(q));
                    w.Write('\n');
                }
            }
            SessionLog.Write($"REGISTRO QSO con {q.Call} {q.FreqHz / 1e6:F4} MHz {q.Mode} {q.RstSent}/{q.RstRcvd}");
            if (s.QsoUdpEnabled) Wsjtx.SendLogged(s.QsoUdpHost, s.QsoUdpPort, q);
            Changed?.Invoke();
        }

        public static void Export(string path)
        {
            var sb = new StringBuilder(Header());
            foreach (var q in All()) { sb.Append(Record(q)); sb.Append('\n'); }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string Header() =>
            $"IP-TRX-CTRL registro QSO\n{F("adif_ver", "3.1.4")}{F("programid", "IP-TRX-CTRL")}{F("programversion", AppVersion.Text)}<eoh>\n";

        private static string F(string name, string? v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return $"<{name}:{Encoding.UTF8.GetByteCount(v)}>{v} ";
        }

        public static string Record(Qso q)
        {
            var sb = new StringBuilder();
            sb.Append(F("call", q.Call));
            sb.Append(F("qso_date", q.OnUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
            sb.Append(F("time_on", q.OnUtc.ToString("HHmmss", CultureInfo.InvariantCulture)));
            sb.Append(F("qso_date_off", q.OffUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
            sb.Append(F("time_off", q.OffUtc.ToString("HHmmss", CultureInfo.InvariantCulture)));
            sb.Append(F("band", q.Band));
            sb.Append(F("freq", (q.FreqHz / 1e6).ToString("0.000000", CultureInfo.InvariantCulture)));
            var (mode, sub) = AdifMode(q.Mode);
            sb.Append(F("mode", mode));
            sb.Append(F("submode", sub));
            sb.Append(F("rst_sent", q.RstSent));
            sb.Append(F("rst_rcvd", q.RstRcvd));
            sb.Append(F("gridsquare", q.Grid));
            sb.Append(F("name", q.Name));
            sb.Append(F("qth", q.Qth));
            sb.Append(F("comment", q.Comment));
            sb.Append(F("operator", q.Operator));
            sb.Append(F("station_callsign", q.StationCall));
            sb.Append(F("my_gridsquare", q.MyGrid));
            sb.Append("<eor>");
            return sb.ToString();
        }

        /// <summary>Modi della radio (Hamlib) → modo ADIF: USB/LSB = SSB con sottomodo, CWR = CW, PKTUSB = DATA…</summary>
        public static (string mode, string sub) AdifMode(string m)
        {
            m = (m ?? "").ToUpperInvariant();
            return m switch
            {
                "USB" => ("SSB", "USB"),
                "LSB" => ("SSB", "LSB"),
                "CW" or "CWR" => ("CW", ""),
                "AM" or "SAM" or "AMS" => ("AM", ""),
                "FM" or "WFM" or "FMN" => ("FM", ""),
                "RTTY" or "RTTYR" => ("RTTY", ""),
                "PKTUSB" or "PKTLSB" or "PKTFM" or "USB-D" or "LSB-D" => ("DATA", ""),
                "" => ("", ""),
                _ => (m, "")
            };
        }

        /// <summary>Banda ADIF dalla frequenza.</summary>
        public static string BandOf(long hz)
        {
            double m = hz / 1e6;
            (double lo, double hi, string b)[] t =
            {
                (0.135, 0.138, "2190m"), (0.472, 0.479, "630m"), (1.8, 2.0, "160m"), (3.5, 4.0, "80m"), (5.06, 5.45, "60m"),
                (7.0, 7.3, "40m"), (10.1, 10.15, "30m"), (14.0, 14.35, "20m"), (18.068, 18.168, "17m"), (21.0, 21.45, "15m"),
                (24.89, 24.99, "12m"), (28.0, 29.7, "10m"), (40.0, 45.0, "8m"), (50.0, 54.0, "6m"), (70.0, 71.0, "4m"),
                (144.0, 148.0, "2m"), (222.0, 225.0, "1.25m"), (420.0, 450.0, "70cm"), (902.0, 928.0, "33cm"),
                (1240.0, 1300.0, "23cm"), (2300.0, 2450.0, "13cm"), (3300.0, 3500.0, "9cm"), (5650.0, 5925.0, "6cm"), (10000.0, 10500.0, "3cm")
            };
            foreach (var (lo, hi, b) in t) if (m >= lo && m <= hi) return b;
            return "";
        }

        public static List<Qso> ParseAdif(string text)
        {
            var res = new List<Qso>();
            int eoh = text.IndexOf("<eoh>", StringComparison.OrdinalIgnoreCase);
            if (eoh >= 0) text = text[(eoh + 5)..];
            var rec = new Dictionary<string, string>();
            var rx = new Regex(@"<([A-Za-z0-9_]+):(\d+)(?::[^>]*)?>", RegexOptions.Compiled);
            int i = 0;
            while (i < text.Length)
            {
                int lt = text.IndexOf('<', i);
                if (lt < 0) break;
                if (string.Compare(text, lt, "<eor>", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    res.Add(FromFields(rec));
                    rec = new Dictionary<string, string>();
                    i = lt + 5;
                    continue;
                }
                var m = rx.Match(text, lt);
                if (!m.Success || m.Index != lt) { i = lt + 1; continue; }
                int n = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int start = m.Index + m.Length;
                // la lunghezza ADIF è in byte: per i caratteri non ASCII si conta in UTF-8
                int end = start, bytes = 0;
                while (end < text.Length && bytes < n) { bytes += Encoding.UTF8.GetByteCount(text[end].ToString()); end++; }
                rec[m.Groups[1].Value.ToLowerInvariant()] = text[start..end];
                i = end;
            }
            return res;
        }

        private static Qso FromFields(Dictionary<string, string> f)
        {
            string G(string k) => f.TryGetValue(k, out var v) ? v.Trim() : "";
            DateTime D(string d, string t)
            {
                if (d.Length != 8) return DateTime.MinValue;
                t = (t + "000000")[..6];
                return DateTime.TryParseExact(d + t, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var x) ? x : DateTime.MinValue;
            }
            var q = new Qso
            {
                Call = G("call"), Band = G("band"), Mode = G("submode").Length > 0 ? G("submode") : G("mode"),
                RstSent = G("rst_sent"), RstRcvd = G("rst_rcvd"), Grid = G("gridsquare"), Name = G("name"), Qth = G("qth"),
                Comment = G("comment"), Operator = G("operator"), StationCall = G("station_callsign"), MyGrid = G("my_gridsquare"),
                OnUtc = D(G("qso_date"), G("time_on")),
            };
            q.OffUtc = D(G("qso_date_off").Length > 0 ? G("qso_date_off") : G("qso_date"), G("time_off"));
            if (q.OffUtc == DateTime.MinValue) q.OffUtc = q.OnUtc;
            if (double.TryParse(G("freq"), NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)) q.FreqHz = (long)Math.Round(mhz * 1e6);
            return q;
        }
    }

    /// <summary>Protocollo UDP di WSJT-X (NetworkMessage.hpp): QDataStream big endian, schema 2.</summary>
    public static class Wsjtx
    {
        private const uint Magic = 0xADBCCBDA;
        private const string Id = "IP-TRX-CTRL";

        public static void SendLogged(string host, int port, Qso q)
        {
            try
            {
                using var udp = new UdpClient();
                var m5 = QsoLogged(q);
                udp.Send(m5, m5.Length, host, port);
                var m12 = LoggedAdif(q);
                udp.Send(m12, m12.Length, host, port);
                SessionLog.Write($"REGISTRO QSO mandato a {host}:{port} (protocollo WSJT-X)");
            }
            catch (Exception ex) { SessionLog.Write($"REGISTRO invio a {host}:{port} non riuscito: {ex.Message}"); }
        }

        private sealed class W
        {
            private readonly MemoryStream _ms = new();
            public W U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); _ms.Write(b); return this; }
            public W I64(long v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, v); _ms.Write(b); return this; }
            public W U64(ulong v) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, v); _ms.Write(b); return this; }
            public W U8(byte v) { _ms.WriteByte(v); return this; }
            public W Utf8(string? s)
            {
                var b = Encoding.UTF8.GetBytes(s ?? "");
                U32((uint)b.Length);
                _ms.Write(b, 0, b.Length);
                return this;
            }
            /// <summary>QDateTime: giorno giuliano (qint64), millisecondi dalla mezzanotte (quint32), 1 = UTC.</summary>
            public W DateTimeUtc(DateTime t)
            {
                t = t.ToUniversalTime();
                long jd = (long)(t.Date - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays + 2440588;
                I64(jd);
                U32((uint)t.TimeOfDay.TotalMilliseconds);
                U8(1);
                return this;
            }
            public byte[] Build() => _ms.ToArray();
        }

        private static W Start(uint type) => new W().U32(Magic).U32(2).U32(type).Utf8(Id);

        public static byte[] QsoLogged(Qso q)
        {
            var (mode, _) = QsoLog.AdifMode(q.Mode);
            return Start(5)
                .DateTimeUtc(q.OffUtc).Utf8(q.Call).Utf8(q.Grid).U64((ulong)Math.Max(0, q.FreqHz)).Utf8(mode.Length > 0 ? mode : q.Mode)
                .Utf8(q.RstSent).Utf8(q.RstRcvd).Utf8("").Utf8(q.Comment).Utf8(q.Name).DateTimeUtc(q.OnUtc)
                .Utf8(q.Operator).Utf8(q.StationCall).Utf8(q.MyGrid).Utf8("").Utf8("").Utf8("")
                .Build();
        }

        public static byte[] LoggedAdif(Qso q) =>
            Start(12).Utf8($"\n{QsoLog.Header()}{QsoLog.Record(q)}\n").Build();
    }
}
