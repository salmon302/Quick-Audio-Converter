// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Fires an OS-native notification on conversion completion / failure (NFR 4.2).
///
/// Implementation note: the SRS recommends the Windows.UI.Notifications WinRT toast, but that API
/// (a) requires the Microsoft.Windows.SDK.NET projection package, whose runtime shim is not
/// published at a usable version for this toolchain, and (b) only renders for apps with a registered
/// AppUserModelID (an installer-created Start-Menu shortcut). To stay dependency-free and fully
/// Native-AOT compatible, we use the OS notification area directly via Shell_NotifyIconW — the same
/// underlying facility the WinRT toast builds on. The headless process never creates a visible
/// window, so it never steals focus (NFR 4.2).
/// </summary>
internal static class ToastNotifier
{
    private const string ClassName = "QacToastHost";
    private const int NIF_ICON = 0x00000002;
    private const int NIF_INFO = 0x00000010;
    private const int NIF_TIP = 0x00000004;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIIF_INFO = 0x00000001;
    private const uint WM_DESTROY = 0x0002;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private static readonly IntPtr IDI_INFORMATION = new(32516);

    public static void Show(string title, string message)
    {
        try
        {
            ShowCore(title, message);
        }
        catch
        {
            // Notification is best-effort; never let a toast failure break a conversion run.
        }
    }

    private static void ShowCore(string title, string message)
    {
        var hInstance = GetModuleHandle(null);
        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName
        };
        ushort atom = RegisterClassEx(ref wndClass);
        if (atom == 0) return;

        try
        {
            IntPtr hwnd = CreateWindowEx(
                0, ClassName, ClassName, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;

            try
            {
                var hIcon = LoadIcon(IntPtr.Zero, IDI_INFORMATION);
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = hwnd,
                    uID = 1,
                    uFlags = NIF_ICON | NIF_INFO | NIF_TIP,
                    hIcon = hIcon,
                    szTip = Truncate(title, 127),
                    szInfo = Truncate(message, 255),
                    szInfoTitle = Truncate(title, 63),
                    dwInfoFlags = NIIF_INFO,
                    uVersion = 4
                };

                if (Shell_NotifyIcon(NIM_ADD, ref data))
                {
                    Shell_NotifyIcon(NIM_MODIFY, ref data);
                    // Keep the host window alive long enough for the balloon to render/auto-dismiss.
                    Thread.Sleep(5000);
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                }
            }
            finally
            {
                DestroyWindow(hwnd);
            }
        }
        finally
        {
            UnregisterClass(ClassName, hInstance);
        }
    }

    private static string Truncate(string s, int max)
    {
        s = string.IsNullOrEmpty(s) ? "" : s;
        return s.Length > max ? s.Substring(0, max) : s;
    }

    private static readonly WndProcDelegate _wndProc = (hWnd, msg, wParam, lParam) =>
    {
        if (msg == WM_DESTROY) return IntPtr.Zero;
        return DefWindowProc(hWnd, msg, wParam, lParam);
    };

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
}
