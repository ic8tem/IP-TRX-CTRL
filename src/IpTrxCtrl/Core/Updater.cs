using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Aggiornamento automatico dalle release di GitHub (dalla 0.3.2).
    ///  - si legge l'ultima release (api.github.com/repos/ic8tem/IP-TRX-CTRL/releases/latest) e, se è più nuova, si
    ///    scarica il pacchetto adatto a questo PC: zip portatile (Windows, anche installato con il Setup), tar.gz
    ///    portatile (Linux, Raspberry Pi, Alpine), AppImage, IP-TRX-CTRL.app in zip (Mac);
    ///  - si controllano dimensione e impronta SHA-256 dichiarate da GitHub, poi si sostituiscono i file sul posto
    ///    (su Windows l'eseguibile in uso si rinomina in *.old-aggiornamento e si cancella al riavvio; su Mac si
    ///    sostituisce l'intero IP-TRX-CTRL.app, per non rompere la firma);
    ///  - i pacchetti dei gestori di sistema (.deb, .rpm, Arch, Slackware) stanno in /opt e appartengono a root:
    ///    lì il programma non si sostituisce da solo e si limita ad avvisare;
    ///  - impostazioni, chiavi, abbinamenti e registri stanno nella cartella dell'utente e non si toccano.
    /// </summary>
    public static class Updater
    {
        public const string OldSuffix = ".old-aggiornamento";
        private const string StageName = ".IP-TRX-CTRL-aggiornamento";

        public enum Kind { Portable, AppImage, MacApp, Package }

        public sealed class Release
        {
            public string Version = "";
            public string PageUrl = Support.ReleasesUrl;
            public string? AssetName, AssetUrl, Sha256;
            public long AssetSize;
        }

        public sealed class Staged
        {
            public Release Release = new();
            public string Dir = "";        // cartella con i file nuovi
            public string File = "";       // AppImage: il file nuovo
        }

        // ================================================================ dove e come è installato

        public static bool IsWin => OperatingSystem.IsWindows();
        public static bool IsMac => OperatingSystem.IsMacOS();

        public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, IsWin ? "IP-TRX-CTRL.exe" : "IP-TRX-CTRL");
        public static string ExeDir => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;
        public static string? AppImagePath => Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } a && File.Exists(a) ? a : null;

        /// <summary>IP-TRX-CTRL.app in esecuzione (solo Mac), altrimenti null.</summary>
        public static string? MacBundle
        {
            get
            {
                if (!IsMac) return null;
                string b = ExePath;
                int i = b.IndexOf(".app" + Path.DirectorySeparatorChar + "Contents", StringComparison.OrdinalIgnoreCase);
                return i > 0 ? b.Substring(0, i + 4) : null;
            }
        }

        /// <summary>Pacchetto del gestore di sistema («deb», «rpm», «arch», «slackware»), scritto nel file PACCHETTO accanto al programma.</summary>
        public static string PackageName
        {
            get
            {
                try
                {
                    string f = Path.Combine(ExeDir, "PACCHETTO");
                    return File.Exists(f) ? File.ReadAllText(f).Trim() : "";
                }
                catch { return ""; }
            }
        }

        public static Kind InstallKind
        {
            get
            {
                if (AppImagePath != null) return Kind.AppImage;
                if (PackageName.Length > 0) return Kind.Package;
                if (MacBundle != null) return Kind.MacApp;
                return Kind.Portable;
            }
        }

        /// <summary>Codice della compilazione (win-x64, linux-musl-arm64, osx-arm64…).</summary>
        public static string Rid
        {
            get
            {
                foreach (var a in typeof(Updater).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
                    if (a is AssemblyMetadataAttribute m && m.Key == "Rid" && !string.IsNullOrEmpty(m.Value)) return m.Value;
                string r = RuntimeInformation.RuntimeIdentifier ?? "";
                if (r.Contains('-') && !r.Contains('.')) return r;   // «ubuntu.24.04-x64» non è un codice di compilazione
                string arch = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X86 => "x86", Architecture.Arm64 => "arm64", Architecture.Arm => "arm", _ => "x64"
                };
                return (IsWin ? "win-" : IsMac ? "osx-" : "linux-") + arch;
            }
        }

        /// <summary>Fine del nome del file della release adatto a questo PC (null se non c'è).</summary>
        public static string? AssetSuffix()
        {
            switch (InstallKind)
            {
                case Kind.AppImage:
                    return RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.X64 => "-x86_64.AppImage", Architecture.Arm64 => "-aarch64.AppImage", _ => null
                    };
                case Kind.MacApp: return "-" + Rid + "-app.zip";
                case Kind.Package: return null;
                default: return "-" + Rid + (IsWin ? "-portatile.zip" : "-portatile.tar.gz");
            }
        }

        /// <summary>Cartella in cui si scrivono i file nuovi.</summary>
        private static string TargetDir => InstallKind switch
        {
            Kind.AppImage => Path.GetDirectoryName(AppImagePath!)!,
            Kind.MacApp => Path.GetDirectoryName(MacBundle!)!,
            _ => ExeDir
        };

        private static bool CanWrite(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".prova-scrittura-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(f, "");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Il programma può sostituirsi da solo? Se no, <paramref name="why"/> dice perché e cosa fare.</summary>
        public static bool CanSelfUpdate(out string why)
        {
            why = "";
            var k = InstallKind;
            if (k == Kind.Package)
            {
                string p = PackageName;
                why = p switch
                {
                    "deb" => "installato con il pacchetto .deb: per aggiornarlo scarica il .deb nuovo e installalo con «sudo apt install ./ip-trx-ctrl_…deb»",
                    "rpm" => "installato con il pacchetto .rpm: per aggiornarlo scarica il .rpm nuovo e installalo con «sudo dnf install ./ip-trx-ctrl-…rpm» (o zypper)",
                    "arch" => "installato con il pacchetto di Arch: per aggiornarlo scarica il pacchetto nuovo e installalo con «sudo pacman -U ./ip-trx-ctrl-…pkg.tar.zst»",
                    "slackware" => "installato con il pacchetto di Slackware: per aggiornarlo scarica il .txz nuovo e installalo da root con «upgradepkg ip-trx-ctrl-…txz»",
                    _ => "installato con il gestore dei pacchetti del sistema: per aggiornarlo installa il pacchetto nuovo allo stesso modo"
                };
                return false;
            }
            if (AssetSuffix() == null) { why = "per questo sistema non c'è un pacchetto che si aggiorna da solo"; return false; }
            if (!CanWrite(TargetDir))
            {
                why = k == Kind.MacApp
                    ? "IP-TRX-CTRL.app è in una cartella in cui non puoi scrivere (o è ancora dentro il .dmg): spostalo in Applicazioni"
                    : $"la cartella del programma ({TargetDir}) non è scrivibile con il tuo utente: installa la versione nuova a mano";
                return false;
            }
            return true;
        }

        // ================================================================ ultima release

        private static HttpClient Http(TimeSpan timeout)
        {
            var http = new HttpClient { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"IP-TRX-CTRL/{AppVersion.Text}");
            return http;
        }

        /// <summary>Ultima release pubblicata (null se non si legge). GitHub vede l'indirizzo IP di chi chiede, come per qualunque pagina web.</summary>
        public static async Task<Release?> GetLatestAsync(CancellationToken ct)
        {
            try
            {
                using var http = Http(TimeSpan.FromSeconds(15));
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                string json = await http.GetStringAsync($"https://api.github.com/repos/{Support.Repo}/releases/latest", ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var r = new Release();
                string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                r.Version = tag.TrimStart('v', 'V').Trim();
                if (root.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String) r.PageUrl = h.GetString() ?? r.PageUrl;
                string? suf = AssetSuffix();
                if (suf != null && root.TryGetProperty("assets", out var assets))
                    foreach (var a in assets.EnumerateArray())
                    {
                        string n = a.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
                        if (!n.StartsWith("IP-TRX-CTRL-", StringComparison.Ordinal) || !n.EndsWith(suf, StringComparison.Ordinal)) continue;
                        r.AssetName = n;
                        r.AssetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        r.AssetSize = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                        if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String && (d.GetString() ?? "").StartsWith("sha256:"))
                            r.Sha256 = d.GetString()!.Substring(7).ToLowerInvariant();
                        break;
                    }
                return r;
            }
            catch (Exception ex)
            {
                SessionLog.Write("AGGIORNAMENTI controllo non riuscito: " + ex.Message);
                return null;
            }
        }

        // ================================================================ scaricamento

        private static string StageRoot => InstallKind == Kind.MacApp
            ? Path.Combine(TargetDir, StageName)                       // stesso disco di IP-TRX-CTRL.app: lo scambio è uno spostamento
            : Path.Combine(AppSettings.Folder, "aggiornamento");

        /// <summary>Scarica e controlla il pacchetto nuovo, lo estrae in una cartella a parte. Non tocca il programma in uso.</summary>
        public static async Task<Staged> DownloadAsync(Release r, Action<int>? progress, CancellationToken ct)
        {
            if (r.AssetUrl == null || r.AssetName == null) throw new InvalidOperationException("nella release non c'è un pacchetto per questo PC");
            string dir = StageRoot;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, r.AssetName);

            using (var http = Http(TimeSpan.FromMinutes(15)))
            using (var resp = await http.GetAsync(r.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? r.AssetSize;
                await using var rs = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(file);
                var buf = new byte[81920];
                long got = 0; int n, last = -1;
                while ((n = await rs.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    got += n;
                    int p = total > 0 ? (int)(got * 100 / total) : -1;
                    if (p != last) { last = p; progress?.Invoke(p); }
                }
            }

            long len = new FileInfo(file).Length;
            if (r.AssetSize > 0 && len != r.AssetSize) throw new IOException($"pacchetto incompleto ({len} byte invece di {r.AssetSize})");
            if (r.Sha256 != null)
            {
                string h;
                using (var f = File.OpenRead(file)) h = Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
                if (h != r.Sha256) throw new IOException("l'impronta SHA-256 del pacchetto non corrisponde a quella pubblicata su GitHub");
            }

            var st = new Staged { Release = r };
            if (InstallKind == Kind.AppImage)
            {
                MakeExecutable(file);
                st.File = file;
                return st;
            }
            string outDir = Path.Combine(dir, "nuova");
            Directory.CreateDirectory(outDir);
            if (IsMac)
            {
                // ditto conserva permessi, attributi e firma del pacchetto .app
                var p = Process.Start(new ProcessStartInfo("/usr/bin/ditto") { ArgumentList = { "-x", "-k", file, outDir }, UseShellExecute = false })
                        ?? throw new IOException("ditto non avviato");
                await p.WaitForExitAsync(ct);
                if (p.ExitCode != 0) throw new IOException($"ditto non ha estratto il pacchetto (codice {p.ExitCode})");
            }
            else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(file, outDir, true);
            else
            {
                await using var fs = File.OpenRead(file);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, outDir, true, ct);
            }
            st.Dir = outDir;
            return st;
        }

        // ================================================================ installazione

        private static void MakeExecutable(string f)
        {
            if (IsWin || !File.Exists(f)) return;
            try
            {
                File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { }
        }

        private static string? FindRoot(string dir, string marker)
        {
            if (File.Exists(Path.Combine(dir, marker)) || Directory.Exists(Path.Combine(dir, marker))) return dir;
            foreach (string d in Directory.GetDirectories(dir))
                if (File.Exists(Path.Combine(d, marker)) || Directory.Exists(Path.Combine(d, marker))) return d;
            return null;
        }

        /// <summary>Sostituisce un file anche se è in uso: su Windows si rinomina quello vecchio, su Linux si rinomina il nuovo sopra il vecchio.</summary>
        private static void Replace(string src, string dst)
        {
            if (!File.Exists(dst)) { File.Copy(src, dst); return; }
            if (IsWin)
            {
                try { File.Copy(src, dst, true); }
                catch (Exception)
                {
                    string old = dst + OldSuffix;
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(dst, old);
                    File.Copy(src, dst);
                }
            }
            else
            {
                string tmp = dst + ".nuovo";
                File.Copy(src, tmp, true);
                if (Path.GetFileName(dst) == "IP-TRX-CTRL") MakeExecutable(tmp);
                File.Move(tmp, dst, true);   // il programma in esecuzione tiene il file vecchio fino all'uscita
            }
        }

        /// <summary>Mette al suo posto la versione scaricata (pochi istanti) e restituisce il comando per riavviare il programma.</summary>
        public static ProcessStartInfo Install(Staged st)
        {
            string pid = Environment.ProcessId.ToString();
            switch (InstallKind)
            {
                case Kind.AppImage:
                {
                    string target = AppImagePath!;
                    File.Move(st.File, target, true);
                    MakeExecutable(target);
                    return new ProcessStartInfo(target) { UseShellExecute = false, ArgumentList = { "--dopo-aggiornamento", pid } };
                }
                case Kind.MacApp:
                {
                    string bundle = MacBundle!;
                    string? root = FindRoot(st.Dir, "IP-TRX-CTRL.app") ?? throw new IOException("IP-TRX-CTRL.app non trovato nel pacchetto");
                    string newApp = Path.Combine(root, "IP-TRX-CTRL.app");
                    MakeExecutable(Path.Combine(newApp, "Contents", "MacOS", "IP-TRX-CTRL"));
                    string old = bundle + OldSuffix;
                    if (Directory.Exists(old)) Directory.Delete(old, true);
                    Directory.Move(bundle, old);
                    try { Directory.Move(newApp, bundle); }
                    catch { Directory.Move(old, bundle); throw; }
                    return new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, ArgumentList = { "-n", bundle, "--args", "--dopo-aggiornamento", pid } };
                }
                default:
                {
                    string exeName = IsWin ? "IP-TRX-CTRL.exe" : "IP-TRX-CTRL";
                    string root = FindRoot(st.Dir, exeName) ?? throw new IOException(exeName + " non trovato nel pacchetto");
                    string target = ExeDir;
                    // prima l'eseguibile: se non si riesce a sostituirlo, il resto non cambia
                    Replace(Path.Combine(root, exeName), Path.Combine(target, Path.GetFileName(ExePath)));
                    foreach (string f in Directory.GetFiles(root))
                    {
                        if (Path.GetFileName(f) == exeName) continue;
                        try { Replace(f, Path.Combine(target, Path.GetFileName(f))); } catch { /* licenze, icona: non indispensabili */ }
                    }
                    return new ProcessStartInfo(ExePath) { UseShellExecute = false, WorkingDirectory = target, ArgumentList = { "--dopo-aggiornamento", pid } };
                }
            }
        }

        /// <summary>All'avvio: aspetta che la versione vecchia sia uscita e toglie i resti dell'aggiornamento.</summary>
        public static void AfterRestart(string[] args)
        {
            int i = Array.IndexOf(args, "--dopo-aggiornamento");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int pid))
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.WaitForExit(30000);
                }
                catch { /* già uscito */ }
                SessionLog.Write($"AGGIORNAMENTI avviata la versione {AppVersion.Text} dopo l'aggiornamento");
            }
            Cleanup();
        }

        public static void Cleanup()
        {
            try
            {
                string? b = MacBundle;
                if (b != null)
                {
                    if (Directory.Exists(b + OldSuffix)) Directory.Delete(b + OldSuffix, true);
                    string st = Path.Combine(Path.GetDirectoryName(b) ?? "", StageName);
                    if (Directory.Exists(st)) Directory.Delete(st, true);
                }
                foreach (string f in Directory.GetFiles(ExeDir, "*" + OldSuffix))
                    try { File.Delete(f); } catch { }
                string stage = Path.Combine(AppSettings.Folder, "aggiornamento");
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
            }
            catch { }
        }
    }
}
