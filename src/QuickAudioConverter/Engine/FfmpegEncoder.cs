// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Encodes audio to compressed container formats (M4A / AAC / FLAC) using a bundled or system FFmpeg.
///
/// Rationale: this Windows build (and many "N"-edition / server installs) ships no AAC/FLAC encoder
/// MFT, so the Media Foundation Sink Writer cannot produce these formats (see dev-audit #6). FFmpeg's
/// native AAC/FLAC encoders are reliable and cross-machine. This mirrors the project's existing
/// LAME-based MP3 path and is sanctioned by SRS item #20 ("Optional trimmed FFmpeg binary").
///
/// The process runs hidden (<see cref="ProcessStartInfo.CreateNoWindow"/>) so headless conversion
/// never steals focus (NFR 4.2). Encoding always targets a sibling temp file before an atomic move,
/// so a failed run — or an in-place conversion of the source — never corrupts the original.
/// </summary>
internal static class FfmpegEncoder
{
    /// <summary>Resolves ffmpeg.exe: a bundled copy next to the app wins, else PATH lookup.</summary>
    internal static string? ResolveFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var cand = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(cand)) return cand;
            }
            catch (ArgumentException) { /* ignore malformed PATH entries */ }
        }
        return null;
    }

    public static void Encode(string input, string output, ConversionSettings s)
    {
        var ffmpeg = ResolveFfmpeg();
        if (ffmpeg is null)
            throw new InvalidOperationException(
                "FFmpeg not found. M4A/AAC/FLAC output requires ffmpeg.exe next to the app or on PATH.");

        // Encode to a sibling temp file first so a failed/in-place run never corrupts the source.
        // The temp file keeps the output's extension so ffmpeg infers the correct container/muxer.
        string tmp = Path.Combine(
            Path.GetDirectoryName(output) ?? ".",
            Path.GetFileNameWithoutExtension(output) + ".qac_tmp" + Path.GetExtension(output));
        if (File.Exists(tmp)) File.Delete(tmp);

        string codec = s.OutputFormat.ToLowerInvariant() switch
        {
            "flac" => "flac",
            _ => "aac" // m4a and aac both use the AAC encoder
        };

        var args = $"-y -i \"{input}\" -vn -c:a {codec} -b:a {s.BitrateKbps}k -ar {s.SampleRate} -ac {s.Channels} \"{tmp}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg process.");
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited {proc.ExitCode}: {stderr}");
        if (!File.Exists(tmp) || new FileInfo(tmp).Length == 0)
            throw new InvalidOperationException("ffmpeg produced no output file.");

        // Atomic replace of the final output (handles in-place conversion of the source).
        if (File.Exists(output)) File.Delete(output);
        File.Move(tmp, output);
    }
}
