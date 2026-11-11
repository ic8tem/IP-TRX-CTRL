using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Chiave master dell'abbinamento.
    /// Windows: DPAPI dell'utente. macOS: Portachiavi (comando security).
    /// Linux/Raspberry: file cifrato con chiave legata a macchina e utente, permessi 600.
    /// </summary>
    public static class KeyStore
    {
        private const string Service = "IP-TRX-CTRL";
        private const string Account = "pairing-master";
        private static string KeyPath => Path.Combine(AppSettings.Folder, "pairing.key");
        // id = null: chiave unica (server, e client fino alla 0.2.x); altrimenti la chiave di un server del client
        private static string PathFor(string? id) => id == null ? KeyPath : Path.Combine(AppSettings.Folder, $"pairing-{id}.key");
        private static string AccountFor(string? id) => id == null ? Account : $"pairing-{id}";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IP-TRX-CTRL-pairing-v1");

        public static byte[]? LoadMaster(string? id = null)
        {
            try
            {
                byte[]? key = null;
                string path = PathFor(id);
                if (OperatingSystem.IsWindows())
                {
                    if (File.Exists(path))
                        key = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    string hex = Run("security", $"find-generic-password -s {Service} -a {AccountFor(id)} -w").Trim();
                    if (hex.Length == 64) key = Convert.FromHexString(hex);
                }
                else
                {
                    if (File.Exists(path)) key = Crypto.Open(LocalKey(), File.ReadAllBytes(path));
                }
                return key != null && key.Length == 32 ? key : null;
            }
            catch { return null; }
        }

        public static void SaveMaster(byte[] key, string? id = null)
        {
            string path = PathFor(id);
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(path, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("security", $"add-generic-password -U -s {Service} -a {AccountFor(id)} -w {Convert.ToHexString(key)}");
            }
            else
            {
                File.WriteAllBytes(path, Crypto.Seal(LocalKey(), key));
                try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
        }

        public static void DeleteMaster(string? id = null)
        {
            try
            {
                if (OperatingSystem.IsMacOS()) Run("security", $"delete-generic-password -s {Service} -a {AccountFor(id)}");
                else File.Delete(PathFor(id));
            }
            catch { }
        }

        private static byte[] LocalKey()
        {
            string machine = "";
            foreach (string p in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            {
                try { if (File.Exists(p)) { machine = File.ReadAllText(p).Trim(); break; } } catch { }
            }
            byte[] ikm = Encoding.UTF8.GetBytes(machine + "|" + Environment.UserName + "|" + Environment.MachineName);
            return Crypto.Hkdf(ikm, Entropy, "ip-trx-ctrl-local-key");
        }

        private static string Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
    }
}
