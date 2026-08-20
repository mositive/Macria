using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Macria
{
    // Otomasyonun kritik aninda kullanicinin fiziksel fare/klavye girdisini engeller.
    // Boylece yanlislikla atilan bir tik odagi kaydirip Tab sirasini bozamaz.
    // - Kendi enjekte ettigimiz tuslar (keybd_event) gecer.
    // - Macria'nin kendi pencerelerine tiklama (PiP'teki Durdur butonu) serbesttir.
    // - Ctrl+Alt+Del her zaman calisir (sistem tarafindan garanti edilir).
    internal static class InputGuard
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const uint WM_QUIT = 0x0012;
        private const uint LLKHF_INJECTED = 0x10;
        private const uint LLMHF_INJECTED = 0x01;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private static readonly HookProc _mouseProc = MouseProc;
        private static readonly HookProc _keyProc = KeyProc;
        private static readonly uint _pid = (uint)Process.GetCurrentProcess().Id;
        private static readonly object _kilit = new object();

        private static IntPtr _mouseHook;
        private static IntPtr _keyHook;
        private static uint _threadId;

        public static bool Active { get; private set; }

        public static void Enable()
        {
            lock (_kilit)
            {
                if (Active) return;
                Active = true;

                // LL hook'lar hizli bir mesaj dongusu ister; UI thread'i Thread.Sleep ile
                // arada kilitlendigi icin hook'lari kendi dongusu olan ayri thread'e kur.
                var hazir = new ManualResetEventSlim(false);
                var t = new Thread(() =>
                {
                    _threadId = GetCurrentThreadId();
                    IntPtr mod = GetModuleHandle(null);
                    _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
                    _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, mod, 0);
                    hazir.Set();

                    MSG msg;
                    while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }

                    if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
                    if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
                    _mouseHook = IntPtr.Zero;
                    _keyHook = IntPtr.Zero;
                });
                t.IsBackground = true;
                t.Start();
                hazir.Wait(2000);
            }
        }

        public static void Disable()
        {
            lock (_kilit)
            {
                if (!Active) return;
                Active = false;

                if (_threadId != 0)
                    PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _threadId = 0;
            }
        }

        private static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Active)
            {
                int msg = wParam.ToInt32();

                // 0x0201-0x020E: tum buton ve teker mesajlari (WM_MOUSEMOVE 0x0200 haric)
                if (msg >= 0x0201 && msg <= 0x020E)
                {
                    var bilgi = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    bool enjekte = (bilgi.flags & LLMHF_INJECTED) != 0;

                    if (!enjekte && !IsOwnWindowAt(bilgi.pt))
                        return (IntPtr)1; // fiziksel tiklama yutuldu
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Active)
            {
                var bilgi = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool enjekte = (bilgi.flags & LLKHF_INJECTED) != 0;

                if (!enjekte)
                    return (IntPtr)1; // fiziksel tus yutuldu; keybd_event ile gonderdiklerimiz gecer
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private static bool IsOwnWindowAt(POINT pt)
        {
            IntPtr w = WindowFromPoint(pt);
            if (w == IntPtr.Zero) return false;

            uint pid;
            GetWindowThreadProcessId(w, out pid);
            return pid == _pid;
        }
    }
}
