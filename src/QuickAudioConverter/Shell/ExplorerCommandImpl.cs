// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using QuickAudioConverter.Engine;

/// <summary>
/// Out-of-process COM local server implementing <see cref="IExplorerCommand"/>. Explorer launches
/// <c>QuickAudioConverter.exe --comserver</c>; we register the class object, pump a message loop on
/// an STA thread, and service <c>Invoke</c> calls. <c>Invoke</c> simply spawns a detached
/// <c>--headless</c> conversion process for the selected files (mirrors REQ-SHELL-01 and never
/// steals focus from the user's foreground window).
/// </summary>
internal sealed class ExplorerCommand : IExplorerCommand, IObjectWithSite
{
    private IntPtr _site;

    public int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName)
    {
        ComServer.Touch();
        ppszName = IntPtr.Zero;
        string label;
        try { label = "Convert to " + (AppSettings.Load().OutputFormat ?? "mp3").ToUpperInvariant(); }
        catch { label = "Convert with Quick Audio Converter"; }
        ppszName = Marshal.StringToCoTaskMemUni(label);
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon)
    {
        ComServer.Touch();
        ppszIcon = Marshal.StringToCoTaskMemUni(ExePath + ",0");
        return HResult.S_OK;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip)
    {
        ComServer.Touch();
        ppszInfotip = Marshal.StringToCoTaskMemUni("Convert selected audio files with Quick Audio Converter");
        return HResult.S_OK;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        ComServer.Touch();
        pguidCommandName = ExplorerCommandGuids.CommandCanonicalName;
        return HResult.S_OK;
    }

    public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out EXPCMDSTATE pCmdState)
    {
        ComServer.Touch();
        pCmdState = EXPCMDSTATE.ECS_ENABLED;
        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
    {
        ComServer.Touch();
        try
        {
            var paths = EnumeratePaths(psiItemArray);
            if (paths.Count > 0) LaunchHeadless(paths);
        }
        catch
        {
            // Best-effort: never let a failure here crash Explorer's call into us.
        }
        return HResult.S_OK;
    }

    public int GetFlags(out EXPCMDFLAGS pFlags)
    {
        ComServer.Touch();
        pFlags = EXPCMDFLAGS.ECF_DEFAULT;
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IntPtr ppEnum)
    {
        ComServer.Touch();
        ppEnum = IntPtr.Zero; // no sub-commands
        return HResult.S_OK;
    }

    public int SetSite(IntPtr pUnkSite)
    {
        _site = pUnkSite;
        return HResult.S_OK;
    }

    public int GetSite(ref Guid riid, out IntPtr ppvSite)
    {
        ppvSite = IntPtr.Zero;
        if (_site == IntPtr.Zero) return HResult.E_NOINTERFACE;
        return Marshal.QueryInterface(_site, in riid, out ppvSite);
    }

    private static List<string> EnumeratePaths(IShellItemArray? items)
    {
        var result = new List<string>();
        if (items is null) return result;
        if (items.GetCount(out uint count) != HResult.S_OK) return result;
        for (uint i = 0; i < count; i++)
        {
            if (items.GetItemAt(i, out var item) != HResult.S_OK || item is null) continue;
            if (item.GetDisplayName(SIGDN.FILESYSPATH, out IntPtr ptr) == HResult.S_OK && ptr != IntPtr.Zero)
            {
                var s = Marshal.PtrToStringUni(ptr);
                if (!string.IsNullOrEmpty(s)) result.Add(s);
                Marshal.FreeCoTaskMem(ptr);
            }
        }
        return result;
    }

    private static void LaunchHeadless(List<string> paths)
    {
        var sb = new StringBuilder("--headless");
        foreach (var p in paths)
        {
            sb.Append(" --input \"").Append(p.Replace("\"", "\\\"")).Append('"');
        }
        var psi = new ProcessStartInfo(ExePath, sb.ToString())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var proc = Process.Start(psi);
    }

    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? "QuickAudioConverter.exe";
}

/// <summary>IClassFactory that produces <see cref="ExplorerCommand"/> instances for the COM SCM.</summary>
internal sealed class ExplorerCommandClassFactory : IClassFactory
{
    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ComServer.Touch();
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero) return HResult.CLASS_E_NOAGGREGATION;

        var cmd = new ExplorerCommand();
        if (riid == ExplorerCommandGuids.IID_IExplorerCommand)
            ppvObject = Marshal.GetComInterfaceForObject(cmd, typeof(IExplorerCommand));
        else if (riid == ExplorerCommandGuids.IID_IObjectWithSite)
            ppvObject = Marshal.GetComInterfaceForObject(cmd, typeof(IObjectWithSite));
        else if (riid == ExplorerCommandGuids.IID_IUnknown)
            ppvObject = Marshal.GetComInterfaceForObject(cmd, typeof(IExplorerCommand));
        else
            return HResult.E_NOINTERFACE;

        return ppvObject == IntPtr.Zero ? HResult.E_NOINTERFACE : HResult.S_OK;
    }

    public int LockServer(bool fLock) => HResult.S_OK;
}

/// <summary>
/// Hosts the COM local server: registers the class object, pumps a message loop on the STA
/// thread, and exits after a period of inactivity (Explorer never explicitly tells us to quit).
/// </summary>
internal static class ComServer
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;
    private const uint REGCLS_SUSPENDED = 0x4;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint WM_QUIT = 0x0012;
    private const int IdleTimeoutSeconds = 30;

    private static DateTime _lastActivity = DateTime.UtcNow;

    /// <summary>Record activity so the idle watchdog keeps the server alive while in use.</summary>
    public static void Touch() => _lastActivity = DateTime.UtcNow;

    public static int Run()
    {
        Ole32.CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
        uint threadId = Kernel32.GetCurrentThreadId();

        var clsid = ExplorerCommandGuids.ExplorerCommandClsid;
        var factory = new ExplorerCommandClassFactory();
        IntPtr cfPtr = Marshal.GetComInterfaceForObject(factory, typeof(IClassFactory));
        if (cfPtr == IntPtr.Zero) { Ole32.CoUninitialize(); return 1; }

        int hr = Ole32.CoRegisterClassObject(
            in clsid, cfPtr, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE | REGCLS_SUSPENDED, out uint cookie);
        if (hr < 0)
        {
            Marshal.Release(cfPtr);
            Ole32.CoUninitialize();
            return 1;
        }

        Ole32.CoResumeClassObjects();
        Touch();

        bool exit = false;
        var watchdog = new System.Threading.Thread(() =>
        {
            while (!exit)
            {
                System.Threading.Thread.Sleep(5000);
                if ((DateTime.UtcNow - _lastActivity).TotalSeconds > IdleTimeoutSeconds)
                {
                    Kernel32.PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                    break;
                }
            }
        }) { IsBackground = true };
        watchdog.Start();

        while (Ole32.GetMessage(out MSG msg, IntPtr.Zero, 0, 0) != 0)
        {
            Ole32.TranslateMessage(ref msg);
            Ole32.DispatchMessage(ref msg);
        }

        exit = true;
        Ole32.CoRevokeClassObject(cookie);
        Marshal.Release(cfPtr);
        Ole32.CoUninitialize();
        return 0;
    }
}
