// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.IO;

/// <summary>
/// Validates the canonical 44-byte RIFF/WAVE(PCM) header produced by <see cref="WavWriter"/>.
///
/// Shared by the self-test (<c>Program.RunSelfTest</c>) and the verification test project so the
/// header contract is enforced in exactly one place.
/// </summary>
public static class WavValidation
{
    /// <summary>Validates a WAV file on disk (must be at least 44 bytes).</summary>
    public static bool IsValidWav(string path)
    {
        var hdr = new byte[44];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        if (fs.Length < 44) return false;
        if (fs.Read(hdr, 0, 44) != 44) return false;
        return IsValidWav(hdr, (int)(fs.Length - 44));
    }

    /// <summary>Validates a 44-byte header plus the expected PCM data size.</summary>
    public static bool IsValidWav(ReadOnlySpan<byte> header44, int dataSize)
    {
        if (header44.Length < 44) return false;

        bool riff = header44[0] == 'R' && header44[1] == 'I' && header44[2] == 'F' && header44[3] == 'F';
        bool wave = header44[8] == 'W' && header44[9] == 'A' && header44[10] == 'V' && header44[11] == 'E';
        bool fmt = header44[12] == 'f' && header44[13] == 'm' && header44[14] == 't' && header44[15] == ' ';
        bool data = header44[36] == 'd' && header44[37] == 'a' && header44[38] == 't' && header44[39] == 'a';

        int audioFormat = header44[20] | (header44[21] << 8);
        int bits = header44[34] | (header44[35] << 8);
        int declaredDataSize =
            header44[40] | (header44[41] << 8) | (header44[42] << 16) | (header44[43] << 24);

        return riff && wave && fmt && data
            && audioFormat == 1   // PCM
            && bits == 16
            && declaredDataSize == dataSize;
    }
}
