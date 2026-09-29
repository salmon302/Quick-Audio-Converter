// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Diagnostics;
using System.Security.Principal;

/// <summary>UAC / elevation helpers for the admin (all-users) install path.</summary>
internal static class Uac
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Re-launch the current EXE with the given arguments elevated (triggers a UAC prompt).</summary>
    public static bool RelaunchElevated(string arguments)
    {
        var exe = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "QuickAudioConverter.exe";
        var psi = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
