using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpTrxCtrl.Core
{
    public enum NodeRole { Server, Client }

    /// <summary>Metodo con cui il server manda in trasmissione la radio.</summary>
    public enum PttMethod { Cat, Rts, Dtr, Vox, Tci }

    /// <summary>Come il server trasmette il testo CW: keyer della radio via CAT oppure tasto su linea seriale.</summary>
    public enum CwMethod { Cat, Dtr, Rts }

    public sealed class CwMacro
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>Impostazioni salvate nella cartella dati dell'utente (IP-TRX-CTRL/settings.json).</summary>
    public sealed class AppSettings
    {
        public bool FirstRunDone { get; set; }
        public NodeRole Role { get; set; } = NodeRole.Server;
        public string Callsign { get; set; } = "";
        public string PeerCallsign { get; set; } = "";
        public bool DeclarationAccepted { get; set; }
        public string Theme { get; set; } = "chiaro";

        // ---------------- Radio (solo server)
        public int RigModel { get; set; }
        public string RigModelName { get; set; } = "";
        public string RigCom { get; set; } = "";
        public int RigBaud { get; set; } = 38400;
        public PttMethod PttMethod { get; set; } = PttMethod.Cat;
        public string PttCom { get; set; } = "";
        public int PttDelayMs { get; set; } = 100;
        public CwMethod CwMethod { get; set; } = CwMethod.Cat;
        public string CwCom { get; set; } = "";       // vuoto = stessa porta del PTT
        public string TciHost { get; set; } = "127.0.0.1";
        public int TciPort { get; set; } = 40001;
        public string TxGainLevel { get; set; } = "MICGAIN";
        public bool UseExternalRigctld { get; set; }
        public string ExternalRigctldHost { get; set; } = "127.0.0.1";
        public int ExternalRigctldPort { get; set; } = 4532;
        public string RigctldPath { get; set; } = "";
        public bool AutoUnmute { get; set; }

        // ---------------- Audio
        public string InputDeviceServer { get; set; } = "";
        public string OutputDeviceServer { get; set; } = "";
        public string InputDeviceClient { get; set; } = "";
        public string OutputDeviceClient { get; set; } = "";

        [JsonIgnore]
        public string InputDevice
        {
            get => Role == NodeRole.Server ? InputDeviceServer : InputDeviceClient;
            set { if (Role == NodeRole.Server) InputDeviceServer = value; else InputDeviceClient = value; }
        }

        [JsonIgnore]
        public string OutputDevice
        {
            get => Role == NodeRole.Server ? OutputDeviceServer : OutputDeviceClient;
            set { if (Role == NodeRole.Server) OutputDeviceServer = value; else OutputDeviceClient = value; }
        }

        public int RxVolume { get; set; } = 80;
        public int TxVolume { get; set; } = 70;
        public int RxBoost { get; set; }
        public int TxBoost { get; set; }
        public int PrefillMs { get; set; } = 160;
        public bool PttWithSpace { get; set; } = true;

        // ---------------- Keyer WAV (client, modi fonia) - da WavPTT
        public List<string> WavMemories { get; set; } = new() { "", "", "", "", "", "", "", "" };
        public int KeyerVolume { get; set; } = 100;
        public int KeyerFilter { get; set; } = 1;      // 0 nessuno, 1 300-2700, 2 500-2400, 3 500-2100
        public int KeyerCompDb { get; set; } = 3;      // 0 off, 3..13

        // ---------------- Keyer CW (client, modo CW)
        public List<CwMacro> CwMacros { get; set; } = DefaultMacros();
        public int CwSpeedWpm { get; set; } = 24;
        public string CwCall { get; set; } = "";
        public int CwNr { get; set; } = 1;
        public string CwRst { get; set; } = "5NN";

        // ---------------- Decodificatore CW
        public int CwPitch { get; set; } = 600;
        public int CwDecodeWidth { get; set; } = 100;
        public int CwThreshold { get; set; } = 40;
        public int CwRxWpm { get; set; }               // 0 = automatico

        // ---------------- Rete (gestita in automatico)
        public string LastServerIp { get; set; } = "";
        public int LastServerControlPort { get; set; }
        public int LastServerAudioPort { get; set; }
        public List<string> MqttBrokers { get; set; } = new() { "broker.hivemq.com:1883", "test.mosquitto.org:1883", "broker.emqx.io:1883" };

        public static List<CwMacro> DefaultMacros() => new()
        {
            new() { Name = "CQ", Text = "CQ TEST {MYCALL} {MYCALL} TEST" },
            new() { Name = "Mio nominativo", Text = "{MYCALL}" },
            new() { Name = "Rapporto", Text = "{CALL} {RST} {NR}" },
            new() { Name = "QRZ?", Text = "QRZ? {MYCALL}" },
            new() { Name = "TU", Text = "TU {MYCALL} TEST" },
            new() { Name = "Ripeti", Text = "{NR} {NR}" },
            new() { Name = "AGN?", Text = "AGN?" },
            new() { Name = "73", Text = "{CALL} TU 73 {MYCALL} SK" },
        };

        public static string Folder
        {
            get
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "IP-TRX-CTRL");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        public static string WavFolder
        {
            get
            {
                string f = Path.Combine(Folder, "WAV");
                Directory.CreateDirectory(f);
                return f;
            }
        }

        private static string FilePath => Path.Combine(Folder, "settings.json");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            AppSettings s;
            try
            {
                s = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                    : new AppSettings();
            }
            catch { s = new AppSettings(); }
            while (s.WavMemories.Count < 8) s.WavMemories.Add("");
            var def = DefaultMacros();
            while (s.CwMacros.Count < 8) s.CwMacros.Add(def[s.CwMacros.Count]);
            s.CwPitch = Math.Clamp(s.CwPitch, 200, 1000);
            return s;
        }

        private readonly object _saveLock = new();

        public void Save()
        {
            lock (_saveLock)
            {
                try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts)); } catch { }
            }
        }

        /// <summary>Sostituisce le variabili delle macro CW.</summary>
        public string ExpandMacro(string text)
        {
            return (text ?? "")
                .Replace("{MYCALL}", Callsign, StringComparison.OrdinalIgnoreCase)
                .Replace("{CALL}", CwCall, StringComparison.OrdinalIgnoreCase)
                .Replace("{NR}", CwNr.ToString("D3"), StringComparison.OrdinalIgnoreCase)
                .Replace("{RST}", CwRst, StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();
        }
    }

    /// <summary>Registro delle sessioni, conservato per eventuali richieste delle autorità.</summary>
    public static class SessionLog
    {
        private static readonly object Lock = new();
        public static string FilePath => Path.Combine(AppSettings.Folder, "sessioni.log");

        public static void Write(string line)
        {
            lock (Lock)
            {
                try { File.AppendAllText(FilePath, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z | {line}{Environment.NewLine}"); } catch { }
            }
        }

        public static string ReadAll()
        {
            lock (Lock)
            {
                try { return File.Exists(FilePath) ? File.ReadAllText(FilePath) : ""; } catch { return ""; }
            }
        }
    }
}
