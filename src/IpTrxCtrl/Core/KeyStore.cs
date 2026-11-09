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
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IP-TRX-CTRL-pairing-v1");

        public static byte[]? LoadMaster()
        {
            try
            {
                byte[]? key = null;
                if (OperatingSystem.IsWindows())
                {
                    if (File.Exists(KeyPath))
                        key = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, DataProtectionScope.CurrentUser);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    string hex = Run("security", $"find-generic-password -s {Service} -a {Account} -w").Trim();
                    if (hex.Length == 64) key = Convert.FromHexString(hex);
                }
                else
                {
                    if (File.Exists(KeyPath)) key = Crypto.Open(LocalKey(), File.ReadAllBytes(KeyPath));
                }
                return key != null && key.Length == 32 ? key : null;
            }
            catch { return null; }
        }

        public static void SaveMaster(byte[] key)
        {
            if (OperatingSystem.IsWindows())
            {
                File.WriteAllBytes(KeyPath, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("security", $"add-generic-password -U -s {Service} -a {Account} -w {Convert.ToHexString(key)}");
            }
            else
            {
                File.WriteAllBytes(KeyPath, Crypto.Seal(LocalKey(), key));
                try { File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
        }

        public static void DeleteMaster()
        {
            try
            {
                if (OperatingSystem.IsMacOS()) Run("security", $"delete-generic-password -s {Service} -a {Account}");
                else File.Delete(KeyPath);
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
