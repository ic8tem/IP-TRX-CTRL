using System;
using System.Collections.Generic;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Accesso nativo alla scheda audio, sempre a 8 kHz, 16 bit, mono, in modalità condivisa:
    /// il sistema operativo converte dalla frequenza della scheda.
    /// </summary>
    public interface IAudioBackend : IDisposable
    {
        List<string> Inputs();
        List<string> Outputs();

        /// <summary>
        /// Avvia cattura e riproduzione. onCapture riceve blocchi da FrameSamples campioni;
        /// fill deve riempire il blocco di riproduzione da FrameSamples campioni.
        /// </summary>
        void Start(string input, string output, Action<short[]> onCapture, Action<short[]> fill);
        void Stop();
        string LastError { get; }
    }

    public static class AudioBackends
    {
        public static IAudioBackend Create()
        {
            if (OperatingSystem.IsWindows()) return new WinMmBackend();
            if (OperatingSystem.IsMacOS()) return new MacBackend();
            return new AlsaBackend();
        }
    }
}
