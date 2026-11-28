using System;
using System.Diagnostics;
using IpTrxCtrl.Audio;

namespace IpTrxCtrl.Audio
{
    // al posto del motore audio vero: solo le costanti che usano i componenti misurati
    public sealed class AudioEngine { public const int SampleRate = 8000, FrameMs = 40, FrameSamples = SampleRate * FrameMs / 1000; }
}

public static class Bench
{
    const int Frames = 1500;   // 60 s di audio
    static byte[][] MakeAudio()
    {
        var rnd = new Random(1);
        var frames = new byte[Frames][];
        double ph = 0;
        for (int f = 0; f < Frames; f++)
        {
            var b = new byte[640];
            for (int i = 0; i < 320; i++)
            {
                int n = f * 320 + i;
                double pitch = 140 + 40 * Math.Sin(n * 0.0007);
                ph += 2 * Math.PI * pitch / 8000;
                double voiced = ((n / 4000) % 3 != 0) ? 1 : 0;
                double v = voiced * 0.25 * (Math.Sin(ph) + 0.5 * Math.Sin(2 * ph) + 0.3 * Math.Sin(3 * ph)) + 0.05 * (rnd.NextDouble() * 2 - 1);
                short s = (short)Math.Clamp(v * 32767, -32768, 32767);
                b[2 * i] = (byte)s; b[2 * i + 1] = (byte)(s >> 8);
            }
            frames[f] = b;
        }
        return frames;
    }

    static void Measure(string name, Action<byte[]> work, byte[][] frames)
    {
        for (int i = 0; i < 100; i++) work((byte[])frames[i].Clone());   // riscaldamento (JIT)
        GC.Collect(); GC.WaitForPendingFinalizers();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        var copies = new byte[Frames][];
        for (int i = 0; i < Frames; i++) copies[i] = (byte[])frames[i].Clone();
        long allocCopies = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        var t0 = Process.GetCurrentProcess().TotalProcessorTime;
        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++) work(copies[i]);
        sw.Stop();
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000 / Frames;
        double corePct = usPerFrame / 40000.0 * 100;
        Console.WriteLine($"{name,-38} {usPerFrame,9:F1} µs/blocco  {corePct,6:F2} % di un core  {(a1 - a0) / (double)Frames,8:F0} byte allocati/blocco");
    }

    public static void Main()
    {
        Console.WriteLine($"{RuntimeInfo()}");
        var frames = MakeAudio();
        var enc = new OpusTx(); enc.SetBitrate(16000);
        Measure("Opus codifica 16 kbit/s", f => enc.Encode(f, f.Length), frames);
        var enc2 = new OpusTx();
        var packets = new byte[Frames][];
        for (int i = 0; i < Frames; i++) packets[i] = enc2.Encode(frames[i], 640);
        var dec = new OpusRx(); uint seq = 0;
        Measure("Opus decodifica", f => dec.Decode(++seq, packets[seq % Frames]), frames);
        var nrv = new NeuralDenoiser { Enabled = true };
        Measure("NR neurale HamNoise (fonia)", f => nrv.Process(f, f.Length, false), frames);
        var nrc = new NeuralDenoiser { Enabled = true };
        Measure("NR neurale HamNoise (CW)", f => nrc.Process(f, f.Length, true), frames);
        var sq = new VoiceSquelch { Enabled = true };
        Measure("Squelch vocale", f => sq.Process(f, f.Length), frames);
        var shorts = new short[320];
        void ToShort(byte[] f) { for (int i = 0; i < 320; i++) shorts[i] = (short)(f[2 * i] | f[2 * i + 1] << 8); }
        var cw = new CwDecoder(600, 100);
        Measure("Decodificatore CW (1 canale)", f => { ToShort(f); cw.Process(shorts, 320); }, frames);
        var cws = new CwDecoder[6]; for (int i = 0; i < 6; i++) cws[i] = new CwDecoder(400 + 100 * i, 100);
        Measure("Decodificatore CW (6 canali)", f => { ToShort(f); foreach (var d in cws) d.Process(shorts, 320); }, frames);
        var sp = new Spectrum(); int rows = 0; sp.Row += _ => rows++;
        Measure("Spettro della cascata CW", f => { ToShort(f); sp.Process(shorts, 320); }, frames);
        Console.WriteLine($"memoria gestita dopo le prove: {GC.GetTotalMemory(true) / 1024.0 / 1024:F1} MB");
    }

    static string RuntimeInfo() => $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} · {Environment.ProcessorCount} core";
}
