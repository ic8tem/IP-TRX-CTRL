using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Rig
{
    public enum SerialLine { None, Rts, Dtr }

    /// <summary>
    /// Come vengono usate le porte seriali per PTT e CW, ricavato dalle impostazioni.
    /// Regole: se PTT e CW usano la stessa porta, usano linee opposte (CW su DTR → PTT su RTS e viceversa).
    /// Se la porta coincide con quella del CAT, la apre rigctld: il PTT lo gestisce rigctld e il CW
    /// non può essere battuto su quella porta (si usa il CAT).
    /// </summary>
    public sealed record KeyingPlan(string PttPort, SerialLine PttLine, bool PttViaRigctld,
                                    string CwPort, SerialLine CwLine, bool CwSerial, string Note)
    {
        public static KeyingPlan Resolve(AppSettings s)
        {
            var notes = new List<string>();
            bool pttSerial = s.PttMethod == PttMethod.Rts || s.PttMethod == PttMethod.Dtr;
            string pttPort = string.IsNullOrEmpty(s.PttCom) ? s.RigCom : s.PttCom;
            var pttLine = s.PttMethod == PttMethod.Rts ? SerialLine.Rts : s.PttMethod == PttMethod.Dtr ? SerialLine.Dtr : SerialLine.None;

            bool cwSerial = s.CwMethod != CwMethod.Cat;
            var cwLine = s.CwMethod == CwMethod.Dtr ? SerialLine.Dtr : s.CwMethod == CwMethod.Rts ? SerialLine.Rts : SerialLine.None;
            string cwPort = !string.IsNullOrEmpty(s.CwCom) ? s.CwCom : pttSerial ? pttPort : (!string.IsNullOrEmpty(s.PttCom) ? s.PttCom : s.RigCom);

            if (cwSerial && pttSerial && Same(cwPort, pttPort) && cwLine == pttLine)
            {
                pttLine = cwLine == SerialLine.Dtr ? SerialLine.Rts : SerialLine.Dtr;
                notes.Add($"stessa porta per PTT e CW: CW su {Name(cwLine)}, PTT spostato su {Name(pttLine)}");
            }
            bool pttViaRigctld = pttSerial && Same(pttPort, s.RigCom) && !s.UseExternalRigctld;
            if (pttViaRigctld) notes.Add($"PTT {Name(pttLine)} sulla porta del CAT: lo comanda rigctld");
            if (cwSerial && Same(cwPort, s.RigCom) && !s.UseExternalRigctld)
            {
                cwSerial = false;
                notes.Add("la porta CW coincide con quella del CAT (aperta da rigctld): il CW viene inviato via CAT; scegli la seconda porta seriale della radio o un adattatore");
            }
            if (cwSerial && string.IsNullOrEmpty(cwPort))
            {
                cwSerial = false;
                notes.Add("nessuna porta per il CW: il CW viene inviato via CAT");
            }
            return new KeyingPlan(pttPort, pttLine, pttViaRigctld, cwPort, cwSerial ? cwLine : SerialLine.None, cwSerial, string.Join(" · ", notes));
        }

        public static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        public static string Name(SerialLine l) => l == SerialLine.Rts ? "RTS" : l == SerialLine.Dtr ? "DTR" : "—";
    }

    /// <summary>Porta seriale usata solo per le linee RTS e DTR, condivisa tra PTT e CW.</summary>
    public sealed class SerialLines : IDisposable
    {
        private static readonly Dictionary<string, (SerialLines lines, int refs)> Open = new(StringComparer.OrdinalIgnoreCase);
        private readonly SerialPort _port;
        public string Name { get; }

        private SerialLines(string name)
        {
            Name = name;
            // linee basse prima dell'apertura: alcune porte USB le alzano all'apertura (PTT involontario)
            _port = new SerialPort(name, 9600) { Handshake = Handshake.None, DtrEnable = false, RtsEnable = false };
            _port.Open();
            _port.DtrEnable = false;
            _port.RtsEnable = false;
        }

        public static SerialLines Acquire(string name)
        {
            lock (Open)
            {
                if (Open.TryGetValue(name, out var e)) { Open[name] = (e.lines, e.refs + 1); return e.lines; }
                var l = new SerialLines(name);
                Open[name] = (l, 1);
                return l;
            }
        }

        public void Set(SerialLine line, bool on)
        {
            try
            {
                if (line == SerialLine.Rts) _port.RtsEnable = on;
                else if (line == SerialLine.Dtr) _port.DtrEnable = on;
            }
            catch { }
        }

        public void Dispose()
        {
            lock (Open)
            {
                if (!Open.TryGetValue(Name, out var e)) return;
                if (e.refs > 1) { Open[Name] = (e.lines, e.refs - 1); return; }
                Open.Remove(Name);
            }
            try { _port.RtsEnable = false; _port.DtrEnable = false; } catch { }
            try { _port.Close(); } catch { }
            _port.Dispose();
        }
    }

    /// <summary>
    /// Tasto CW su linea seriale (DTR o RTS): riceve il testo dal client e lo trasmette con i tempi
    /// Morse alla velocità indicata. Prima del primo elemento alza il PTT e attende il ritardo PTT;
    /// alla fine del testo (e della coda) attende 3 punti e rilascia il PTT.
    /// </summary>
    public sealed class CwKeyer : IDisposable
    {
        private static readonly Dictionary<char, string> Morse = new()
        {
            ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.", ['G'] = "--.", ['H'] = "....",
            ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.",
            ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
            ['Y'] = "-.--", ['Z'] = "--..", ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
            ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.", ['?'] = "..--..", ['/'] = "-..-.",
            ['.'] = ".-.-.-", [','] = "--..--", ['='] = "-...-", ['+'] = ".-.-.", ['-'] = "-....-", ['@'] = ".--.-.",
        };

        private readonly Action<bool> _key;
        private readonly Action<bool> _ptt;
        private readonly Func<int> _pttDelayMs;
        private readonly BlockingCollection<(string text, int wpm)> _queue = new();
        private readonly Thread _thread;
        private volatile bool _abort;
        private volatile bool _disposed;
        public event Action<string>? Log;
        public bool Busy { get; private set; }

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

        public CwKeyer(Action<bool> key, Action<bool> ptt, Func<int> pttDelayMs)
        {
            _key = key; _ptt = ptt; _pttDelayMs = pttDelayMs;
            _thread = new Thread(Run) { IsBackground = true, Name = "CW keyer", Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        public void Send(string text, int wpm)
        {
            text = (text ?? "").Trim().ToUpperInvariant();
            if (text.Length > 0) _queue.Add((text, Math.Clamp(wpm, 5, 60)));
        }

        /// <summary>Interrompe subito: tasto su, coda svuotata, PTT rilasciato.</summary>
        public void Stop()
        {
            while (_queue.TryTake(out _)) { }
            _abort = true;
        }

        private void Run()
        {
            bool win = OperatingSystem.IsWindows();
            foreach (var (text, wpm) in _queue.GetConsumingEnumerable())
            {
                if (_disposed) break;
                _abort = false;
                Busy = true;
                if (win) try { timeBeginPeriod(1); } catch { }
                try
                {
                    double dot = 1200.0 / wpm;
                    Log?.Invoke($"CW {wpm} WPM: {text}");
                    _ptt(true);
                    Wait(Math.Max(0, _pttDelayMs()));
                    string current = text;
                    while (!_abort)
                    {
                        Play(current, dot);
                        if (_abort || !_queue.TryTake(out var next)) break;
                        dot = 1200.0 / next.wpm;          // messaggio in coda: PTT resta su
                        Wait(dot * 7);
                        current = next.text;
                        Log?.Invoke($"CW {next.wpm} WPM: {current}");
                    }
                    _key(false);
                    if (!_abort) Wait(dot * 3);
                }
                finally
                {
                    _key(false);
                    _ptt(false);
                    if (win) try { timeEndPeriod(1); } catch { }
                    Busy = false;
                }
            }
        }

        private void Play(string text, double dot)
        {
            for (int i = 0; i < text.Length && !_abort; i++)
            {
                char c = text[i];
                if (c == ' ') { Wait(dot * 4); continue; }        // con i 3 punti di fine carattere fa 7
                if (!Morse.TryGetValue(c, out string? code)) continue;
                foreach (char s in code)
                {
                    if (_abort) return;
                    _key(true);
                    Wait(s == '-' ? dot * 3 : dot);
                    _key(false);
                    Wait(dot);
                }
                Wait(dot * 2);                                    // fine carattere: 3 punti in tutto
            }
        }

        /// <summary>Attesa precisa: sonno per la parte lunga, poi attesa attiva sugli ultimi 2 ms.</summary>
        private void Wait(double ms)
        {
            var sw = Stopwatch.StartNew();
            while (!_abort)
            {
                double left = ms - sw.Elapsed.TotalMilliseconds;
                if (left <= 0) return;
                if (left > 3) Thread.Sleep(1);
                else Thread.SpinWait(50);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
            _queue.CompleteAdding();
        }
    }
}

namespace IpTrxCtrl.Rig
{
    /// <summary>
    /// Server: ripete il tasto CW manuale del client conservandone i tempi. Ogni evento arriva con
    /// l'istante del client; il primo di una serie fissa la corrispondenza degli orologi e tutti
    /// vengono eseguiti con un ritardo fisso (assorbe il jitter di rete). Il PTT si alza al primo
    /// evento e si rilascia dopo 800 ms senza attività. Tasto giù per più di 10 s: rilasciato.
    /// </summary>
    public sealed class ManualCwReplayer : IDisposable
    {
        private readonly Action<bool> _key, _ptt;
        private readonly System.Collections.Concurrent.BlockingCollection<(bool down, long ts)> _events = new();
        private readonly System.Threading.Thread _thread;
        private readonly int _delayMs;
        private volatile bool _run = true;

        public ManualCwReplayer(Action<bool> key, Action<bool> ptt, int delayMs)
        {
            _key = key; _ptt = ptt; _delayMs = System.Math.Clamp(delayMs, 50, 2000);
            _thread = new System.Threading.Thread(Run) { IsBackground = true, Name = "tasto CW manuale", Priority = System.Threading.ThreadPriority.Highest };
            _thread.Start();
        }

        public void Event(bool down, long clientMs) { if (_run) _events.Add((down, clientMs)); }

        private void Run()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long offset = 0;           // istante del server = istante del client + offset
            bool active = false, keyDown = false;
            long lastActivity = 0, downSince = 0;
            while (_run)
            {
                long now = clock.ElapsedMilliseconds;
                if (_events.TryTake(out var ev, 5))
                {
                    now = clock.ElapsedMilliseconds;
                    if (!active)
                    {
                        offset = now - ev.ts + _delayMs;
                        _ptt(true);
                        active = true;
                    }
                    long at = ev.ts + offset;
                    while (clock.ElapsedMilliseconds < at && _run)
                    {
                        long left = at - clock.ElapsedMilliseconds;
                        if (left > 3) System.Threading.Thread.Sleep(1); else System.Threading.Thread.SpinWait(50);
                    }
                    if (at < clock.ElapsedMilliseconds - 300) offset = clock.ElapsedMilliseconds - ev.ts + _delayMs; // rete in ritardo: riallinea
                    _key(ev.down);
                    keyDown = ev.down;
                    if (ev.down) downSince = clock.ElapsedMilliseconds;
                    lastActivity = clock.ElapsedMilliseconds;
                    continue;
                }
                now = clock.ElapsedMilliseconds;
                if (keyDown && now - downSince > 10000) { _key(false); keyDown = false; lastActivity = now; }
                if (active && !keyDown && now - lastActivity > 800) { _ptt(false); active = false; }
            }
            _key(false);
            if (active) _ptt(false);
        }

        public void Dispose()
        {
            _run = false;
            _events.CompleteAdding();
        }
    }
}
