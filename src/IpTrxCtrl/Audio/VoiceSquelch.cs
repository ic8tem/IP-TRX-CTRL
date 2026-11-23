using System;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Squelch vocale (dalla 0.3.7, idea di PiRO di VK3TR): apre quando sente una voce, non quando sale il livello.
    /// Per ogni blocco da 40 ms cerca la periodicità della voce (altezza tra 70 e 400 Hz) con l'autocorrelazione
    /// normalizzata. Una portante o un tono CW sono periodici in modo perfetto e sempre alla stessa altezza: non aprono.
    /// Il fruscio e gli scrosci di statica non sono periodici: non aprono. Nessuna soglia da regolare.
    /// Resta aperto per il tempo di tenuta dopo l'ultima sillaba; apertura e chiusura con una rampa breve.
    /// </summary>
    public sealed class VoiceSquelch
    {
        private const int MinLag = 20;    // 400 Hz a 8 kHz
        private const int MaxLag = 114;   // 70 Hz
        private readonly float[] _x = new float[AudioEngine.FrameSamples];
        private int _holdFrames, _left;
        private float _gain;                 // guadagno attuale (rampa)
        private int _lastLag, _sameLag;      // altezza ferma per molti blocchi = portante
        private int _voicedRun;

        public bool Enabled { get; set; }
        public int HoldMs { set => _holdFrames = Math.Max(1, value / AudioEngine.FrameMs); }
        public bool IsOpen => _left > 0;

        public VoiceSquelch() { HoldMs = 1200; }

        public void Reset() { _left = 0; _gain = 0; _voicedRun = 0; _sameLag = 0; }

        /// <summary>Applica lo squelch a un blocco PCM 16 bit LE (sul posto).</summary>
        public void Process(byte[] pcm, int len)
        {
            if (!Enabled) { _gain = 1; return; }
            int n = Math.Min(len / 2, _x.Length);
            if (n < MaxLag * 2) return;
            double energy = 0;
            for (int i = 0; i < n; i++) { _x[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8); energy += _x[i] * (double)_x[i]; }
            bool voiced = false;
            if (energy / n > 40 * 40)   // sotto circa -58 dBFS non c'è niente da sentire
            {
                double best = 0; int bestLag = 0;
                for (int lag = MinLag; lag <= MaxLag; lag++)
                {
                    double num = 0, e0 = 0, e1 = 0;
                    for (int i = 0; i + lag < n; i++) { num += _x[i] * (double)_x[i + lag]; e0 += _x[i] * (double)_x[i]; e1 += _x[i + lag] * (double)_x[i + lag]; }
                    double r = num / Math.Sqrt(e0 * e1 + 1);
                    if (r > best) { best = r; bestLag = lag; }
                }
                // portante: periodicità quasi perfetta e altezza che non cambia
                if (Math.Abs(bestLag - _lastLag) <= 1) _sameLag++; else _sameLag = 0;
                _lastLag = bestLag;
                bool carrier = best > 0.97 || _sameLag > 12;
                voiced = best > 0.45 && !carrier;
            }
            _voicedRun = voiced ? _voicedRun + 1 : 0;
            if (_voicedRun >= 2) _left = _holdFrames;          // due blocchi di voce di fila (80 ms) aprono
            else if (_left > 0) _left--;
            float target = _left > 0 ? 1f : 0f;
            float step = 1f / 40;                               // rampa di 5 ms
            for (int i = 0; i < n; i++)
            {
                if (_gain < target) _gain = Math.Min(target, _gain + step);
                else if (_gain > target) _gain = Math.Max(target, _gain - step / 8);   // chiude più piano (40 ms)
                int v = (int)(_x[i] * _gain);
                pcm[2 * i] = (byte)v; pcm[2 * i + 1] = (byte)(v >> 8);
            }
        }
    }
}
