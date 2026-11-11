using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IpTrxCtrl.Net
{
    /// <summary>Tipi di messaggio (primo byte del testo in chiaro di ogni frame).</summary>
    public static class Msg
    {
        public const byte Audio = 0x01;            // seq(u32) | pcm 16 bit
        public const byte Hello = 0x10;            // nominativo
        public const byte Ping = 0x11;             // ticks(i64)
        public const byte Pong = 0x12;             // ticks(i64)
        public const byte PeerInfo = 0x13;         // server -> client: IP del client visto dal server
        public const byte Telemetry = 0x20;        // server -> client
        public const byte SetFreqMode = 0x21;      // client -> server
        public const byte Ptt = 0x22;              // client -> server, ripetuto ogni 250 ms mentre premuto
        public const byte RadioStatus = 0x23;      // testo di stato della radio
        public const byte SetVfoB = 0x24;          // i64 Hz (frequenza di trasmissione in split)
        public const byte SetSplit = 0x25;         // u8
        public const byte VfoOp = 0x26;            // str: XCHG (A/B) o CPY (A=B)
        public const byte SetPower = 0x27;         // f32 valore | u8 1 = watt, 0 = relativo 0..1
        public const byte SetPitch = 0x28;         // i32 Hz
        public const byte SendMorse = 0x29;        // str testo
        public const byte StopMorse = 0x2A;
        public const byte SetKeySpeed = 0x2B;      // i32 WPM
        public const byte SetPassband = 0x2C;      // str modo | i32 Hz (filtro della radio)
        public const byte CwKey = 0x2D;            // u8 giù/su | i64 istante in ms (tasto manuale del client)
        public const byte SetServerDevices = 0x2E; // str ingresso | str uscita: il client sceglie le schede audio del server
        public const byte SwrUnlock = 0x2F;        // client -> server: sblocca la trasmissione bloccata per ROS alto
        public const byte SetSwrProtection = 0x32; // client -> server: u8 attiva | f32 soglia avviso | f32 soglia blocco
        public const byte Busy = 0x33;             // server -> client: str operatore collegato (server occupato, riprova più tardi)
        public const byte ServerNotice = 0x34;     // server -> client: str avviso per l'operatore (QSY rifiutato, tempo massimo…)
        public const byte SetRadioCtl = 0x35;      // client -> server: str nome (NR NB ANF COMP SQL ATT PREAMP AGC) | f32 valore (-1 = passo successivo)
        public const byte Memories = 0x36;         // server -> client: i32 n | n × (str id | str nome | i64 Hz | str modo)
        public const byte MemorySave = 0x37;       // client -> server: str nome | i64 Hz | str modo
        public const byte MemoryDelete = 0x38;     // client -> server: str id
        public const byte SpectrumOn = 0x39;       // client -> server: u8 panadapter voluto
        public const byte Spectrum = 0x3A;         // server -> client: i64 inizio | i64 fine | u8 fuori campo | i32 n | n byte 0..255
        public const byte RigTunnelOpen = 0x60;    // i32 id: il client apre un collegamento rigctld
        public const byte RigTunnelData = 0x61;    // i32 id | dati
        public const byte RigTunnelClose = 0x62;   // i32 id
        public const byte UdpOffer = 0x70;         // candidati UDP: str elenco "ip:porta;ip:porta" | i32 passo NAT simmetrico
        public const byte UdpAudioOn = 0x71;       // u8: audio su UDP attivo
        public const byte AudioUnreachable = 0x30; // client -> server
        public const byte NewAudioPort = 0x31;     // server -> client
        public const byte SwapRequest = 0x40;
        public const byte SwapAnswer = 0x41;       // 1 = accettato
        public const byte SwapReady = 0x42;        // indirizzo del nuovo server (8 byte)
        public const byte DeviceState = 0x50;      // server -> client: mute e volume delle schede audio
        public const byte SetDeviceMute = 0x51;    // u8 0 = ingresso, 1 = uscita | u8 mute
        public const byte SetDeviceVolume = 0x52;  // u8 0/1 | f32 0..1
        public const byte SetAutoUnmute = 0x53;    // u8
    }

    public sealed class MsgWriter
    {
        private readonly MemoryStream _ms = new();
        private readonly BinaryWriter _w;
        public MsgWriter(byte type) { _w = new BinaryWriter(_ms, Encoding.UTF8); _w.Write(type); }
        public MsgWriter I32(int v) { _w.Write(v); return this; }
        public MsgWriter U32(uint v) { _w.Write(v); return this; }
        public MsgWriter I64(long v) { _w.Write(v); return this; }
        public MsgWriter F32(float v) { _w.Write(v); return this; }
        public MsgWriter U8(byte v) { _w.Write(v); return this; }
        public MsgWriter Bool(bool v) { _w.Write((byte)(v ? 1 : 0)); return this; }
        public MsgWriter Str(string? v) { _w.Write(v ?? ""); return this; }
        public MsgWriter Bytes(byte[] v) { _w.Write(v); return this; }
        public MsgWriter Bytes(byte[] v, int offset, int count) { _w.Write(v, offset, count); return this; }
        public byte[] Build() { _w.Flush(); return _ms.ToArray(); }
    }

    public sealed class MsgReader
    {
        private readonly BinaryReader _r;
        public byte Type { get; }
        public MsgReader(byte[] data)
        {
            _r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            Type = _r.ReadByte();
        }
        public bool AtEnd => _r.BaseStream.Position >= _r.BaseStream.Length;
        public int I32() => _r.ReadInt32();
        public uint U32() => _r.ReadUInt32();
        public long I64() => _r.ReadInt64();
        public float F32() => _r.ReadSingle();
        public byte U8() => _r.ReadByte();
        public bool Bool() => _r.ReadByte() == 1;
        public string Str() => _r.ReadString();
        public byte[] Bytes(int count) => _r.ReadBytes(count);
        public byte[] Rest() => _r.ReadBytes((int)(_r.BaseStream.Length - _r.BaseStream.Position));
    }

    /// <summary>Telemetria della radio del server.</summary>
    public sealed class RigTelemetry
    {
        public long FreqHz;
        public string Mode = "";
        public long FreqBHz;                    // 0 = sconosciuta
        public bool Split;
        public float StrengthDb = float.NaN;    // dB rispetto a S9 (Hamlib STRENGTH)
        public float PowerW = float.NaN;        // potenza in uscita misurata
        public float PowerRel = float.NaN;      // 0..1 se i watt non sono disponibili
        public float Swr = float.NaN;
        public float TxGain = float.NaN;        // 0..1
        public int Mute = -1;                   // -1 sconosciuto
        public bool Transmitting;
        public bool CatOk;
        public float PowerSetRel = float.NaN;   // potenza impostata 0..1
        public float PowerSetW = float.NaN;     // potenza impostata in watt (NaN se la radio non converte)
        public float PowerMaxW = float.NaN;     // potenza massima in watt
        public int CwPitch;                     // 0 = sconosciuto
        public int KeySpeed;                    // 0 = sconosciuto
        public string RigName = "";
        public string PttInfo = "";
        public int PttDelayMs = 100;
        public string Status = "";
        public int PassbandHz;                  // 0 = sconosciuto
        // protezione ROS del server
        public bool SwrProtect;                 // protezione attiva
        public float SwrWarnAt = 1.9f;          // avviso: barra lampeggiante
        public float SwrTripAt = 2.5f;          // blocco della trasmissione
        public bool TxBlocked;                  // trasmissione bloccata per ROS alto
        public float TxBlockedSwr = float.NaN;  // ROS che ha fatto scattare il blocco
        // comandi della radio (-1 = sconosciuto o non disponibile con questa radio)
        public int Nr = -1, Nb = -1, Anf = -1, Comp = -1;   // 0 / 1
        public int Att = -1, Preamp = -1;                    // dB (o livello Hamlib), 0 = spento
        public int Agc = -1;                                 // valore Hamlib (2 FAST, 5 MID, 3 SLOW…)
        public float Sql = float.NaN;                        // 0..1
        public float Alc = float.NaN;                        // 0..1, in trasmissione
        // sicurezza in trasmissione
        public int TxSeconds;                   // durata della trasmissione in corso (comandata dal programma)
        public int TxLimit;                     // tempo massimo in secondi (0 = nessun limite)
        public bool TxTimedOut;                 // trasmissione interrotta dal tempo massimo: rilascia il PTT
        public int LocalLockSec;                // trasmissione remota bloccata: operatore alla radio (secondi che restano)
        public bool QsyLock;                    // niente cambi di frequenza in trasmissione
        public bool TxAllowed = true;           // questo client può trasmettere
        public string TxDenied = "";            // perché no (solo ascolto, operatore alla radio…)
        // panadapter della radio
        public bool PanAvailable;
        public string PanStatus = "";

        public bool PowerInWatts => !float.IsNaN(PowerSetW) && !float.IsNaN(PowerMaxW) && PowerMaxW > 0;

        public byte[] Encode() => new MsgWriter(Msg.Telemetry)
            .I64(FreqHz).Str(Mode).I64(FreqBHz).Bool(Split)
            .F32(StrengthDb).F32(PowerW).F32(PowerRel).F32(Swr).F32(TxGain)
            .I32(Mute).Bool(Transmitting).Bool(CatOk)
            .F32(PowerSetRel).F32(PowerSetW).F32(PowerMaxW)
            .I32(CwPitch).I32(KeySpeed).Str(RigName).Str(PttInfo).I32(PttDelayMs).Str(Status).I32(PassbandHz)
            .Bool(SwrProtect).F32(SwrWarnAt).F32(SwrTripAt).Bool(TxBlocked).F32(TxBlockedSwr)
            .I32(Nr).I32(Nb).I32(Anf).I32(Comp).I32(Att).I32(Preamp).I32(Agc).F32(Sql).F32(Alc)
            .I32(TxSeconds).I32(TxLimit).Bool(TxTimedOut).I32(LocalLockSec).Bool(QsyLock).Bool(TxAllowed).Str(TxDenied)
            .Bool(PanAvailable).Str(PanStatus).Build();

        public static RigTelemetry Decode(MsgReader r) => new()
        {
            // gli inizializzatori sono valutati nell'ordine scritto: è lo stesso ordine di Encode
            FreqHz = r.I64(), Mode = r.Str(), FreqBHz = r.I64(), Split = r.Bool(),
            StrengthDb = r.F32(), PowerW = r.F32(), PowerRel = r.F32(), Swr = r.F32(), TxGain = r.F32(),
            Mute = r.I32(), Transmitting = r.Bool(), CatOk = r.Bool(),
            PowerSetRel = r.F32(), PowerSetW = r.F32(), PowerMaxW = r.F32(),
            CwPitch = r.I32(), KeySpeed = r.I32(), RigName = r.Str(), PttInfo = r.Str(), PttDelayMs = r.I32(), Status = r.Str(),
            PassbandHz = r.I32(),
            SwrProtect = r.Bool(), SwrWarnAt = r.F32(), SwrTripAt = r.F32(), TxBlocked = r.Bool(), TxBlockedSwr = r.F32(),
            Nr = r.I32(), Nb = r.I32(), Anf = r.I32(), Comp = r.I32(), Att = r.I32(), Preamp = r.I32(), Agc = r.I32(), Sql = r.F32(), Alc = r.F32(),
            TxSeconds = r.I32(), TxLimit = r.I32(), TxTimedOut = r.Bool(), LocalLockSec = r.I32(), QsyLock = r.Bool(), TxAllowed = r.Bool(), TxDenied = r.Str(),
            PanAvailable = r.Bool(), PanStatus = r.Str()
        };

        /// <summary>Nome dell'AGC Hamlib.</summary>
        public static string AgcName(int v) => v switch
        {
            0 => "OFF", 1 => "S.FAST", 2 => "FAST", 3 => "SLOW", 4 => "USER", 5 => "MID", 6 => "AUTO", 7 => "LONG", 8 => "ON", _ => "—"
        };

        public RigTelemetry Clone() => (RigTelemetry)MemberwiseClone();

        public static bool IsCw(string? mode) => mode != null && mode.StartsWith("CW", StringComparison.OrdinalIgnoreCase);

        public static string SMeterText(float db)
        {
            if (float.IsNaN(db)) return "—";
            if (db <= 0)
            {
                int s = (int)Math.Round(9 + db / 6.0);
                return "S" + Math.Clamp(s, 0, 9);
            }
            return $"S9+{Math.Round(db)}";
        }

        /// <summary>Percentuale della barra S-meter: S0 = 0 %, S9 = 60 %, S9+60 = 100 %.</summary>
        public static double SMeterPercent(float db)
        {
            if (float.IsNaN(db)) return 0;
            if (db <= 0) return Math.Clamp((db + 54) / 54.0 * 60.0, 0, 60);
            return Math.Clamp(60 + db / 60.0 * 40.0, 60, 100);
        }
    }

    /// <summary>Una riga del panadapter della radio: livelli 0..255 da StartHz a EndHz.</summary>
    public sealed class SpectrumLine
    {
        public long StartHz, EndHz;
        public bool OutOfRange;
        public byte[] Bins = Array.Empty<byte>();

        public byte[] Encode() => new MsgWriter(Msg.Spectrum).I64(StartHz).I64(EndHz).Bool(OutOfRange).I32(Bins.Length).Bytes(Bins).Build();

        public static SpectrumLine Decode(MsgReader r)
        {
            var l = new SpectrumLine { StartHz = r.I64(), EndHz = r.I64(), OutOfRange = r.Bool() };
            int n = Math.Clamp(r.I32(), 0, 4096);
            l.Bins = r.Bytes(n);
            return l;
        }
    }

    /// <summary>Stato delle schede audio del server (mute del sistema operativo).</summary>
    public sealed class DeviceStateInfo
    {
        public string InName = "", OutName = "";
        public int InMute = -1, OutMute = -1;          // -1 sconosciuto
        public float InVol = float.NaN, OutVol = float.NaN;
        public bool AutoUnmute;
        public string Note = "";
        /// <summary>Schede audio presenti sul server (per sceglierle dal client).</summary>
        public List<string> Inputs = new(), Outputs = new();

        public byte[] Encode()
        {
            var w = new MsgWriter(Msg.DeviceState)
                .Str(InName).I32(InMute).F32(InVol).Str(OutName).I32(OutMute).F32(OutVol).Bool(AutoUnmute).Str(Note);
            w.I32(Inputs.Count); foreach (var x in Inputs) w.Str(x);
            w.I32(Outputs.Count); foreach (var x in Outputs) w.Str(x);
            return w.Build();
        }

        public static DeviceStateInfo Decode(MsgReader r)
        {
            var d = new DeviceStateInfo
            {
                InName = r.Str(), InMute = r.I32(), InVol = r.F32(), OutName = r.Str(), OutMute = r.I32(), OutVol = r.F32(),
                AutoUnmute = r.Bool(), Note = r.Str()
            };
            if (!r.AtEnd)
            {
                int n = Math.Clamp(r.I32(), 0, 256);
                for (int i = 0; i < n; i++) d.Inputs.Add(r.Str());
                n = Math.Clamp(r.I32(), 0, 256);
                for (int i = 0; i < n; i++) d.Outputs.Add(r.Str());
            }
            return d;
        }
    }
}
