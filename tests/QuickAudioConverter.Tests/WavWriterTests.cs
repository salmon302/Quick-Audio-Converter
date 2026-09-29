// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using System.IO;
using QuickAudioConverter.Engine;
using Xunit;

/// <summary>Regression coverage for the hand-rolled RIFF/WAVE(PCM) writer (no Media Foundation).</summary>
public class WavWriterTests
{
    [Fact]
    public void Writes_Valid_44Byte_Riff_Pcm()
    {
        const int sampleRate = 44100, channels = 2;
        const int frames = 1000; // 1000 stereo frames

        using var ms = new MemoryStream();
        using (var wav = new WavWriter(ms, sampleRate, channels, 16))
        {
            var pcm = new short[frames * channels];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)(i % 30000);
            wav.WriteSamples(pcm, pcm.Length);
        }

        var bytes = ms.ToArray();
        int dataSize = frames * channels * 2;
        Assert.Equal(44 + dataSize, bytes.Length);
        Assert.True(WavValidation.IsValidWav(bytes.AsSpan(0, 44), dataSize));

        // Spot-check a little-endian PCM sample at the start of the data chunk.
        Assert.Equal((byte)0, bytes[44]);
        Assert.Equal((byte)0, bytes[45]);
    }

    [Fact]
    public void Backpatches_Header_DataSize_On_Dispose()
    {
        using var ms = new MemoryStream();
        var wav = new WavWriter(ms, 8000, 1, 16);
        wav.WriteSamples(new short[] { 1, 2, 3, 4 }, 4);
        wav.Dispose();

        var bytes = ms.ToArray();
        int declared = bytes[40] | (bytes[41] << 8) | (bytes[42] << 16) | (bytes[43] << 24);
        Assert.Equal(8, declared); // 4 samples * 2 bytes
        Assert.Equal(44 + 8, bytes.Length);
    }

    [Fact]
    public void Throws_On_Non16Bit()
    {
        using var ms = new MemoryStream();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new WavWriter(ms, 44100, 2, 24));
    }
}
