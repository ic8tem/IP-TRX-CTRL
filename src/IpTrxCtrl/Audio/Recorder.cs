using System;
using System.IO;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Audio
{
    /// <summary>
    /// Registrazione dell'audio in un file WAV (dalla 0.3.7, idea delle registrazioni di rustyrig): 8 kHz, 16 bit
    /// mono, con quello che si sente (ricezione) e quello che si trasmette, nell'ordine in cui passano. I file
    /// vanno nella cartella «registrazioni» accanto alle impostazioni, con data, ora, frequenza e nominativo.
    /// </summary>
    public sealed class Recorder
    {
        private readonly object _lock = new();
        private FileStream? _fs;
        private long _bytes;
        public string? CurrentFile { get; private set; }
        public bool Active { get { lock (_lock) return _fs != null; } }
        public TimeSpan Length { get { lock (_lock) return TimeSpan.FromSeconds(_bytes / 2.0 / AudioEngine.SampleRate); } }
        public event Action? Changed;

        public static string FolderPath => Path.Combine(AppSettings.Folder, "registrazioni");

        public string Start(long freqHz, string call)
        {
            lock (_lock)
            {
                StopLocked();
                Directory.CreateDirectory(FolderPath);
                string safeCall = string.Concat((call ?? "").ToUpperInvariant().Split(Path.GetInvalidFileNameChars()));
                string name = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}Z"
                              + (freqHz > 0 ? $"_{freqHz / 1000.0:0.0}kHz".Replace(',', '.') : "")
                              + (safeCall.Length > 0 ? "_" + safeCall : "") + ".wav";
                string path = Path.Combine(FolderPath, name);
                _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                _fs.Write(new byte[44]);   // intestazione scritta alla fine, con le lunghezze
                _bytes = 0;
                CurrentFile = path;
            }
            SessionLog.Write("REGISTRAZIONE avviata: " + CurrentFile);
            Changed?.Invoke();
            return CurrentFile!;
        }

        public void Write(byte[] pcm, int len)
        {
            lock (_lock)
            {
                if (_fs == null) return;
                try { _fs.Write(pcm, 0, len); _bytes += len; }
                catch { StopLocked(); }
            }
        }

        public string? Stop()
        {
            string? f;
            lock (_lock) { f = CurrentFile; if (_fs == null) return null; StopLocked(); }
            SessionLog.Write("REGISTRAZIONE salvata: " + f);
            Changed?.Invoke();
            return f;
        }

        /// <summary>Ferma e aggiunge il nominativo al nome del file (registrazione automatica del QSO).</summary>
        public string? StopAs(string call)
        {
            string? f = Stop();
            string safe = string.Concat((call ?? "").ToUpperInvariant().Split(Path.GetInvalidFileNameChars()));
            if (f == null || safe.Length == 0) return f;
            try
            {
                string to = Path.Combine(Path.GetDirectoryName(f)!, Path.GetFileNameWithoutExtension(f) + "_" + safe + ".wav");
                File.Move(f, to);
                return to;
            }
            catch { return f; }
        }

        private void StopLocked()
        {
            if (_fs == null) return;
            try
            {
                _fs.Position = 0;
                _fs.Write(Header(_bytes));
                _fs.Flush();
            }
            catch { }
            _fs.Dispose();
            _fs = null;
        }

        private static byte[] Header(long dataBytes)
        {
            int rate = AudioEngine.SampleRate;
            var h = new byte[44];
            void Str(int o, string s) { for (int i = 0; i < 4; i++) h[o + i] = (byte)s[i]; }
            void I32(int o, int v) => BitConverter.GetBytes(v).CopyTo(h, o);
            void I16(int o, short v) => BitConverter.GetBytes(v).CopyTo(h, o);
            int data = (int)Math.Min(dataBytes, int.MaxValue - 44);
            Str(0, "RIFF"); I32(4, 36 + data); Str(8, "WAVE");
            Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, 1); I32(24, rate); I32(28, rate * 2); I16(32, 2); I16(34, 16);
            Str(36, "data"); I32(40, data);
            return h;
        }
    }
}
