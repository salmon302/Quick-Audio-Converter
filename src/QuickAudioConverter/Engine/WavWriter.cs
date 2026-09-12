// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.IO;

/// <summary>
/// Writes a canonical 44-byte RIFF/WAVE file (PCM) with a streaming, back-patched header.
/// This deliberately avoids the Media Foundation Sink Writer so the WAV (PCM passthrough) path
/// has zero COM/MFT overhead — the file is just a header plus the raw PCM buffer.
/// </summary>
internal sealed class WavWriter : IDisposable
{
    private readonly Stream _out;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly short _bitsPerSample;
    private long _dataBytes;
    private bool _disposed;

    public WavWriter(Stream output, int sampleRate, int channels, int bitsPerSample = 16)
    {
        _out = output ?? throw new ArgumentNullException(nameof(output));
        _sampleRate = sampleRate;
        _channels = channels;
        _bitsPerSample = (short)bitsPerSample;
        if (_bitsPerSample != 16)
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Only 16-bit PCM is supported.");
        WriteHeader(0);
    }

    public void WriteSamples(short[] pcm, int sampleCount)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WavWriter));
        if (sampleCount <= 0) return;
        // Raw little-endian bytes (target is win-x64). Buffer.BlockCopy preserves byte order.
        byte[] bytes = new byte[sampleCount * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        _out.Write(bytes, 0, bytes.Length);
        _dataBytes += bytes.Length;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _out.Flush();
        _out.Seek(0, SeekOrigin.Begin);
        WriteHeader((int)_dataBytes);
        _out.Flush();
        // The underlying stream is owned by the caller and disposed by its own `using`.
    }

    private void WriteHeader(int dataLength)
    {
        int blockAlign = _channels * (_bitsPerSample / 8);
        int avgBytes = _sampleRate * blockAlign;
        WriteInt32(_out, 0x46464952);          // "RIFF"
        WriteInt32(_out, 36 + dataLength);     // file size - 8
        WriteInt32(_out, 0x45564157);          // "WAVE"
        WriteInt32(_out, 0x20746D66);          // "fmt "
        WriteInt32(_out, 16);                  // fmt chunk size
        WriteInt16(_out, 1);                   // PCM
        WriteInt16(_out, _channels);
        WriteInt32(_out, _sampleRate);
        WriteInt32(_out, avgBytes);
        WriteInt16(_out, blockAlign);
        WriteInt16(_out, _bitsPerSample);
        WriteInt32(_out, 0x61746164);          // "data"
        WriteInt32(_out, dataLength);
    }

    private static void WriteInt32(Stream s, int v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 24) & 0xFF));
    }

    private static void WriteInt16(Stream s, int v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }
}
