using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IpTrxCtrl.Core;

namespace IpTrxCtrl.Rig
{
    public sealed record RigModelInfo(int Id, string Manufacturer, string Model)
    {
        public override string ToString() => $"{Manufacturer} {Model}".Trim();
    }

    /// <summary>
    /// Ricerca di Hamlib (rigctld/rigctl).
    /// Windows: incluso nell'eseguibile ed estratto nella cartella dati locale.
    /// Linux/Raspberry: pacchetto del sistema (libhamlib-utils / hamlib) o percorso impostato a mano.
    /// macOS: Homebrew (/opt/homebrew/bin, /usr/local/bin) o percorso impostato a mano.
    /// </summary>
    public static class Hamlib
    {
        private const string Prefix = "hamlib/";
        private static string? _dir;
        private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

        public static bool IsEmbedded =>
            Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(n => n.Equals(Prefix + "rigctld.exe", StringComparison.OrdinalIgnoreCase));

        public static string EmbeddedVersion => (ReadText("VERSION.txt") ?? "").Trim();

        public static string? ReadText(string name)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + name);
            if (s == null) return null;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        private static string Extract()
        {
            if (_dir != null) return _dir;
            var asm = Assembly.GetExecutingAssembly();
            string ver = EmbeddedVersion;
            if (ver.Length == 0) ver = "x";
            foreach (char c in Path.GetInvalidFileNameChars()) ver = ver.Replace(c, '_');
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                                      "IP-TRX-CTRL", "hamlib", ver);
            Directory.CreateDirectory(dir);
            foreach (string res in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix)))
            {
                string target = Path.Combine(dir, res.Substring(Prefix.Length));
                using var s = asm.GetManifestResourceStream(res)!;
                if (File.Exists(target) && new FileInfo(target).Length == s.Length) continue;
                try
                {
                    using var f = File.Create(target);
                    s.CopyTo(f);
                }
                catch (IOException) { /* file in uso da un rigctld già avviato */ }
            }
            _dir = dir;
            return dir;
        }

        private static readonly string[] UnixDirs = { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" };

        /// <summary>Percorso di un programma Hamlib, o null se non trovato.</summary>
        public static string? Find(string name, AppSettings? s = null)
        {
            if (s != null && !string.IsNullOrWhiteSpace(s.RigctldPath))
            {
                string dir = Path.GetDirectoryName(s.RigctldPath) ?? "";
                string p = Path.Combine(dir, Exe(name));
                if (File.Exists(p)) return p;
            }
            if (OperatingSystem.IsWindows() && IsEmbedded) return Path.Combine(Extract(), Exe(name));
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Concat(UnixDirs))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try
                {
                    string p = Path.Combine(d, Exe(name));
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        public static string InstallHint =>
            OperatingSystem.IsWindows() ? "Hamlib non incluso: imposta il percorso di rigctld.exe nelle Impostazioni."
            : OperatingSystem.IsMacOS() ? "Hamlib non trovato: installalo con «brew install hamlib» oppure imposta il percorso di rigctld."
            : "Hamlib non trovato: installalo con «sudo apt install libhamlib-utils» oppure imposta il percorso di rigctld.";

        public static string Version(AppSettings? s = null)
        {
            if (OperatingSystem.IsWindows() && IsEmbedded) return EmbeddedVersion;
            string? p = Find("rigctld", s);
            if (p == null) return "";
            string o = RunSync(p, "--version", 4000);
            var m = Regex.Match(o, @"Hamlib\s+([\w\.\-~]+)");
            return m.Success ? m.Groups[1].Value : o.Trim();
        }

        public static async Task<List<RigModelInfo>> ListModelsAsync(AppSettings? s = null)
        {
            var list = new List<RigModelInfo>();
            string? rigctl = Find("rigctl", s);
            if (rigctl == null) return list;
            string output = await RunAsync(rigctl, "-l", 15000);
            var rx = new Regex(@"^\s*(\d+)\s{2,}(.+?)\s{2,}(.+?)\s{2,}", RegexOptions.Multiline);
            foreach (Match m in rx.Matches(output))
            {
                if (int.TryParse(m.Groups[1].Value, out int id))
                    list.Add(new RigModelInfo(id, m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim()));
            }
            return list.OrderBy(x => x.Manufacturer).ThenBy(x => x.Model).ToList();
        }

        public static string[] SerialPorts()
        {
            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames().ToList();
                if (!OperatingSystem.IsWindows())
                {
                    foreach (string pattern in new[] { "ttyUSB*", "ttyACM*", "cu.usbserial*", "cu.SLAB*", "cu.usbmodem*" })
                    {
                        try { ports.AddRange(Directory.GetFiles("/dev", pattern)); } catch { }
                    }
                    try { if (Directory.Exists("/dev/serial/by-id")) ports.AddRange(Directory.GetFiles("/dev/serial/by-id")); } catch { }
                }
                return ports.Distinct().OrderBy(x => x).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        /// <summary>Prova porte seriali e velocità finché la radio risponde con una frequenza.</summary>
        public static async Task<(string com, int baud)?> DetectAsync(int model, AppSettings s, IProgress<string>? progress = null)
        {
            string? rigctl = Find("rigctl", s);
            if (rigctl == null) return null;
            int[] bauds = { 38400, 19200, 115200, 9600, 4800, 57600 };
            foreach (string com in SerialPorts())
            {
                foreach (int baud in bauds)
                {
                    progress?.Report($"Provo {com} a {baud}…");
                    string outp = await RunAsync(rigctl, $"-m {model} -r {com} -s {baud} f", 4000);
                    string first = outp.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
                    if (long.TryParse(first, out long f) && f > 0) return (com, baud);
                }
            }
            return null;
        }

        /// <summary>rigctld «Dummy» (modello 1): con il CAT via TCI risponde ai comandi non legati alla radio
        /// (\dump_state, \chk_vfo…) per i programmi Hamlib. Null se rigctld non c'è: il TCI funziona lo stesso.</summary>
        public static Process? StartDummyRigctld(AppSettings s, int localPort)
        {
            string? exe = Find("rigctld", s);
            if (exe == null) return null;
            var psi = new ProcessStartInfo(exe, $"-m 1 -T 127.0.0.1 -t {localPort}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            };
            try
            {
                var p = Process.Start(psi);
                if (p == null) return null;
                p.OutputDataReceived += (_, _) => { };
                p.ErrorDataReceived += (_, _) => { };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return p;
            }
            catch { return null; }
        }

        public static Process? StartRigctld(AppSettings s, int localPort)
        {
            string? exe = Find("rigctld", s);
            if (exe == null) return null;
            var args = new StringBuilder();
            args.Append($"-m {s.RigModel} -r {s.RigCom} -s {s.RigBaud} -T 127.0.0.1 -t {localPort}");
            // RTS/DTR su una porta diversa da quella del CAT li comanda direttamente il programma (anche per il CW)
            var plan = KeyingPlan.Resolve(s);
            if (s.PttMethod == PttMethod.Cat) args.Append(" -P RIG");
            else if (plan.PttViaRigctld) args.Append($" -P {KeyingPlan.Name(plan.PttLine)} -p {plan.PttPort}");
            else args.Append(" -P NONE");
            var psi = new ProcessStartInfo(exe, args.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            };
            var p = Process.Start(psi);
            if (p == null) return null;
            p.OutputDataReceived += (_, _) => { };
            p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        private static string RunSync(string exe, string args, int timeoutMs)
        {
            try { return RunAsync(exe, args, timeoutMs).GetAwaiter().GetResult(); } catch { return ""; }
        }

        private static async Task<string> RunAsync(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)!
                };
                using var p = Process.Start(psi)!;
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                var exit = p.WaitForExitAsync();
                if (await Task.WhenAny(exit, Task.Delay(timeoutMs)) != exit)
                {
                    try { p.Kill(true); } catch { }
                    return "";
                }
                return await outTask + "\n" + await errTask;
            }
            catch { return ""; }
        }
    }
}
