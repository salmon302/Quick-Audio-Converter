// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using QuickAudioConverter.Engine;

/// <summary>
/// Registers / unregisters the IExplorerCommand COM server and (optionally) installs the sparse
/// package that grants the package identity Windows 11 needs to promote the command to the
/// top-level context menu. Uses HKCU (per-user) or HKLM (all-users, admin).
///
/// Registry layout for a non-packaged install:
///   ...\Software\Classes\CLSID\{clsid}
///        (default)        = "Quick Audio Converter Explorer Command"
///        LocalServer32    = "&lt;exe&gt;" --comserver
///   ...\Software\Classes\SystemFileAssociations\&lt;ext&gt;\shell\QacModernConvert
///        (default)                = "Convert to &lt;FMT&gt;"   (fallback label)
///        ExplorerCommandHandler   = "{clsid}"                  (routes to our IExplorerCommand)
///        Position                 = "Top"
/// </summary>
internal static class ExplorerCommandRegistration
{
    public enum Scope { PerUser, AllUsers }

    private const string VerbKeyName = "QacModernConvert";

    private static string ExePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? "QuickAudioConverter.exe";

    private static string ClsIdString => "{" + ExplorerCommandGuids.ExplorerCommandClsid.ToString("B").ToUpperInvariant() + "}";

    internal static string ClsIdPath(bool hklm) =>
        (hklm ? @"HKEY_LOCAL_MACHINE\Software\Classes\CLSID\" : @"HKEY_CURRENT_USER\Software\Classes\CLSID\") + ClsIdString;

    internal static string[] ShellVerbPaths(bool hklm)
    {
        var root = hklm ? @"HKEY_LOCAL_MACHINE\Software\Classes\SystemFileAssociations\"
                        : @"HKEY_CURRENT_USER\Software\Classes\SystemFileAssociations\";
        var list = new string[AppSettings.DecodableExtensions.Count];
        for (int i = 0; i < list.Length; i++)
            list[i] = root + AppSettings.DecodableExtensions[i] + @"\shell\" + VerbKeyName;
        return list;
    }

    public static void Register(Scope scope)
    {
        bool hklm = scope == Scope.AllUsers;
        RegisterClsId(hklm);
        RegisterShellVerbs(hklm);
    }

    /// <summary>
    /// Called WITH package identity (via Invoke-CommandInDesktopPackage). HKCU writes are then
    /// virtualized into the package so File Explorer promotes the command to the Windows 11
    /// top-level menu.
    /// </summary>
    public static void RegisterPackaged()
    {
        RegisterClsId(hklm: false);
        RegisterShellVerbs(hklm: false);
    }

    public static void Unregister(Scope scope)
    {
        bool hklm = scope == Scope.AllUsers;
        UnregisterShellVerbs(hklm);
        UnregisterClsId(hklm);
    }

    private static void RegisterClsId(bool hklm)
    {
        using var key = (hklm ? Registry.LocalMachine : Registry.CurrentUser)
            .CreateSubKey(@"Software\Classes\CLSID\" + ClsIdString);
        key.SetValue("", "Quick Audio Converter Explorer Command");
        using var ls32 = key.CreateSubKey("LocalServer32");
        ls32.SetValue("", "\"" + ExePath + "\" --comserver");
    }

    private static void UnregisterClsId(bool hklm)
    {
        using var classes = (hklm ? Registry.LocalMachine : Registry.CurrentUser)
            .OpenSubKey(@"Software\Classes\CLSID", writable: true);
        classes?.DeleteSubKeyTree(ClsIdString, throwOnMissingSubKey: false);
    }

    private static void RegisterShellVerbs(bool hklm)
    {
        var fmt = (AppSettings.Load().OutputFormat ?? "mp3").ToUpperInvariant();
        var clsid = ClsIdString;
        foreach (var ext in AppSettings.DecodableExtensions)
        {
            using var shellKey = (hklm ? Registry.LocalMachine : Registry.CurrentUser)
                .CreateSubKey(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell\" + VerbKeyName);
            shellKey.SetValue("", "Convert to " + fmt);
            shellKey.SetValue("ExplorerCommandHandler", clsid);
            shellKey.SetValue("Position", "Top");
        }
    }

    private static void UnregisterShellVerbs(bool hklm)
    {
        foreach (var ext in AppSettings.DecodableExtensions)
        {
            using var sfa = (hklm ? Registry.LocalMachine : Registry.CurrentUser)
                .OpenSubKey(@"Software\Classes\SystemFileAssociations\" + ext + @"\shell", writable: true);
            sfa?.DeleteSubKeyTree(VerbKeyName, throwOnMissingSubKey: false);
        }
    }

    // ---- Sparse package (Windows 11 top-level identity) ----

    /// <summary>Installs the prebuilt sparse .msix next to the EXE via Add-AppxPackage.</summary>
    public static bool InstallSparsePackage()
    {
        var dir = Path.GetDirectoryName(ExePath) ?? ".";
        var msix = Path.Combine(dir, "QuickAudioConverter.Sparse.msix");
        if (!File.Exists(msix)) return false;
        return RunPowerShell($"Add-AppxPackage -Path '{msix}' -ExternalLocation '{dir}'");
    }

    public static bool RemoveSparsePackage()
    {
        var name = ExplorerCommandGuids.PackageName;
        return RunPowerShell($"Get-AppxPackage -Name '{name}' | Remove-AppxPackage");
    }

    public static string? GetSparsePackageFullName()
    {
        var name = ExplorerCommandGuids.PackageName;
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -Command \"& {{ (Get-AppxPackage -Name '{name}').PackageFullName }}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return string.IsNullOrWhiteSpace(outp) ? null : outp;
        }
        catch { return null; }
    }

    /// <summary>Runs the packaged registration under the package identity so HKCU writes virtualize.</summary>
    public static bool RegisterUnderPackageIdentity()
    {
        var fullName = GetSparsePackageFullName();
        if (fullName is null) return false;
        var exe = ExePath;
        var script = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{fullName}' -AppId QAC " +
                     $"-Command '{exe}' -Args '--register-modern-packaged'";
        return RunPowerShell(script);
    }

    private static bool RunPowerShell(string command)
    {
        var psi = new ProcessStartInfo("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "`\"")}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
