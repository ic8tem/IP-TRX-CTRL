using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace IpTrxCtrl.Core
{
    /// <summary>Indirizzo del server: IPv4 pubblico + porta controllo + porta audio.</summary>
    public readonly record struct ServerAddress(IPAddress Ip, int ControlPort, int AudioPort)
    {
        public byte[] ToBytes()
        {
            byte[] b = new byte[8];
            Ip.MapToIPv4().GetAddressBytes().CopyTo(b, 0);
            b[4] = (byte)(ControlPort >> 8); b[5] = (byte)ControlPort;
            b[6] = (byte)(AudioPort >> 8); b[7] = (byte)AudioPort;
            return b;
        }

        public static ServerAddress FromBytes(byte[] b, int offset)
        {
            var ip = new IPAddress(new[] { b[offset], b[offset + 1], b[offset + 2], b[offset + 3] });
            int pc = (b[offset + 4] << 8) | b[offset + 5];
            int pa = (b[offset + 6] << 8) | b[offset + 7];
            return new ServerAddress(ip, pc, pa);
        }

        public bool IsValid => ControlPort > 0 && AudioPort > 0 && !Ip.Equals(IPAddress.Any);
    }

    public static class Crypto
    {
        public const int KeySize = 32;      // AES-256
        public const int NonceSize = 12;
        public const int TagSize = 16;

        public static byte[] Hkdf(byte[] ikm, byte[] salt, string info, int length = KeySize)
            => HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, Encoding.UTF8.GetBytes(info));

        public static byte[] Hmac(byte[] key, params byte[][] parts)
        {
            using var h = new HMACSHA256(key);
            foreach (var p in parts) h.TransformBlock(p, 0, p.Length, null, 0);
            h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return h.Hash!;
        }

        public static bool Equal(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

        public static byte[] Concat(params byte[][] parts)
        {
            byte[] r = new byte[parts.Sum(p => p.Length)];
            int o = 0;
            foreach (var p in parts) { p.CopyTo(r, o); o += p.Length; }
            return r;
        }

        /// <summary>Formato: nonce(12) | tag(16) | testo cifrato.</summary>
        public static byte[] Seal(byte[] key, byte[] plain)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ct = new byte[plain.Length];
            byte[] tag = new byte[TagSize];
            using (var g = new AesGcm(key, TagSize)) g.Encrypt(nonce, plain, ct, tag);
            return Concat(nonce, tag, ct);
        }

        public static byte[] Open(byte[] key, byte[] sealedData)
        {
            if (sealedData.Length < NonceSize + TagSize) throw new CryptographicException("Pacchetto troppo corto");
            var nonce = sealedData.AsSpan(0, NonceSize);
            var tag = sealedData.AsSpan(NonceSize, TagSize);
            var ct = sealedData.AsSpan(NonceSize + TagSize);
            byte[] plain = new byte[ct.Length];
            using (var g = new AesGcm(key, TagSize)) g.Decrypt(nonce, ct, tag, plain);
            return plain;
        }

        // ---------- Base32 (alfabeto Crockford, senza lettere ambigue) ----------
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        public static string ToCode(byte[] data, int group = 4)
        {
            var sb = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b; bits += 8;
                while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            string s = sb.ToString();
            var outp = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && i % group == 0) outp.Append('-');
                outp.Append(s[i]);
            }
            return outp.ToString();
        }

        public static byte[]? FromCode(string code, int byteLength)
        {
            string s = code.ToUpperInvariant().Replace("-", "").Replace(" ", "")
                           .Replace('O', '0').Replace('I', '1').Replace('L', '1');
            var result = new byte[byteLength];
            int buffer = 0, bits = 0, idx = 0;
            foreach (char c in s)
            {
                int v = Alphabet.IndexOf(c);
                if (v < 0) return null;
                buffer = (buffer << 5) | v; bits += 5;
                if (bits >= 8)
                {
                    if (idx >= byteLength) return null;
                    result[idx++] = (byte)(buffer >> (bits - 8));
                    bits -= 8;
                }
            }
            return idx == byteLength ? result : null;
        }

        // ---------- Codice di abbinamento (solo prima installazione) ----------
        // segreto(10) | indirizzo(8) | controllo(1) = 19 byte
        public const int PairingSecretSize = 10;

        // ---------- Numero di abbinamento (dalla 0.3.0) ----------
        // 10 cifre a caso, valide 10 minuti e una sola volta. Il numero non contiene l'indirizzo del server
        // (arriva solo cifrato sul broker MQTT). Il segreto si ricava con PBKDF2-SHA256 lento e con la finestra
        // di 10 minuti come sale: provare tutti i 10 miliardi di numeri richiede troppo tempo, e una tabella
        // calcolata prima non serve perché ogni finestra ha il suo sale.
        public const int PairingDigits = 10;
        public const int PairingWindowMinutes = 10;
        private const int PairingIterations = 200_000;

        public static string MakePairingNumber()
        {
            var sb = new StringBuilder(PairingDigits);
            for (int i = 0; i < PairingDigits; i++) sb.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
            return sb.ToString();
        }

        /// <summary>«4829130571» → «482 913 0571».</summary>
        public static string FormatPairingNumber(string d) => d.Length == PairingDigits ? $"{d[..3]} {d[3..6]} {d[6..]}" : d;

        /// <summary>Tiene solo le cifre; vero se sono esattamente 10.</summary>
        public static bool TryNormalizePairingNumber(string input, out string digits)
        {
            digits = new string((input ?? "").Where(char.IsAsciiDigit).ToArray());
            return digits.Length == PairingDigits;
        }

        // ---------- Codice fisso del server ----------
        // 12 cifre che non scadono: per questo due cifre in più e un calcolo tre volte più lento del numero
        // temporaneo (provare tutti i codici richiederebbe anni di calcolo anche con hardware dedicato).
        public const int FixedCodeDigits = 12;
        private const int FixedIterations = 600_000;

        public static string MakeFixedCode()
        {
            var sb = new StringBuilder(FixedCodeDigits);
            for (int i = 0; i < FixedCodeDigits; i++) sb.Append((char)('0' + RandomNumberGenerator.GetInt32(10)));
            return sb.ToString();
        }

        /// <summary>«482913057712» → «4829 1305 7712».</summary>
        public static string FormatFixedCode(string d) => d.Length == FixedCodeDigits ? $"{d[..4]} {d[4..8]} {d[8..]}" : d;

        public static byte[] PairingSecretFromFixedCode(string digits) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(digits), Encoding.ASCII.GetBytes("IP-TRX-CTRL codice fisso del server v3"),
                FixedIterations, HashAlgorithmName.SHA256, KeySize);

        /// <summary>Solo le cifre: 10 = numero temporaneo, 12 = codice fisso del server.</summary>
        public static bool TryNormalizeAnyCode(string input, out string digits)
        {
            digits = new string((input ?? "").Where(char.IsAsciiDigit).ToArray());
            return digits.Length == PairingDigits || digits.Length == FixedCodeDigits;
        }

        /// <summary>Finestra di 10 minuti (UTC) in cui cade un istante.</summary>
        public static long PairingWindow(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds() / (PairingWindowMinutes * 60);

        public static byte[] PairingSecretFromNumber(string digits, long window) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.ASCII.GetBytes(digits),
                Encoding.ASCII.GetBytes("IP-TRX-CTRL abbinamento v3 | finestra " + window.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                PairingIterations, HashAlgorithmName.SHA256, KeySize);

        public static string MakePairingCode(byte[] secret, ServerAddress addr)
        {
            byte[] body = Concat(secret, addr.ToBytes());
            byte check = SHA256.HashData(body)[0];
            return ToCode(Concat(body, new[] { check }), 5);
        }

        public static bool TryParsePairingCode(string code, out byte[] secret, out ServerAddress addr)
        {
            secret = Array.Empty<byte>(); addr = default;
            byte[]? raw = FromCode(code, PairingSecretSize + 8 + 1);
            if (raw == null) return false;
            byte[] body = raw.Take(raw.Length - 1).ToArray();
            if (SHA256.HashData(body)[0] != raw[^1]) return false;
            secret = body.Take(PairingSecretSize).ToArray();
            addr = ServerAddress.FromBytes(body, PairingSecretSize);
            return addr.IsValid;
        }

        // ---------- Codice di riserva (quando il broker MQTT non risponde) ----------
        // sale(2) | indirizzo cifrato(8) | tag(4) = 14 byte
        public static string MakeFallbackCode(byte[] master, ServerAddress addr)
        {
            byte[] key = Hkdf(master, Array.Empty<byte>(), "aal-fallback");
            byte[] salt = RandomNumberGenerator.GetBytes(2);
            byte[] plain = addr.ToBytes();
            byte[] ks = Hmac(key, Encoding.ASCII.GetBytes("ks"), salt);
            byte[] enc = new byte[8];
            for (int i = 0; i < 8; i++) enc[i] = (byte)(plain[i] ^ ks[i]);
            byte[] tag = Hmac(key, Encoding.ASCII.GetBytes("tag"), salt, plain).Take(4).ToArray();
            return ToCode(Concat(salt, enc, tag), 4);
        }

        public static bool TryParseFallbackCode(byte[] master, string code, out ServerAddress addr)
        {
            addr = default;
            byte[]? raw = FromCode(code, 14);
            if (raw == null) return false;
            byte[] key = Hkdf(master, Array.Empty<byte>(), "aal-fallback");
            byte[] salt = raw.Take(2).ToArray();
            byte[] ks = Hmac(key, Encoding.ASCII.GetBytes("ks"), salt);
            byte[] plain = new byte[8];
            for (int i = 0; i < 8; i++) plain[i] = (byte)(raw[2 + i] ^ ks[i]);
            byte[] tag = Hmac(key, Encoding.ASCII.GetBytes("tag"), salt, plain).Take(4).ToArray();
            if (!Equal(tag, raw.Skip(10).Take(4).ToArray())) return false;
            addr = ServerAddress.FromBytes(plain, 0);
            return addr.IsValid;
        }

        /// <summary>Identificativo pubblico dell'abbinamento (non rivela la chiave).</summary>
        public static byte[] PairId(byte[] master) => Hkdf(master, Array.Empty<byte>(), "aal-pairid", 8);

        public static string Fingerprint(byte[] master)
        {
            byte[] f = Hkdf(master, Array.Empty<byte>(), "aal-fingerprint", 8);
            return string.Join(" ", Enumerable.Range(0, 4).Select(i => Convert.ToHexString(f, i * 2, 2)));
        }
    }
}
