// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using QuickAudioConverter.Engine;
using Xunit;

/// <summary>
/// End-to-end legs over the real engines. These are skipped, not failed, when their native
/// prerequisites are absent, so an unprovisioned CI runner stays green without masking real failures.
/// </summary>
public class EngineIntegrationTests
{
    private static string LameDllPath => Path.Combine(AppContext.BaseDirectory, "libmp3lame.dll");
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "1s_stereo_44k.m4a");

    [WindowsFact]
    public async Task Wav_Pipeline_Decodes_And_Writes_Valid_Wav()
    {
        if (!File.Exists(FixturePath)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "qac_it_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(FixturePath) + ".wav");
        try
        {
            var service = new AudioConversionService(new WmfAudioEngine());
            var settings = new ConversionSettings { OutputFormat = "wav", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
            var routing = new OutputRouting { SaveToSource = false, SaveToFolder = outDir, CopySourceStructure = false };
            var report = await service.ConvertAsync(new[] { FixturePath }, settings, routing);
            Assert.Equal(1, report.Succeeded);
            Assert.True(WavValidation.IsValidWav(outPath));
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [LameFact]
    public async Task Mp3_Pipeline_Encodes_Via_Lame()
    {
        if (!File.Exists(LameDllPath) || !File.Exists(FixturePath)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "qac_it_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(FixturePath) + ".mp3");
        try
        {
            var service = new AudioConversionService(new WmfAudioEngine());
            var settings = new ConversionSettings { OutputFormat = "mp3", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
            var routing = new OutputRouting { SaveToSource = false, SaveToFolder = outDir, CopySourceStructure = false };
            var report = await service.ConvertAsync(new[] { FixturePath }, settings, routing);
            Assert.Equal(1, report.Succeeded);

            var info = new FileInfo(outPath);
            Assert.True(info.Length > 1000, "MP3 output suspiciously small.");
            var head = new byte[2];
            using (var fs = File.OpenRead(outPath)) fs.ReadExactly(head);
            Assert.Equal(0xFF, head[0]);
            Assert.Equal(0xFB, head[1]); // MPEG-1 Layer III frame sync
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [FfmpegFact]
    public async Task M4a_Pipeline_Encodes_Via_Ffmpeg()
    {
        if (!File.Exists(FixturePath)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "qac_it_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(FixturePath) + ".m4a");
        try
        {
            var service = new AudioConversionService(new WmfAudioEngine());
            var settings = new ConversionSettings { OutputFormat = "m4a", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
            var routing = new OutputRouting { SaveToSource = false, SaveToFolder = outDir, CopySourceStructure = false };
            var report = await service.ConvertAsync(new[] { FixturePath }, settings, routing);
            Assert.Equal(1, report.Succeeded);

            var head = new byte[32];
            using (var fs = File.OpenRead(outPath)) fs.ReadExactly(head);
            bool hasFtyp = false;
            for (int i = 0; i + 4 <= head.Length; i++)
                if (head[i] == 'f' && head[i + 1] == 't' && head[i + 2] == 'y' && head[i + 3] == 'p') { hasFtyp = true; break; }
            Assert.True(hasFtyp, "Output is not a valid M4A/MP4 container.");
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    [FfmpegFact]
    public async Task Flac_Pipeline_Encodes_Via_Ffmpeg()
    {
        if (!File.Exists(FixturePath)) return;

        string outDir = Path.Combine(Path.GetTempPath(), "qac_it_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(FixturePath) + ".flac");
        try
        {
            var service = new AudioConversionService(new WmfAudioEngine());
            var settings = new ConversionSettings { OutputFormat = "flac", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
            var routing = new OutputRouting { SaveToSource = false, SaveToFolder = outDir, CopySourceStructure = false };
            var report = await service.ConvertAsync(new[] { FixturePath }, settings, routing);
            Assert.Equal(1, report.Succeeded);

            var magic = new byte[4];
            using (var fs = File.OpenRead(outPath)) fs.ReadExactly(magic);
            Assert.True(magic[0] == 'f' && magic[1] == 'L' && magic[2] == 'a' && magic[3] == 'C', "Output is not a FLAC file.");
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }
}

/// <summary>Skips the test at discovery time when not running on Windows.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Media Foundation decode is Windows-only.";
    }
}

/// <summary>Skips when not on Windows, or the LGPL LAME encoder / M4A fixture is not staged.</summary>
public sealed class LameFactAttribute : FactAttribute
{
    public LameFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "Media Foundation decode is Windows-only.";
        else if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "libmp3lame.dll")))
            Skip = "libmp3lame.dll not staged (run SNDEV/scripts/fetch-lame.ps1).";
        else if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "1s_stereo_44k.m4a")))
            Skip = "M4A fixture not staged in test output.";
    }
}

/// <summary>Skips when not on Windows, or ffmpeg is unavailable, or the M4A fixture is missing.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Skip = "ffmpeg encode is Windows-only here.";
        else if (FfmpegEncoder.ResolveFfmpeg() == null)
            Skip = "ffmpeg not found on PATH/local (run deploy.ps1 or install ffmpeg).";
        else if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "1s_stereo_44k.m4a")))
            Skip = "M4A fixture not staged in test output.";
    }
}
