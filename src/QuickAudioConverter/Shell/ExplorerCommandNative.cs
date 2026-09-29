// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Native plumbing for the IExplorerCommand COM local server: HRESULT codes, the COM
/// enums used by IExplorerCommand, and P/Invoke declarations for ole32 / user32 / kernel32.
/// </summary>
internal static class HResult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
}

/// <summary>EXPCMDSTATE — returned by IExplorerCommand.GetState.</summary>
[Flags]
internal enum EXPCMDSTATE : uint
{
    ECS_ENABLED = 0,
    ECS_DISABLED = 0x1,
    ECS_HIDDEN = 0x2,
    ECS_CHECKED = 0x4,
    ECS_RADIOCHECK = 0x8,
}

/// <summary>EXPCMDFLAGS — returned by IExplorerCommand.GetFlags.</summary>
[Flags]
internal enum EXPCMDFLAGS : uint
{
    ECF_DEFAULT = 0,
    ECF_HASSUBCOMMANDS = 0x1,
    ECF_HASSPLITBUTTON = 0x2,
    ECF_ASYNC = 0x4,
    ECF_VALUEISICON = 0x8,
}

/// <summary>SIGDN — display-name selector for IShellItem.GetDisplayName.</summary>
internal enum SIGDN : uint
{
    NORMALDISPLAY = 0,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    PARENTRELATIVEEDITING = 0x80031001,
    DESKTOPABSOLUTEEDITING = 0x8004C001,
    FILESYSPATH = 0x80058000,
    URL = 0x80068000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int x;
    public int y;
}

internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    public static extern int CoRegisterClassObject(
        in Guid rclsid, IntPtr pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoRevokeClassObject(uint dwRegister);

    [DllImport("ole32.dll")]
    public static extern int CoResumeClassObjects();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern bool DispatchMessage(ref MSG lpMsg);
}

internal static class Kernel32
{
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}
