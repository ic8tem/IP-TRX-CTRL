using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using IpTrxCtrl.Core;
using IpTrxCtrl.Platform;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Emulazione di un WinKeyer (protocollo WK2/WK3 di K1EL) sul client, dalla 0.3.7: idea di CAESAR (RA0SMS),
    /// che porta il WinKeyer del programma di log fino alla radio. N1MM, DXLog, TR4W, Win-Test, QLog e gli altri
    /// programmi aprono la porta come un WinKeyer vero; il testo che mandano diventa CW trasmesso dal server
    /// (keyer della radio o del server), alla velocità chiesta dal programma.
    /// Windows: una coppia di porte virtuali com0com (il programma di log sull'una, IP-TRX-CTRL sull'altra).
    /// Linux e macOS: con «virtuale» il programma crea uno pseudo-terminale e indica il percorso da usare.
    /// </summary>
    public sealed class WinKeyerEmu : IDisposable
    {
        private const byte Version = 23;   // risponde come un WinKeyer 2.3
        private readonly Node _node;
        private readonly AppSettings _s;
        private SerialPort? _port;
        private Pty? _pty;
        private Thread? _thread;
        private volatile bool _stop;
        private readonly StringBuilder _text = new();
        private DateTime _lastChar = DateTime.MinValue, _busyUntil = DateTime.MinValue;
        private int _wpm;
        private bool _open, _busySent;
        public string Where { get; private set; } = "";

        public WinKeyerEmu(Node node, AppSettings s) { _node = node; _s = s; _wpm = Math.Clamp(s.CwSpeedWpm, 5, 60); }

        public void Start()
        {
            string name = (_s.WinKeyerPort ?? "").Trim();
            if (!OperatingSystem.IsWindows() && (name.Length == 0 || name.Equals("virtuale", StringComparison.OrdinalIgnoreCase)))
            {
                _pty = new Pty();
                string link = Path.Combine(AppSettings.Folder, "winkeyer");
                try { try { File.Delete(link); } catch { } File.CreateSymbolicLink(link, _pty.SlavePath); Where = $"{link} → {_pty.SlavePath}"; }
                catch { Where = _pty.SlavePath; }
            }
            else
            {
                if (name.Length == 0) throw new InvalidOperationException("scegli la porta in Impostazioni › WinKeyer per i programmi di log");
                _port = new SerialPort(name, 1200, Parity.None, 8, StopBits.Two) { ReadTimeout = 200, WriteTimeout = 500 };
                _port.Open();
                Where = name;
            }
            SessionLog.Write("WINKEYER emulato su " + Where);
            _thread = new Thread(Loop) { IsBackground = true, Name = "WinKeyer" };
            _thread.Start();
        }

        private int ReadSome(byte[] buf)
        {
            if (_pty != null) return _pty.Read(buf, 100);
            try { return _port!.Read(buf, 0, buf.Length); } catch (TimeoutException) { return 0; }
        }

        private void Write(params byte[] b)
        {
            try { if (_pty != null) _pty.Write(b); else _port?.Write(b, 0, b.Length); } catch { }
        }

        private void Loop()
        {
            var buf = new byte[256];
            var pending = new System.Collections.Generic.List<byte>();
            while (!_stop)
            {
                int n;
                try { n = ReadSome(buf); } catch { Thread.Sleep(200); continue; }
                for (int i = 0; i < n; i++) pending.Add(buf[i]);
                int used;
                while (pending.Count > 0 && (used = Parse(pending)) > 0) pending.RemoveRange(0, used);
                Tick();
            }
        }

        /// <summary>Quanti byte occupa il comando in testa (0 = ne servono altri).</summary>
        private int Parse(System.Collections.Generic.List<byte> p)
        {
            byte c = p[0];
            if (c >= 0x20)
            {
                if (_open) { lock (_text) _text.Append((char)c); _lastChar = DateTime.UtcNow; }
                return 1;
            }
            int Need(int args) => p.Count >= 1 + args ? 1 + args : 0;
            switch (c)
            {
                case 0x00:   // comandi di amministrazione
                    if (p.Count < 2) return 0;
                    byte sub = p[1];
                    switch (sub)
                    {
                        case 0x02: _open = true; Write(Version); SessionLog.Write("WINKEYER aperto dal programma di log"); return 2;
                        case 0x03: _open = false; return 2;
                        case 0x04: if (p.Count < 3) return 0; Write(p[2]); return 3;   // prova dell'eco
                        case 0x05: case 0x06: Write(0); return 2;
                        case 0x07: Write(new byte[15]); return 2;
                        case 0x09: Write(0); return 2;
                        case 0x0C: Write(new byte[256]); return 2;
                        case 0x0D: return p.Count >= 258 ? 258 : 0;
                        case 0x00: case 0x0E: case 0x0F: case 0x16: case 0x19: return p.Count >= 3 ? 3 : 0;
                        case 0x13: return p.Count >= 4 ? 4 : 0;
                        case 0x15: Write(0x33); return 2;   // tensione (5 V)
                        case 0x17: Write(3); return 2;      // revisione minore
                        case 0x18: Write(1); return 2;      // tipo di integrato (SMT)
                        default: return 2;
                    }
                case 0x02:   // velocità
                    if (p.Count < 2) return 0;
                    if (p[1] >= 5 && p[1] <= 99) _wpm = Math.Min(60, (int)p[1]);
                    return 2;
                case 0x07: Write((byte)(0x80 | Math.Clamp(_wpm - 5, 0, 31))); return 1;   // potenziometro
                case 0x08: lock (_text) { if (_text.Length > 0) _text.Length--; } return 1;   // cancella l'ultimo carattere
                case 0x0A:   // svuota il buffer: ferma il CW
                    lock (_text) _text.Clear();
                    _busyUntil = DateTime.MinValue;
                    _node.StopMorse();
                    SendStatus(false);
                    return 1;
                case 0x15: SendStatus(DateTime.UtcNow < _busyUntil); return 1;
                case 0x04: case 0x1B: return Need(2);
                case 0x05: return Need(3);
                case 0x0F: return Need(15);
                case 0x16: if (p.Count < 2) return 0; return p[1] == 0 ? 2 : Need(2);
                case 0x13: case 0x1E: case 0x1F: return 1;
                case 0x1C:   // cambio di velocità nel buffer
                    if (p.Count < 2) return 0;
                    Flush();
                    if (p[1] >= 5 && p[1] <= 99) _wpm = Math.Min(60, (int)p[1]);
                    return 2;
                default:
                    return c <= 0x1F ? Need(1) : 1;   // gli altri comandi hanno un argomento (tono, peso, PTT, pausa…)
            }
        }

        private void Tick()
        {
            // il programma di log manda il messaggio tutto insieme: si spedisce dopo 150 ms senza caratteri nuovi
            if (DateTime.UtcNow - _lastChar > TimeSpan.FromMilliseconds(150)) Flush();
            if (_busySent && DateTime.UtcNow >= _busyUntil) SendStatus(false);
        }

        private void Flush()
        {
            string t;
            lock (_text) { t = _text.ToString(); _text.Clear(); }
            t = t.Trim();
            if (t.Length == 0) return;
            _node.SendMorse(t, _wpm);
            // durata stimata: circa 8,5 unità per carattere (PARIS), un'unità = 1200/WPM ms, più il ritardo di rete
            double ms = t.Length * 8.5 * 1200.0 / _wpm + 400;
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            if (until > _busyUntil) _busyUntil = until;
            SendStatus(true);
        }

        private void SendStatus(bool busy)
        {
            _busySent = busy;
            Write((byte)(0xC0 | (busy ? 0x04 : 0)));
        }

        public void Dispose()
        {
            _stop = true;
            try { _thread?.Join(500); } catch { }
            try { _port?.Close(); } catch { }
            _port?.Dispose();
            _pty?.Dispose();
            try { string link = Path.Combine(AppSettings.Folder, "winkeyer"); if (_pty != null) File.Delete(link); } catch { }
        }
    }
}
