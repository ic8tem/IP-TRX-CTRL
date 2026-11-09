using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Cavi audio virtuali per i programmi digitali del client (WSJT-X, JTDX, JS8Call, fldigi…).
    /// Windows 64 bit: VB-CABLE (uno o due cavi). macOS: BlackHole. Linux / Raspberry: due «null sink»
    /// creati dal programma con pactl (PipeWire o PulseAudio). Windows 32 bit: non disponibile.
    /// </summary>
    public static class VirtualCables
    {
        public const string LinuxRx = "IPTRX_RX";   // il programma ascolta da IPTRX_RX.monitor
        public const string LinuxTx = "IPTRX_TX";   // il programma trasmette su IPTRX_TX

        public static bool Supported =>
            !(OperatingSystem.IsWindows() && !Environment.Is64BitProcess);

        public static bool IsLinux => !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

        public static bool IsVirtual(string name) =>
            name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BlackHole", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

        /// <summary>Breve stato per la finestra delle impostazioni.</summary>
        public static string Describe(List<string> inputs, List<string> outputs)
        {
            if (!Supported) return "Non disponibile su Windows 32 bit.";
            if (IsLinux) return "Linux / Raspberry: il programma crea da solo due cavi (IP-TRX-CTRL RX e TX) con pactl.";
            int n = outputs.Count(IsVirtual);
            if (n == 0)
                return OperatingSystem.IsMacOS()
                    ? "Nessun cavo trovato: installa BlackHole 2ch (brew install blackhole-2ch) e riavvia il programma."
                    : "Nessun cavo trovato: installa VB-CABLE (lo propone l'installatore) e riavvia il PC.";
            return n == 1
                ? "Trovato un cavo virtuale: funziona in semi-duplex (ricezione e trasmissione si alternano sullo stesso cavo)."
                : $"Trovati {n} cavi virtuali: usa un cavo per direzione.";
        }

        /// <summary>Scelta automatica: (verso il programma, dal programma).</summary>
        public static (string toProgram, string fromProgram) Suggest(List<string> inputs, List<string> outputs)
        {
            if (IsLinux) return (LinuxRx, LinuxTx + ".monitor");
            var outs = outputs.Where(IsVirtual).ToList();
            var ins = inputs.Where(IsVirtual).ToList();
            string to = outs.FirstOrDefault() ?? "";
            string from = ins.FirstOrDefault() ?? "";
            if (outs.Count >= 2 && ins.Count >= 2)
            {
                // cavo A verso il programma, cavo B dal programma
                to = outs[0];
                string tag = Tag(outs[1]);
                from = ins.FirstOrDefault(i => Tag(i) == tag && tag.Length > 0) ?? ins[1];
            }
            return (to, from);
        }

        private static string Tag(string name)
        {
            int i = name.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            string rest = name[(i + 5)..].TrimStart('-', ' ');
            return rest.Length > 0 && char.IsLetter(rest[0]) && (rest.Length == 1 || !char.IsLetter(rest[1])) ? rest[..1].ToUpperInvariant() : "";
        }

        /// <summary>Linux: crea i due null sink se mancano (restano finché la sessione audio è attiva).</summary>
        public static bool EnsureLinuxSinks(Action<string>? log)
        {
            try
            {
                string sinks = Run("pactl", "list short sinks");
                foreach (var (name, desc) in new[] { (LinuxRx, "IP-TRX-CTRL_RX_(verso_il_programma)"), (LinuxTx, "IP-TRX-CTRL_TX_(dal_programma)") })
                {
                    if (sinks.Contains(name)) continue;
                    Run("pactl", $"load-module module-null-sink sink_name={name} rate=48000 channels=1 sink_properties=device.description={desc}");
                    log?.Invoke($"CAVI creato il cavo virtuale {name}");
                }
                return Run("pactl", "list short sinks").Contains(LinuxRx);
            }
            catch (Exception ex)
            {
                log?.Invoke("CAVI pactl non disponibile: " + ex.Message);
                return false;
            }
        }

        public static string Run(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
    }

    /// <summary>
    /// Secondo percorso audio del client: copia della ricezione verso il programma digitale e
    /// audio di trasmissione preso dal programma (solo mentre il programma chiede il PTT).
    /// </summary>
    public sealed class DigitalAudio : IDisposable
    {
        private readonly JitterBuffer _toProgram = new() { PrefillSamples = AudioEngine.SampleRate * 80 / 1000 };
        private IAudioBackend? _backend;
        private Process? _pacat, _parec;
        private CancellationTokenSource? _cts;
        private volatile bool _tx;
        private readonly short[] _silence = new short[AudioEngine.FrameSamples];

        public string Status { get; private set; } = "spento";
        public float TxPeak;
        /// <summary>Blocco PCM (16 bit LE) arrivato dal programma digitale.</summary>
        public event Action<byte[], int>? Captured;

        /// <summary>Durante la trasmissione non si manda la ricezione al programma (un solo cavo = semi-duplex).</summary>
        public bool Transmitting { get => _tx; set { _tx = value; if (value) _toProgram.Reset(); } }

        public bool Start(string toProgram, string fromProgram, Action<string>? log)
        {
            Stop();
            if (!VirtualCables.Supported) { Status = "non disponibile su Windows 32 bit"; return false; }
            if (VirtualCables.IsLinux) return StartLinux(log);
            if (string.IsNullOrEmpty(toProgram) || string.IsNullOrEmpty(fromProgram)) { Status = "scegli i due cavi nelle impostazioni"; return false; }
            try
            {
                _backend = AudioBackends.Create();
                _backend.Start(fromProgram, toProgram, OnCapture, OnFill);
                Status = $"verso il programma: {toProgram} · dal programma: {fromProgram}";
                log?.Invoke("DIGITALE audio avviato: " + Status);
                return true;
            }
            catch (Exception ex)
            {
                Status = "errore: " + ex.Message;
                log?.Invoke("DIGITALE " + Status);
                return false;
            }
        }

        private bool StartLinux(Action<string>? log)
        {
            if (!VirtualCables.EnsureLinuxSinks(log)) { Status = "pactl non disponibile (installa pulseaudio-utils o pipewire-pulse)"; return false; }
            try
            {
                const string fmt = "--format=s16le --rate=8000 --channels=1 --raw --latency-msec=40";
                _pacat = Process.Start(new ProcessStartInfo("pacat", $"--playback --device={VirtualCables.LinuxRx} {fmt}")
                { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true });
                _parec = Process.Start(new ProcessStartInfo("parec", $"--device={VirtualCables.LinuxTx}.monitor {fmt}")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _ = Task.Run(() => PlayLoopAsync(ct));
                _ = Task.Run(() => RecordLoopAsync(ct));
                Status = "cavi IP-TRX-CTRL RX (il programma ascolta da «Monitor of IP-TRX-CTRL RX») e TX (il programma trasmette su «IP-TRX-CTRL TX»)";
                log?.Invoke("DIGITALE audio avviato con pacat/parec");
                return true;
            }
            catch (Exception ex)
            {
                Status = "pacat/parec non disponibili: " + ex.Message;
                log?.Invoke("DIGITALE " + Status);
                Stop();
                return false;
            }
        }

        private async Task PlayLoopAsync(CancellationToken ct)
        {
            var frame = new short[AudioEngine.FrameSamples];
            var bytes = new byte[frame.Length * 2];
            var sw = Stopwatch.StartNew();
            long sent = 0;
            var stdin = _pacat!.StandardInput.BaseStream;
            while (!ct.IsCancellationRequested)
            {
                OnFill(frame);
                Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
                try { await stdin.WriteAsync(bytes, ct); await stdin.FlushAsync(ct); } catch { break; }
                sent++;
                long due = sent * AudioEngine.FrameMs - sw.ElapsedMilliseconds;
                if (due > 0) { try { await Task.Delay((int)due, ct); } catch { break; } }
            }
        }

        private async Task RecordLoopAsync(CancellationToken ct)
        {
            var stdout = _parec!.StandardOutput.BaseStream;
            var bytes = new byte[AudioEngine.FrameSamples * 2];
            var frame = new short[AudioEngine.FrameSamples];
            while (!ct.IsCancellationRequested)
            {
                int got = 0;
                try
                {
                    while (got < bytes.Length)
                    {
                        int n = await stdout.ReadAsync(bytes.AsMemory(got), ct);
                        if (n <= 0) return;
                        got += n;
                    }
                }
                catch { return; }
                Buffer.BlockCopy(bytes, 0, frame, 0, bytes.Length);
                OnCapture(frame);
            }
        }

        /// <summary>Ricezione dal server (prima del volume), da passare al programma.</summary>
        public void PushRx(short[] frame, int count)
        {
            if (_tx) return;
            var b = new byte[count * 2];
            Buffer.BlockCopy(frame, 0, b, 0, b.Length);
            _toProgram.Write(b, 0, b.Length);
        }

        private void OnFill(short[] frame)
        {
            if (_tx) { Array.Copy(_silence, frame, frame.Length); return; }
            _toProgram.Read(frame, frame.Length);
        }

        private void OnCapture(short[] frame)
        {
            if (!_tx) return;
            var buf = new byte[frame.Length * 2];
            float peak = 0;
            for (int i = 0; i < frame.Length; i++)
            {
                short s = frame[i];
                buf[2 * i] = (byte)s; buf[2 * i + 1] = (byte)(s >> 8);
                float a = Math.Abs(s / 32768f);
                if (a > peak) peak = a;
            }
            TxPeak = peak;
            Captured?.Invoke(buf, buf.Length);
        }

        public void Stop()
        {
            _cts?.Cancel(); _cts = null;
            try { _backend?.Stop(); _backend?.Dispose(); } catch { }
            _backend = null;
            foreach (var p in new[] { _pacat, _parec })
                try { if (p != null && !p.HasExited) p.Kill(); p?.Dispose(); } catch { }
            _pacat = null; _parec = null;
            _toProgram.Reset();
            Status = "spento";
        }

        public void Dispose() => Stop();
    }
}
