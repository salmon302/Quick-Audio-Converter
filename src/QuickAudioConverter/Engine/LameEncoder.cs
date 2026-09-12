// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.IO;

/// <summary>
/// Encodes 16-bit interleaved PCM to MP3 using libmp3lame. Streaming: call <see cref="Encode"/>
/// once per PCM chunk, then <see cref="Flush"/> at end of stream. The instance is single-threaded
/// and must be used sequentially.
/// </summary>
internal sealed class LameEncoder : IDisposable
{
    private IntPtr _gfp;
    private readonly int _channels;
    private bool _disposed;

    public LameEncoder(int sampleRate, int channels, int bitrateKbps, int quality = 2)
    {
        _channels = channels;
        _gfp = LameNative.lame_init();
        if (_gfp == IntPtr.Zero)
            throw new InvalidOperationException("lame_init failed (null handle). Is libmp3lame.dll present?");

        ThrowIfNeg(LameNative.lame_set_in_samplerate(_gfp, sampleRate), "lame_set_in_samplerate");
        ThrowIfNeg(LameNative.lame_set_num_channels(_gfp, channels), "lame_set_num_channels");
        ThrowIfNeg(LameNative.lame_set_brate(_gfp, bitrateKbps), "lame_set_brate");
        int mode = channels == 1 ? LameNative.LAME_MONO : LameNative.LAME_JOINT_STEREO;
        ThrowIfNeg(LameNative.lame_set_mode(_gfp, mode), "lame_set_mode");
        ThrowIfNeg(LameNative.lame_set_quality(_gfp, quality), "lame_set_quality");
        ThrowIfNeg(LameNative.lame_init_params(_gfp), "lame_init_params");
    }

    /// <summary>Encodes <paramref name="sampleCount"/> interleaved 16-bit samples from <paramref name="pcm"/>.</summary>
    public void Encode(short[] pcm, int sampleCount, Stream output)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LameEncoder));
        if (sampleCount <= 0) return;
        // LAME's num_samples is per channel. For interleaved data that is total/count = sampleCount/channels.
        int numSamples = sampleCount / _channels;
        int mp3BufSize = (int)(1.25 * numSamples + 7200);
        byte[] mp3 = new byte[mp3BufSize];
        int written = LameNative.lame_encode_buffer_interleaved(_gfp, pcm, numSamples, mp3, mp3BufSize);
        ThrowIfNeg(written, "lame_encode_buffer_interleaved");
        if (written > 0) output.Write(mp3, 0, written);
    }

    /// <summary>Flushes the final MP3 frames (encoder delay/padding) to <paramref name="output"/>.</summary>
    public void Flush(Stream output)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LameEncoder));
        byte[] mp3 = new byte[7200];
        int written = LameNative.lame_encode_flush(_gfp, mp3, mp3.Length);
        ThrowIfNeg(written, "lame_encode_flush");
        if (written > 0) output.Write(mp3, 0, written);
    }

    private static void ThrowIfNeg(int hr, string op)
    {
        if (hr < 0) throw new InvalidOperationException($"{op} failed: LAME code {hr}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_gfp != IntPtr.Zero)
        {
            LameNative.lame_close(_gfp);
            _gfp = IntPtr.Zero;
        }
    }
}
