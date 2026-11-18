using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IpTrxCtrl.Core
{
    /// <summary>
    /// Piccoli sostituti delle funzioni comparse dopo .NET 6: servono ai pacchetti per Windows 7 SP1, 8 e 8.1,
    /// compilati con .NET 6 (dalla 0.3.5). Con .NET 8 si usano le funzioni originali.
    /// </summary>
    public static class Compat
    {
        public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
        public static bool IsAsciiLetterOrDigit(char c) => IsAsciiDigit(c) || (c | 0x20) >= 'a' && (c | 0x20) <= 'z';
    }

#if !NET7_0_OR_GREATER
    public static class TextReaderCompat
    {
        /// <summary>ReadLineAsync con annullamento (in .NET 6 c'è solo senza): chi chiama chiude il flusso per fermarlo.</summary>
        public static async ValueTask<string?> ReadLineAsync(this TextReader r, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var t = r.ReadLineAsync();
            if (t.IsCompleted) return await t;
            var done = await Task.WhenAny(t, Task.Delay(Timeout.Infinite, ct));
            if (done != t) ct.ThrowIfCancellationRequested();
            return await t;
        }
    }
#endif
}
