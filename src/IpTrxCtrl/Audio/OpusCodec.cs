using System;
using Concentus.Enums;
using Concentus.Structs;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Audio compresso con Opus (dalla 0.3.6) tra i due PC: un blocco da 40 ms a 8 kHz (320 campioni) diventa
    /// un pacchetto di circa 80 byte invece di 640 (16 kbit/s invece di 128). Con la correzione in avanti (FEC)
    /// un pacchetto perso si ricostruisce dal successivo; se ne mancano di più il decodificatore li «inventa»
    /// (mascheramento delle perdite) invece di lasciare un buco nell'audio. Codice C# puro (Concentus): niente
    /// librerie native, gira uguale su Windows (anche 7), Linux, Raspberry Pi e Mac.
    /// </summary>
    public sealed class OpusTx
    {
        private readonly OpusEncoder _enc;
        private readonly short[] _pcm = new short[AudioEngine.FrameSamples];
        private readonly byte[] _out = new byte[1275];
        private readonly object _lock = new();

        public OpusTx(int bitrate = 16000)
        {
            _enc = OpusEncoder.Create(AudioEngine.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _enc.Bitrate = bitrate;
            _enc.UseInbandFEC = true;
            _enc.PacketLossPercent = 10;
            _enc.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            _enc.Complexity = 6;
        }

        /// <summary>PCM 16 bit LE (un blocco da 40 ms) → pacchetto Opus.</summary>
        public byte[] Encode(byte[] pcm, int len)
        {
            lock (_lock)
            {
                int n = Math.Min(len / 2, _pcm.Length);
                for (int i = 0; i < n; i++) _pcm[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8);
                for (int i = n; i < _pcm.Length; i++) _pcm[i] = 0;
                int bytes = _enc.Encode(_pcm, 0, _pcm.Length, _out, 0, _out.Length);
                var res = new byte[Math.Max(0, bytes)];
                Array.Copy(_out, res, res.Length);
                return res;
            }
        }
    }

    public sealed class OpusRx
    {
        private readonly OpusDecoder _dec = OpusDecoder.Create(AudioEngine.SampleRate, 1);
        private readonly short[] _pcm = new short[AudioEngine.FrameSamples];
        private uint _lastSeq;
        private readonly object _lock = new();

        public void Reset() { lock (_lock) _lastSeq = 0; }

        /// <summary>
        /// Pacchetto Opus con il suo numero → blocchi PCM 16 bit LE da riprodurre, in ordine: prima quelli persi
        /// (dal FEC del pacchetto se ne manca uno solo, altrimenti mascherati, al massimo 3), poi quello arrivato.
        /// </summary>
        public System.Collections.Generic.List<(uint seq, byte[] pcm)> Decode(uint seq, byte[] packet)
        {
            var res = new System.Collections.Generic.List<(uint, byte[])>();
            lock (_lock)
            {
                if (_lastSeq != 0 && seq <= _lastSeq && _lastSeq - seq < 1000) return res;   // doppione o fuori tempo
                long gap = _lastSeq == 0 ? 0 : (long)seq - _lastSeq - 1;
                if (gap > 0 && gap <= 3)
                {
                    for (long g = 0; g < gap; g++)
                    {
                        bool fec = g == gap - 1;   // l'ultimo perso si recupera dalla copia ridotta dentro questo pacchetto
                        int n;
                        try { n = fec ? _dec.Decode(packet, 0, packet.Length, _pcm, 0, _pcm.Length, true) : _dec.Decode(null, 0, 0, _pcm, 0, _pcm.Length, false); }
                        catch { n = 0; }
                        res.Add(((uint)(seq - gap + g), ToBytes(n)));
                    }
                }
                int m;
                try { m = _dec.Decode(packet, 0, packet.Length, _pcm, 0, _pcm.Length, false); }
                catch { m = 0; }
                res.Add((seq, ToBytes(m)));
                _lastSeq = seq;
            }
            return res;
        }

        private byte[] ToBytes(int n)
        {
            n = Math.Clamp(n, 0, _pcm.Length);
            var b = new byte[n * 2];
            for (int i = 0; i < n; i++) { b[2 * i] = (byte)_pcm[i]; b[2 * i + 1] = (byte)(_pcm[i] >> 8); }
            return b;
        }
    }
}
