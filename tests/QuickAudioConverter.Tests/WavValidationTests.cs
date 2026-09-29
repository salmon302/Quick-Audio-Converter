// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using System;
using System.IO;
using QuickAudioConverter.Engine;
using Xunit;

public class WavValidationTests
{
    [Fact]
    public void Rejects_Truncated_Header()
    {
        var bytes = new byte[10];
        Assert.False(WavValidation.IsValidWav(bytes.AsSpan(0, 10), 0));
    }

    [Fact]
    public void Accepts_Writer_Output_Via_File_Overload()
    {
        using var ms = new MemoryStream();
        using (var wav = new WavWriter(ms, 44100, 2, 16))
        {
            var pcm = new short[2000];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = 1234;
            wav.WriteSamples(pcm, pcm.Length);
        }

        string tmp = Path.Combine(Path.GetTempPath(), "qac_val_" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(tmp, ms.ToArray());
        try
        {
            Assert.True(WavValidation.IsValidWav(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Rejects_Wrong_AudioFormat_Or_BitDepth()
    {
        // Header claims IEEE float (format 3) at 32 bits -> must be rejected (we only write PCM/16).
        var bytes = BuildHeader(audioFormat: 3, bits: 32, dataSize: 4);
        Assert.False(WavValidation.IsValidWav(bytes.AsSpan(0, 44), 4));
    }

    private static byte[] BuildHeader(int audioFormat, int bits, int dataSize)
    {
        var b = new byte[44];
        void Put(int off, int v)
        {
            b[off] = (byte)(v & 0xFF);
            b[off + 1] = (byte)((v >> 8) & 0xFF);
            b[off + 2] = (byte)((v >> 16) & 0xFF);
            b[off + 3] = (byte)((v >> 24) & 0xFF);
        }

        b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F';
        Put(4, 36 + dataSize);
        b[8] = (byte)'W'; b[9] = (byte)'A'; b[10] = (byte)'V'; b[11] = (byte)'E';
        b[12] = (byte)'f'; b[13] = (byte)'m'; b[14] = (byte)'t'; b[15] = (byte)' ';
        Put(16, 16);
        Put(20, audioFormat);
        Put(22, 2);            // channels
        Put(24, 44100);        // sample rate
        Put(28, 44100 * 2 * 2); // avg bytes/sec
        Put(32, 4);            // block align
        Put(34, bits);
        b[36] = (byte)'d'; b[37] = (byte)'a'; b[38] = (byte)'t'; b[39] = (byte)'a';
        Put(40, dataSize);
        return b;
    }
}
