// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Orchestrates batch conversion: expands inputs (files + directories), resolves output paths,
/// runs the engine per file, and aggregates progress and errors.
/// </summary>
internal sealed class AudioConversionService
{
    private readonly IAudioEngine _engine;

    public AudioConversionService(IAudioEngine engine) => _engine = engine;

    public async Task<ConversionReport> ConvertAsync(
        IReadOnlyList<string> rawInputs,
        ConversionSettings settings,
        OutputRouting routing,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var inputs = ExpandInputs(rawInputs);
        if (inputs.Count == 0)
            return new ConversionReport(0, 0, new List<string>());

        string? commonRoot = routing.CopySourceStructure ? CommonRoot(inputs) : null;

        int done = 0;
        var errors = new List<string>();
        foreach (var input in inputs)
        {
            if (ct.IsCancellationRequested) break;

            string dir = Path.GetDirectoryName(input) ?? Path.GetDirectoryName(input) ?? ".";
            string rel = commonRoot != null ? RelativePath(commonRoot, dir) : string.Empty;
            string outDir = (routing.SaveToSource || string.IsNullOrWhiteSpace(routing.SaveToFolder))
                ? dir
                : Path.Combine(routing.SaveToFolder!, rel);
            Directory.CreateDirectory(outDir);

            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + "." + settings.OutputFormat.ToLowerInvariant());
            if (outPath != input && File.Exists(outPath)) File.Delete(outPath);

            progress?.Report(new ConversionProgress(done, inputs.Count, Path.GetFileName(input)));
            try
            {
                await _engine.ConvertAsync(input, outPath, settings, ct);
                done++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(input)}: {ex.Message}");
            }
        }

        progress?.Report(new ConversionProgress(inputs.Count, inputs.Count, "Done"));
        return new ConversionReport(done, inputs.Count - done, errors);
    }

    private static List<string> ExpandInputs(IReadOnlyList<string> raw)
    {
        var result = new List<string>();
        var audioExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".m4a", ".aac", ".flac", ".wma", ".aiff", ".aif", ".opus"
        };
        foreach (var p in raw)
        {
            if (Directory.Exists(p))
                result.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories)
                    .Where(f => audioExt.Contains(Path.GetExtension(f))));
            else if (File.Exists(p))
                result.Add(p);
        }
        return result;
    }

    private static string? CommonRoot(List<string> inputs)
    {
        var dirs = inputs.Select(i => Path.GetDirectoryName(i) ?? ".").ToArray();
        if (dirs.Length == 0) return null;
        var parts = dirs[0].Split(Path.DirectorySeparatorChar);
        int common = parts.Length;
        foreach (var d in dirs.Skip(1))
        {
            var dp = d.Split(Path.DirectorySeparatorChar);
            int m = 0;
            while (m < common && m < dp.Length && dp[m].Equals(parts[m], StringComparison.OrdinalIgnoreCase)) m++;
            common = m;
        }
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(common));
    }

    private static string RelativePath(string root, string dir)
    {
        if (dir.Length > root.Length && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return dir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
        return string.Empty;
    }
}

internal sealed class ConversionProgress
{
    public int Completed { get; }
    public int Total { get; }
    public string CurrentFile { get; }
    public ConversionProgress(int completed, int total, string currentFile)
    {
        Completed = completed;
        Total = total;
        CurrentFile = currentFile;
    }
}

internal sealed class ConversionReport
{
    public int Succeeded { get; }
    public int Failed { get; }
    public IReadOnlyList<string> Errors { get; }
    public ConversionReport(int succeeded, int failed, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Failed = failed;
        Errors = errors;
    }
}
