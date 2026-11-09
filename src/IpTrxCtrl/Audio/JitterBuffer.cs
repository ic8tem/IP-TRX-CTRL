using System;
using System.Threading;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Jitter buffer circolare lock-free SPSC (un produttore: rete; un consumatore: scheda audio).
    /// Pre-riempimento iniziale, silenzio in caso di svuotamento, scarto dei campioni in eccesso
    /// quando il ritardo accumulato supera tre volte il pre-riempimento.
    /// </summary>
    public sealed class JitterBuffer
    {
        private const int Capacity = 1 << 15;          // 32768 campioni = 4 s a 8 kHz
        private const int Mask = Capacity - 1;
        private readonly short[] _ring = new short[Capacity];
        private long _head;
        private long _tail;
        private volatile bool _prefilling = true;

        public volatile int PrefillSamples = AudioEngine.SampleRate * 160 / 1000;
        public long Underruns;
        public long Overflows;

        public int Available => (int)(Volatile.Read(ref _head) - Volatile.Read(ref _tail));
        public int BufferedMs => Available * 1000 / AudioEngine.SampleRate;

        /// <summary>Produttore: aggiunge PCM 16 bit little endian.</summary>
        public void Write(byte[] pcm, int offset, int count)
        {
            int samples = count / 2;
            long head = Volatile.Read(ref _head);
            long tail = Volatile.Read(ref _tail);
            if (head - tail + samples > Capacity)
            {
                Interlocked.Increment(ref Overflows);
                return;
            }
            for (int i = 0; i < samples; i++)
                _ring[(head + i) & Mask] = (short)(pcm[offset + 2 * i] | (pcm[offset + 2 * i + 1] << 8));
            Volatile.Write(ref _head, head + samples);
        }

        /// <summary>Consumatore: riempie dst con count campioni (silenzio se mancano). Restituisce i campioni veri.</summary>
        public int Read(short[] dst, int count)
        {
            long tail = Volatile.Read(ref _tail);
            long head = Volatile.Read(ref _head);
            int avail = (int)(head - tail);

            if (_prefilling)
            {
                if (avail >= PrefillSamples) _prefilling = false;
                else { Array.Clear(dst, 0, count); return 0; }
            }
            if (avail > PrefillSamples * 3)
            {
                tail = head - PrefillSamples;
                avail = PrefillSamples;
            }
            int n = Math.Min(count, avail);
            for (int i = 0; i < n; i++) dst[i] = _ring[(tail + i) & Mask];
            Volatile.Write(ref _tail, tail + n);
            if (n < count)
            {
                Array.Clear(dst, n, count - n);
                Interlocked.Increment(ref Underruns);
                _prefilling = true;
            }
            return n;
        }

        public void Reset()
        {
            Volatile.Write(ref _tail, Volatile.Read(ref _head));
            _prefilling = true;
        }
    }
}
