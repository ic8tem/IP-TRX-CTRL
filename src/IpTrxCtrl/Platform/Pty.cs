using System;
using System.Runtime.InteropServices;

namespace IpTrxCtrl.Platform
{
    /// <summary>
    /// Porta seriale virtuale su Linux e macOS (dalla 0.3.7): uno pseudo-terminale. Il programma tiene il lato
    /// «master», il programma di log apre il lato «slave» (/dev/pts/N) come se fosse la porta di un WinKeyer.
    /// </summary>
    public sealed class Pty : IDisposable
    {
        [DllImport("libc", SetLastError = true)] private static extern int posix_openpt(int flags);
        [DllImport("libc", SetLastError = true)] private static extern int grantpt(int fd);
        [DllImport("libc", SetLastError = true)] private static extern int unlockpt(int fd);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr ptsname(int fd);
        [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buf, nint count);
        [DllImport("libc", SetLastError = true)] private static extern nint write(int fd, byte[] buf, nint count);
        [DllImport("libc", SetLastError = true)] private static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeout);

        [StructLayout(LayoutKind.Sequential)]
        private struct PollFd { public int fd; public short events; public short revents; }

        private int _fd = -1;
        public string SlavePath { get; }

        public Pty()
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("su Windows serve una coppia di porte com0com");
            int O_RDWR = 2, O_NOCTTY = OperatingSystem.IsMacOS() ? 0x20000 : 0x100;
            _fd = posix_openpt(O_RDWR | O_NOCTTY);
            if (_fd < 0) throw new InvalidOperationException("posix_openpt non riuscito (" + Marshal.GetLastWin32Error() + ")");
            if (grantpt(_fd) != 0 || unlockpt(_fd) != 0) { close(_fd); _fd = -1; throw new InvalidOperationException("pseudo-terminale non sbloccato"); }
            SlavePath = Marshal.PtrToStringAnsi(ptsname(_fd)) ?? "";
        }

        /// <summary>Legge i byte arrivati; 0 se non arriva niente entro il tempo (ms), -1 se chiuso.</summary>
        public int Read(byte[] buf, int timeoutMs)
        {
            if (_fd < 0) return -1;
            var p = new[] { new PollFd { fd = _fd, events = 1 /* POLLIN */ } };
            int r = poll(p, 1, timeoutMs);
            if (r <= 0) return 0;
            if ((p[0].revents & 1) == 0) { System.Threading.Thread.Sleep(Math.Min(timeoutMs, 100)); return 0; }   // nessuno ha aperto il lato slave (POLLHUP)
            nint n = read(_fd, buf, buf.Length);
            return n < 0 ? 0 : (int)n;
        }

        public void Write(byte[] data)
        {
            if (_fd >= 0) write(_fd, data, data.Length);
        }

        public void Dispose()
        {
            if (_fd >= 0) { close(_fd); _fd = -1; }
        }
    }
}
