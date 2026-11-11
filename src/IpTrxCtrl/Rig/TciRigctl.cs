using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// «rigctld» locale (127.0.0.1) che traduce i comandi Hamlib nel TCI. Lo usano sia IP-TRX-CTRL sia i
    /// programmi digitali del client (tramite il tunnel), quindi con il TCI tutto funziona come con Hamlib:
    /// frequenze, modo, split, potenza, S-meter, potenza in uscita, ROS (e la protezione ROS), testo CW.
    /// I comandi che non riguardano la radio (\dump_state, \chk_vfo…) passano a un rigctld «Dummy»
    /// (modello 1) avviato a parte, che risponde nel formato che i programmi Hamlib si aspettano.
    /// </summary>
    public sealed class TciRigctl : IDisposable
    {
        private const string Unavailable = "RPRT -11\n";   // funzione non disponibile
        private const string Ok = "RPRT 0\n";

        private static readonly HashSet<string> VfoNames = new(StringComparer.OrdinalIgnoreCase)
        { "VFOA", "VFOB", "VFOC", "currVFO", "VFO", "MEM", "Main", "Sub", "TX", "RX", "MainA", "MainB", "SubA", "SubB" };

        private static readonly Dictionary<string, string> LongNames = new()
        {
            ["\\get_freq"] = "f", ["\\set_freq"] = "F", ["\\get_mode"] = "m", ["\\set_mode"] = "M",
            ["\\get_ptt"] = "t", ["\\set_ptt"] = "T", ["\\get_split_vfo"] = "s", ["\\set_split_vfo"] = "S",
            ["\\get_split_freq"] = "i", ["\\set_split_freq"] = "I", ["\\get_level"] = "l", ["\\set_level"] = "L",
            ["\\send_morse"] = "b", ["\\vfo_op"] = "G", ["\\get_func"] = "u", ["\\set_func"] = "U", ["\\quit"] = "q"
        };

        private readonly TciLink _link;
        private readonly int _dummyPort;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, TcpClient> _conns = new();
        private int _nextId;

        public int Port { get; }

        /// <param name="dummyPort">porta del rigctld «Dummy» per i comandi non legati alla radio (0 = assente)</param>
        public TciRigctl(TciLink link, int dummyPort)
        {
            _link = link;
            _dummyPort = dummyPort;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient tcp;
                try { tcp = await _listener.AcceptTcpClientAsync(_cts.Token); } catch { break; }
                tcp.NoDelay = true;
                int id = Interlocked.Increment(ref _nextId);
                _conns[id] = tcp;
                _ = Task.Run(() => ServeAsync(id, tcp));
            }
        }

        /// <summary>Una connessione: risposte locali per la radio, il resto al rigctld «Dummy».</summary>
        private async Task ServeAsync(int id, TcpClient tcp)
        {
            var ct = _cts.Token;
            var stream = tcp.GetStream();
            var writeLock = new SemaphoreSlim(1, 1);
            TcpClient? dummy = null;
            NetworkStream? dummyStream = null;
            async Task Write(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                await writeLock.WaitAsync(ct);
                try { await stream.WriteAsync(b, ct); } finally { writeLock.Release(); }
            }
            try
            {
                using var reader = new StreamReader(stream, Encoding.ASCII);
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    if (line.Trim().Length == 0) continue;
                    string? answer = await AnswerAsync(line.Trim());
                    if (answer == "q") break;
                    if (answer != null) { await Write(answer); continue; }

                    // comando non legato alla radio: al rigctld «Dummy», la cui risposta torna così com'è
                    if (_dummyPort <= 0) { await Write(Fallback(line.Trim())); continue; }
                    if (dummy == null)
                    {
                        dummy = new TcpClient { NoDelay = true };
                        await dummy.ConnectAsync(IPAddress.Loopback, _dummyPort, ct);
                        dummyStream = dummy.GetStream();
                        var ds = dummyStream;
                        _ = Task.Run(async () =>
                        {
                            var buf = new byte[8192];
                            try
                            {
                                while (!ct.IsCancellationRequested)
                                {
                                    int n = await ds.ReadAsync(buf, ct);
                                    if (n <= 0) break;
                                    await writeLock.WaitAsync(ct);
                                    try { await stream.WriteAsync(buf.AsMemory(0, n), ct); } finally { writeLock.Release(); }
                                }
                            }
                            catch { }
                        });
                    }
                    await dummyStream!.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), ct);
                }
            }
            catch { }
            finally
            {
                _conns.TryRemove(id, out _);
                try { dummy?.Dispose(); } catch { }
                try { tcp.Dispose(); } catch { }
            }
        }

        /// <summary>Senza il rigctld «Dummy»: risposte minime per i comandi più comuni dei programmi Hamlib.</summary>
        private static string Fallback(string line)
        {
            string c = line.Split(' ')[0];
            return c switch
            {
                "\\chk_vfo" => "0\n",
                "\\get_powerstat" => "1\n",
                "v" or "\\get_vfo" => "VFOA\n",
                "V" or "\\set_vfo" => Ok,
                _ => Unavailable
            };
        }

        private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);
        private static string Inv(long v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>Risposta nel formato di rigctld, «q» per chiudere, null per passare il comando al «Dummy».</summary>
        private async Task<string?> AnswerAsync(string line)
        {
            if (line[0] is '+' or ';' or '|' or ',') return null;   // risposte estese: le gestisce il «Dummy»
            var tok = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            string cmd = tok[0];
            if (LongNames.TryGetValue(cmd, out var shortName)) cmd = shortName;
            tok.RemoveAt(0);
            // con «--vfo» i programmi mettono il VFO come primo argomento: qui conta solo il ricevitore 0
            bool vfoArg = tok.Count > 0 && VfoNames.Contains(tok[0])
                          && ((cmd is "f" or "F" or "m" or "M" or "t" or "T" or "s" or "i" or "I" or "l" or "L" or "u" or "U" or "G")
                              || (cmd == "S" && tok.Count == 3));
            if (vfoArg) tok.RemoveAt(0);
            string A(int i) => i < tok.Count ? tok[i] : "";
            var L = _link;

            switch (cmd)
            {
                case "q": case "Q": return "q";
                case "f": return L.FreqA > 0 ? Inv(L.FreqA) + "\n" : Unavailable;
                case "F":
                    if (!double.TryParse(A(0), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) || f <= 0) return "RPRT -1\n";
                    return await L.SetFreqAAsync((long)Math.Round(f)) ? Ok : "RPRT -5\n";
                case "m":
                    return $"{TciLink.ToHamlib(L.Modulation)}\n{(L.FilterKnown ? Math.Abs(L.FilterHi - L.FilterLo) : 0)}\n";
                case "M":
                {
                    if (A(0).Length == 0) return "RPRT -1\n";
                    string tci = L.ToTci(A(0));
                    bool ok = !string.Equals(tci, L.Modulation, StringComparison.OrdinalIgnoreCase) ? await L.SetModulationAsync(tci) : true;
                    if (int.TryParse(A(1), out int pb) && pb > 0) await L.SetFilterWidthAsync(pb);
                    return ok ? Ok : "RPRT -5\n";
                }
                case "t": return (L.Trx ? "1" : "0") + "\n";
                case "T": return await L.SetTrxAsync(A(0) != "0" && A(0).Length > 0) ? Ok : "RPRT -5\n";
                case "s": return L.Split ? "1\nVFOB\n" : "0\nVFOA\n";
                case "S": return await L.SetSplitAsync(A(0) == "1") ? Ok : "RPRT -5\n";
                case "i": return L.FreqB > 0 ? Inv(L.FreqB) + "\n" : Unavailable;
                case "I":
                    if (!double.TryParse(A(0), NumberStyles.Float, CultureInfo.InvariantCulture, out double fb) || fb <= 0) return "RPRT -1\n";
                    return await L.SetFreqBAsync((long)Math.Round(fb)) ? Ok : "RPRT -5\n";
                case "G":
                    if (A(0) == "CPY") return await L.SetFreqBAsync(L.FreqA) ? Ok : "RPRT -5\n";
                    if (A(0) == "XCHG")
                    {
                        long a = L.FreqA, b = L.FreqB;
                        if (a <= 0 || b <= 0) return Unavailable;
                        await L.SetFreqAAsync(b);
                        return await L.SetFreqBAsync(a) ? Ok : "RPRT -5\n";
                    }
                    return Unavailable;
                case "l": return GetLevel(A(0));
                case "L": return await SetLevelAsync(A(0), A(1));
                case "u":
                    switch (A(0))
                    {
                        case "MUTE" when L.Mute >= 0: return L.Mute + "\n";
                        case "NR" when L.Nr >= 0: return L.Nr + "\n";
                        case "NB" when L.Nb >= 0: return L.Nb + "\n";
                        case "ANF" when L.Anf >= 0: return L.Anf + "\n";
                        default: return Unavailable;
                    }
                case "U":
                {
                    bool on = A(1) == "1";
                    Task<bool>? op = A(0) switch
                    {
                        "MUTE" => L.SetMuteAsync(on),
                        "NR" => L.SetNrAsync(on),
                        "NB" => L.SetNbAsync(on),
                        "ANF" => L.SetAnfAsync(on),
                        _ => null
                    };
                    if (op == null) return Unavailable;
                    return await op ? Ok : "RPRT -5\n";
                }
                case "b":
                {
                    int sp = line.IndexOf(' ');
                    string text = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                    string first = text.Split(' ')[0];
                    if (text.Contains(' ') && (first == "VFOA" || first == "VFOB" || first == "currVFO"))
                        text = text.Substring(text.IndexOf(' ') + 1).Trim();
                    return await L.SendCwAsync(text) ? Ok : "RPRT -5\n";
                }
                case "\\stop_morse": return await L.StopCwAsync() ? Ok : "RPRT -5\n";
                case "\\power2mW":
                case "\\mW2power":
                    return Unavailable;   // il TCI non dà la potenza massima: il programma usa la percentuale
                default:
                    return null;
            }
        }

        private string GetLevel(string name)
        {
            var L = _link;
            switch (name.ToUpperInvariant())
            {
                case "STRENGTH":
                {
                    float dbm = L.SmeterDbm;
                    return float.IsNaN(dbm) ? "-54\n" : Inv((long)Math.Round(dbm + 73)) + "\n";   // dB sopra S9 (S9 = -73 dBm)
                }
                case "SWR":
                {
                    float swr = L.Swr;
                    return (float.IsNaN(swr) ? "1.0" : Inv(Math.Round(swr, 2))) + "\n";
                }
                case "RFPOWER_METER_WATTS": return Inv(Math.Round(L.PowerW, 1)) + "\n";
                case "RFPOWER": return L.Drive >= 0 ? Inv(L.Drive / 100.0) + "\n" : Unavailable;
                case "KEYSPD": return L.CwSpeed > 0 ? Inv(L.CwSpeed) + "\n" : Unavailable;
                case "SQL":
                    if (L.SqlEnable < 0) return Unavailable;
                    if (L.SqlEnable == 0 || float.IsNaN(L.SqlLevelDb)) return "0\n";
                    return Inv(Math.Round(Math.Clamp((L.SqlLevelDb + 140) / 140.0, 0, 1), 2)) + "\n";
                default: return Unavailable;
            }
        }

        private async Task<string> SetLevelAsync(string name, string value)
        {
            var L = _link;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return "RPRT -1\n";
            switch (name.ToUpperInvariant())
            {
                case "RFPOWER": return await L.SetDriveAsync((int)Math.Round(v * 100)) ? Ok : "RPRT -5\n";
                case "KEYSPD": return await L.SetCwSpeedAsync((int)Math.Round(v)) ? Ok : "RPRT -5\n";
                case "SQL": return await L.SetSquelchAsync(v) ? Ok : "RPRT -5\n";
                default: return Unavailable;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            foreach (var c in _conns.Values) { try { c.Dispose(); } catch { } }
            _conns.Clear();
        }
    }
}
