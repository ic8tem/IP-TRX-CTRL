using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Rig;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// «Scheda audio» di una radio Icom in rete (0.3.6): l'audio ricevuto arriva dal flusso audio del protocollo
    /// LAN Icom (PCM 16 bit a 8 kHz) e quello da trasmettere ci ritorna, senza cavi né schede audio sul server.
    /// </summary>
    public sealed class IcomLanAudioBackend : IAudioBackend
    {
        public const string DeviceName = "Icom LAN · audio della radio";

        private readonly object _lock = new();
        private Action<short[]>? _onCapture, _fill;
        private CancellationTokenSource? _cts;
        private IcomLanLink? _link;
        private readonly short[] _rxFrame = new short[AudioEngine.FrameSamples];
        private int _rxFill;

        public string LastError { get; private set; } = "";
        public List<string> Inputs() => new() { DeviceName };
        public List<string> Outputs() => new() { DeviceName };

        public void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill)
        {
            Stop();
            lock (_lock) { _onCapture = onCapture; _fill = fill; _rxFill = 0; }
            _link = IcomLanLink.Current;
            if (_link == null) { LastError = "radio Icom in rete non collegata"; return; }
            LastError = "";
            _link.AudioReceived += OnRx;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => PaceAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            if (_link != null) _link.AudioReceived -= OnRx;
            _link = null;
            lock (_lock) { _onCapture = null; _fill = null; }
        }

        public void Dispose() => Stop();

        /// <summary>Ogni 40 ms un blocco dall'audio del client verso la radio, come farebbe una scheda.</summary>
        private async Task PaceAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            long next = 0;
            var frame = new short[AudioEngine.FrameSamples];
            while (!ct.IsCancellationRequested)
            {
                next += AudioEngine.FrameMs;
                long wait = next - sw.ElapsedMilliseconds;
                if (wait > 0) { try { await Task.Delay((int)wait, ct); } catch { break; } }
                else if (wait < -500) next = sw.ElapsedMilliseconds;
                Action<short[]>? fill;
                lock (_lock) fill = _fill;
                if (fill == null) continue;
                try { fill(frame); } catch { continue; }
                (IcomLanLink.Current ?? _link)?.SendAudio((short[])frame.Clone());
            }
        }

        private void OnRx(short[] samples)
        {
            Action<short[]>? cap;
            var ready = new List<short[]>();
            lock (_lock)
            {
                cap = _onCapture;
                if (cap == null) return;
                foreach (short v in samples)
                {
                    _rxFrame[_rxFill++] = v;
                    if (_rxFill == _rxFrame.Length) { _rxFill = 0; ready.Add((short[])_rxFrame.Clone()); }
                }
            }
            foreach (var f in ready) { try { cap(f); } catch { } }
        }
    }
}
