using System;
using System.Diagnostics;
using System.Threading;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Manipolatore elettronico per i tasti USB (0.3.6): VBand e Vail in modo tastiera (Ctrl sinistro = punto,
    /// Ctrl destro = linea), adattatori MIDI (Vail, TinyMIDI). In modo iambico genera punti e linee alla velocità
    /// scelta (modo A o B, con la memoria dell'altra paletta); in modo verticale segue il contatto così com'è.
    /// Il risultato sono gli stessi eventi «tasto giù / su» del tasto verticale sulla seriale, mandati al server.
    /// </summary>
    public sealed class PaddleKeyer : IDisposable
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static long NowMs => Clock.ElapsedMilliseconds;

        public event Action<bool, long>? Key;     // giù/su, istante in ms

        public volatile int Wpm = 20;
        public volatile bool Iambic = true;
        public volatile bool ModeB = true;
        public volatile bool Swap;

        private volatile bool _dit, _dah, _straight;
        private volatile bool _ditMem, _dahMem;
        private bool _keyDown;
        private readonly Thread _thread;
        private volatile bool _run = true;
        private readonly AutoResetEvent _wake = new(false);

        public PaddleKeyer()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "manipolatore CW", Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        /// <summary>Paletta del punto (sinistra) o della linea (destra); con «Swap» si scambiano.</summary>
        public void Paddle(bool left, bool down)
        {
            bool dit = left ^ Swap;
            if (!Iambic) { SetStraight(down); return; }
            if (dit) { _dit = down; if (down) _ditMem = true; }
            else { _dah = down; if (down) _dahMem = true; }
            _wake.Set();
        }

        /// <summary>Contatto del tasto verticale (nota MIDI del tasto, o una paletta col modo verticale).</summary>
        public void SetStraight(bool down)
        {
            if (_straight == down) return;
            _straight = down;
            Emit(down);
        }

        private void Emit(bool down)
        {
            lock (this)
            {
                if (_keyDown == down) return;
                _keyDown = down;
            }
            try { Key?.Invoke(down, NowMs); } catch { }
        }

        private static void Wait(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                int left = ms - (int)sw.ElapsedMilliseconds;
                if (left > 2) Thread.Sleep(left - 1); else Thread.SpinWait(200);
            }
        }

        private void Loop()
        {
            bool lastDit = false;
            while (_run)
            {
                if (!Iambic || (!_dit && !_dah && !_ditMem && !_dahMem)) { _wake.WaitOne(20); continue; }
                int dot = 1200 / Math.Clamp(Wpm, 5, 60);
                // scelta del prossimo elemento: alternanza se tutte e due le palette sono premute (o in memoria)
                bool sendDit;
                bool wantDit = _dit || _ditMem, wantDah = _dah || _dahMem;
                if (wantDit && wantDah) sendDit = !lastDit;
                else sendDit = wantDit;
                if (sendDit) _ditMem = false; else _dahMem = false;
                lastDit = sendDit;
                // durante l'elemento la paletta opposta va in memoria (modo B: anche se rilasciata subito)
                bool opposite = false;
                Emit(true);
                int len = sendDit ? dot : dot * 3;
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < len) { if (sendDit ? _dah : _dit) opposite = true; Thread.Sleep(1); }
                Emit(false);
                Wait(dot);
                if (opposite)
                {
                    if (ModeB) { if (sendDit) _dahMem = true; else _ditMem = true; }
                    else if (sendDit ? _dah : _dit) { if (sendDit) _dahMem = true; else _ditMem = true; }
                }
                if (!ModeB) { if (!_dit) _ditMem = false; if (!_dah) _dahMem = false; }
            }
            Emit(false);
        }

        public void ReleaseAll()
        {
            _dit = _dah = false; _ditMem = _dahMem = false;
            SetStraight(false);
            Emit(false);
        }

        public void Dispose()
        {
            _run = false;
            _wake.Set();
            _thread.Join(300);
            Emit(false);
        }
    }
}
