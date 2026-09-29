// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

/// <summary>
/// Persisted application settings (last-used encoding profile + global output routing + shell
/// integration state). Stored as JSON under <c>%LOCALAPPDATA%/QuickAudioConverter/settings.json</c>.
///
/// Serialization uses a source-generated <see cref="JsonSerializerContext"/> so it is fully
/// Native-AOT compatible (no runtime reflection / trimming surprises).
/// </summary>
public sealed class AppSettings
{
    public string OutputFormat { get; set; } = "mp3";
    public int BitrateKbps { get; set; } = 224;
    public int Channels { get; set; } = 2;        // 1 = mono, 2 = stereo
    public int SampleRate { get; set; } = 44100;
    public int Quality { get; set; } = 2;          // LAME quality 0-9 (2 = high)
    public bool SaveToSource { get; set; } = true;
    public string? DefaultSaveFolder { get; set; }
    public bool CopySourceStructure { get; set; } = false;
    public bool ShellIntegrationEnabled { get; set; }
    public List<string> RegisteredExtensions { get; set; } = new();

    // Modern (IExplorerCommand / sparse-package) integration state.
    public bool ModernShellInstalled { get; set; }
    public string? ShellInstallScope { get; set; }   // "PerUser" | "AllUsers" | null
    public string? SparsePackageFullName { get; set; }

    private static string Path =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "QuickAudioConverter", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var file = Path;
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Corrupt/unreadable settings: fall back to defaults rather than crash.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var file = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            var json = JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(file, json);
        }
        catch
        {
            // Best-effort persistence; settings loss is non-fatal.
        }
    }

    /// <summary>Input extensions the app can decode and therefore offer a context-menu verb for.</summary>
    public static readonly IReadOnlyList<string> DecodableExtensions =
        new[] { ".m4a", ".aac", ".mp3", ".wav", ".flac", ".wma", ".ogg", ".opus", ".aiff", ".aif" };
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
