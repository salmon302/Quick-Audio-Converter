// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System.Collections.Generic;
using Microsoft.Win32;
using QuickAudioConverter.Engine;

/// <summary>
/// Registers / unregisters per-user Windows Explorer context-menu verbs (REQ-SHELL-01).
///
/// Uses HKCU (no administrator elevation required). For every decodable input extension we add:
///   HKCU\Software\Classes\SystemFileAssociments\&lt;ext&gt;\shell\QacConvertTo&lt;FMT&gt;
///      (default)            = "Convert to &lt;FMT&gt;"
///      Command\(default)    = "&lt;exe&gt;" --headless --input "%1"
///
/// The machine-wide (HKLM) variant mentioned in SRS 2.2 would need admin at install time; the
/// chosen static-integration approach from SRS is per-user HKCU, so registration works from the
/// Options tab at runtime.
/// </summary>
internal static class ShellIntegration
{
    private const string VerbPrefix = "QacConvertTo";

    private static string ExePath =>
        Environment.ProcessPath
        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? "QuickAudioConverter.exe";

    public static void Register(AppSettings settings)
    {
        var exe = ExePath;
        var fmt = (settings.OutputFormat ?? "mp3").ToLowerInvariant();
        var verbLabel = "Convert to " + fmt.ToUpperInvariant();
        var command = $"\"{exe}\" --headless --input \"%1\"";

        foreach (var ext in AppSettings.DecodableExtensions)
        {
            using var baseKey = Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\" + VerbPrefix + fmt);
            baseKey.SetValue("", verbLabel);
            using var cmdKey = baseKey.CreateSubKey("command");
            cmdKey.SetValue("", command);
        }

        settings.ShellIntegrationEnabled = true;
        settings.RegisteredExtensions = new List<string>(AppSettings.DecodableExtensions);
        settings.Save();
    }

    public static void Unregister(AppSettings settings)
    {
        foreach (var ext in AppSettings.DecodableExtensions)
        {
            var shellKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\SystemFileAssociations\" + ext + @"\shell", writable: true);
            if (shellKey is null) continue;
            // Remove any verb we created for this extension (match by prefix).
            foreach (var sub in shellKey.GetSubKeyNames())
            {
                if (sub.StartsWith(VerbPrefix, System.StringComparison.OrdinalIgnoreCase))
                    shellKey.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
            }
            shellKey.Dispose();
        }

        settings.ShellIntegrationEnabled = false;
        settings.RegisteredExtensions = new List<string>();
        settings.Save();
    }

    public static bool IsRegistered(AppSettings settings)
    {
        if (!settings.ShellIntegrationEnabled) return false;
        var ext = AppSettings.DecodableExtensions[0];
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\" + VerbPrefix +
            (settings.OutputFormat ?? "mp3").ToLowerInvariant());
        return key is not null;
    }
}
