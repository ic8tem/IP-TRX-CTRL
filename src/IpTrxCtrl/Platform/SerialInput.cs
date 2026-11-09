using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Platform
{
    /// <summary>
    /// Porta seriale del client usata come ingresso: pedale PTT e tasto CW meccanico, oppure la porta
    /// virtuale di un altro programma (com0com su Windows: il suo RTS arriva su CTS, il DTR su DSR).
    /// DTR e RTS vengono alzati per alimentare il contatto (tasto tra DTR e DSR, pedale tra RTS e CTS).
    /// Le linee si leggono ogni millisecondo, così i tempi del tasto restano precisi.
    /// </summary>
    public sealed class SerialInput : IDisposable
    {
        private readonly SerialPort _port;
        private readonly InputPin _ptt, _cw;
        private readonly Thread _thread;
        private volatile bool _run = true;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public event Action<bool>? PttChanged;
        public event Action<bool, long>? CwKeyChanged;   // giù/su, istante in ms
        public static long NowMs => Clock.ElapsedMilliseconds;

        public SerialInput(string port, InputPin ptt, InputPin cw)
        {
            _ptt = ptt; _cw = cw;
            _port = new SerialPort(port, 9600) { Handshake = Handshake.None, DtrEnable = true, RtsEnable = true };
            _port.Open();
            _thread = new Thread(Loop) { IsBackground = true, Name = "ingresso seriale", Priority = ThreadPriority.Highest };
            _thread.Start();
        }

        private bool Read(InputPin p)
        {
            try
            {
                return p switch
                {
                    InputPin.Cts => _port.CtsHolding,
                    InputPin.Dsr => _port.DsrHolding,
                    InputPin.Dcd => _port.CDHolding,
                    _ => false
                };
            }
            catch { return false; }
        }

        private void Loop()
        {
            bool ptt = false, key = false;
            while (_run)
            {
                if (_ptt != InputPin.None)
                {
                    bool v = Read(_ptt);
                    if (v != ptt) { ptt = v; PttChanged?.Invoke(v); }
                }
                if (_cw != InputPin.None)
                {
                    bool v = Read(_cw);
                    if (v != key) { key = v; CwKeyChanged?.Invoke(v, NowMs); }
                }
                Thread.Sleep(1);
            }
            if (ptt) PttChanged?.Invoke(false);
            if (key) CwKeyChanged?.Invoke(false, NowMs);
        }

        public void Dispose()
        {
            _run = false;
            _thread.Join(500);
            try { _port.DtrEnable = false; _port.RtsEnable = false; _port.Close(); } catch { }
            _port.Dispose();
        }
    }
}
