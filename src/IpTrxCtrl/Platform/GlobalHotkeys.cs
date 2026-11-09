using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace IpTrxCtrl.Platform
{
    /// <summary>
    /// Tasti globali Ctrl+Alt+F1…F8 (anche quando la finestra non è in primo piano).
    /// Windows: RegisterHotKey su un thread con coda di messaggi propria.
    /// Linux/Raspberry: XGrabKey sulla finestra radice di X11 (con Wayland di solito non è permesso).
    /// macOS: RegisterEventHotKey di Carbon.
    /// </summary>
    public sealed class GlobalHotkeys : IDisposable
    {
        public event Action<int>? Pressed;   // 0..7
        public string Status { get; private set; } = "";
        private Thread? _thread;
        private volatile bool _run;
        private uint _winThreadId;
        private IntPtr _display;

        public void Start()
        {
            _run = true;
            try
            {
                if (OperatingSystem.IsWindows()) { _thread = new Thread(WinLoop) { IsBackground = true, Name = "hotkeys" }; _thread.Start(); }
                else if (OperatingSystem.IsMacOS()) MacStart();
                else { _thread = new Thread(X11Loop) { IsBackground = true, Name = "hotkeys" }; _thread.Start(); }
            }
            catch (Exception ex) { Status = "Tasti globali non disponibili: " + ex.Message; }
        }

        // ------------------------------------------------------------------ Windows
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] private static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private void WinLoop()
        {
            const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, WM_HOTKEY = 0x0312, VK_F1 = 0x70;
            _winThreadId = GetCurrentThreadId();
            int ok = 0;
            for (int i = 0; i < 8; i++) if (RegisterHotKey(IntPtr.Zero, 100 + i, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F1 + (uint)i)) ok++;
            Status = ok == 8 ? "" : $"Ctrl+Alt+F1..F8: {8 - ok} tasti già usati da altri programmi";
            try
            {
                while (_run && GetMessageW(out MSG m, IntPtr.Zero, 0, 0) > 0)
                {
                    if (m.message == WM_HOTKEY)
                    {
                        int id = m.wParam.ToInt32() - 100;
                        if (id >= 0 && id < 8) Pressed?.Invoke(id);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < 8; i++) UnregisterHotKey(IntPtr.Zero, 100 + i);
            }
        }

        // ------------------------------------------------------------------ X11
        private const string X = "libX11.so.6";
        [DllImport(X)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(X)] private static extern IntPtr XDefaultRootWindow(IntPtr d);
        [DllImport(X)] private static extern byte XKeysymToKeycode(IntPtr d, IntPtr keysym);
        [DllImport(X)] private static extern int XGrabKey(IntPtr d, int keycode, uint mods, IntPtr win, int ownerEvents, int pointerMode, int keyboardMode);
        [DllImport(X)] private static extern int XSelectInput(IntPtr d, IntPtr win, IntPtr mask);
        [DllImport(X)] private static extern int XPending(IntPtr d);
        [DllImport(X)] private static extern int XNextEvent(IntPtr d, IntPtr ev);
        [DllImport(X)] private static extern int XCloseDisplay(IntPtr d);
        [DllImport(X)] private static extern int XSync(IntPtr d, int discard);

        private void X11Loop()
        {
            if (Environment.GetEnvironmentVariable("DISPLAY") == null) { Status = "Tasti globali non disponibili senza X11 (Wayland)"; return; }
            try { _display = XOpenDisplay(IntPtr.Zero); }
            catch (Exception ex) { Status = "libX11 non disponibile: " + ex.Message; return; }
            if (_display == IntPtr.Zero) { Status = "Display X11 non raggiungibile"; return; }
            IntPtr root = XDefaultRootWindow(_display);
            const uint ControlMask = 1 << 2, Mod1Mask = 1 << 3, LockMask = 1 << 1, Mod2Mask = 1 << 4;
            var codes = new int[8];
            for (int i = 0; i < 8; i++)
            {
                codes[i] = XKeysymToKeycode(_display, (IntPtr)(0xFFBE + i));   // XK_F1..XK_F8
                foreach (uint extra in new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask })
                    XGrabKey(_display, codes[i], ControlMask | Mod1Mask | extra, root, 0, 1, 1);
            }
            XSelectInput(_display, root, (IntPtr)1);   // KeyPressMask
            XSync(_display, 0);
            IntPtr ev = Marshal.AllocHGlobal(256);
            try
            {
                while (_run)
                {
                    if (XPending(_display) == 0) { Thread.Sleep(20); continue; }
                    XNextEvent(_display, ev);
                    int type = Marshal.ReadInt32(ev);
                    if (type != 2) continue;   // KeyPress
                    // XKeyEvent: type, serial, send_event, display, window, root, subwindow, time, x, y, x_root, y_root, state, keycode
                    int off = IntPtr.Size == 8 ? 84 : 52;
                    int keycode = Marshal.ReadInt32(ev, off);
                    int idx = Array.IndexOf(codes, keycode);
                    if (idx >= 0) Pressed?.Invoke(idx);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ev);
                XCloseDisplay(_display);
                _display = IntPtr.Zero;
            }
        }

        // ------------------------------------------------------------------ macOS (Carbon)
        private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
        [StructLayout(LayoutKind.Sequential)] private struct EventTypeSpec { public uint eventClass, eventKind; }
        [StructLayout(LayoutKind.Sequential)] private struct EventHotKeyID { public uint signature, id; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EventHandlerProc(IntPtr callRef, IntPtr evt, IntPtr user);
        [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
        [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, EventHandlerProc handler, uint numTypes, EventTypeSpec[] list, IntPtr user, out IntPtr handlerRef);
        [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target, uint options, out IntPtr hotKeyRef);
        [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr evt, uint name, uint type, IntPtr outType, nuint size, IntPtr outSize, out EventHotKeyID data);
        private static EventHandlerProc? _macHandler;

        private void MacStart()
        {
            // Codici tasto virtuali di F1..F8 su macOS
            uint[] keys = { 122, 120, 99, 118, 96, 97, 98, 100 };
            const uint controlKey = 1 << 12, optionKey = 1 << 11;
            uint Four(string s) => (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
            _macHandler = (callRef, evt, user) =>
            {
                if (GetEventParameter(evt, Four("hkid"), Four("hkid"), IntPtr.Zero, (nuint)Marshal.SizeOf<EventHotKeyID>(), IntPtr.Zero, out var hk) == 0
                    && hk.id < 8) Pressed?.Invoke((int)hk.id);
                return 0;
            };
            var spec = new[] { new EventTypeSpec { eventClass = Four("keyb"), eventKind = 5 } };   // kEventHotKeyPressed
            IntPtr target = GetApplicationEventTarget();
            InstallEventHandler(target, _macHandler, 1, spec, IntPtr.Zero, out _);
            int ok = 0;
            for (uint i = 0; i < 8; i++)
                if (RegisterEventHotKey(keys[i], controlKey | optionKey, new EventHotKeyID { signature = Four("iptx"), id = i }, target, 0, out _) == 0) ok++;
            Status = ok == 8 ? "" : $"Ctrl+Option+F1..F8: {8 - ok} tasti non registrati";
        }

        public void Dispose()
        {
            _run = false;
            if (OperatingSystem.IsWindows() && _winThreadId != 0) PostThreadMessageW(_winThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);   // WM_QUIT
        }
    }
}
