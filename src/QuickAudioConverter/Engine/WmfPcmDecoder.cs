// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Decodes an audio file to 16-bit interleaved PCM using the Media Foundation Source Reader
/// (demux + decode only). Media Foundation must already be initialized (MFStartup) by the caller.
/// PCM is exposed in chunks so it can be streamed to either a manual WAV writer or the LAME encoder.
///
/// A single WMF sample can contain more PCM than the caller's buffer, so this reader is stateful:
/// it locks each sample's buffer and drains it fully across multiple <see cref="Read"/> calls
/// before requesting the next sample (the previous truncation bug dropped the remainder).
/// </summary>
internal sealed class WmfPcmDecoder : IDisposable
{
    private readonly ComPointer _reader;
    private bool _disposed;

    // State for the sample currently being drained.
    private ComPointer? _buffer;   // locked IMFMediaBuffer (for Unlock + Release)
    private IntPtr _data;          // current read pointer into the locked buffer
    private int _remaining;        // bytes left in the locked buffer
    private bool _endOfStream;

    public int SampleRate { get; }
    public int Channels { get; }
    public int BitsPerSample { get; } = 16;

    private WmfPcmDecoder(ComPointer reader, int sampleRate, int channels)
    {
        _reader = reader;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>
    /// Opens <paramref name="input"/> and configures the Source Reader to emit 16-bit PCM at the
    /// parameters in <paramref name="s"/>. Assumes Media Foundation is already started.
    /// </summary>
    public static WmfPcmDecoder Open(string input, ConversionSettings s)
    {
        HResult.ThrowIfFailed(
            MediaFoundation.MFCreateSourceReaderFromURL(input, IntPtr.Zero, out IntPtr readerPtr),
            "MFCreateSourceReaderFromURL");
        var reader = new ComPointer(readerPtr);
        try
        {
            using var mt = CreatePcmType(s);
            int hr = reader.As<MediaFoundation.SetCurrentMediaTypeDlg>(7)(
                reader.Raw, MediaFoundation.MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, mt.Raw);
            HResult.ThrowIfFailed(hr, "SetCurrentMediaType");
            return new WmfPcmDecoder(reader, s.SampleRate, s.Channels);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Reads up to <c>pcm.Length</c> interleaved 16-bit samples. Returns samples read (0 = end of stream).</summary>
    public int Read(short[] pcm)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WmfPcmDecoder));

        int copied = 0;
        while (copied < pcm.Length)
        {
            if (_remaining == 0)
            {
                if (!AdvanceToNextSample()) break;
            }

            int samplesAvailable = _remaining / 2;
            int need = pcm.Length - copied;
            int take = Math.Min(samplesAvailable, need);
            Marshal.Copy(_data, pcm, copied, take);
            copied += take;
            _data = IntPtr.Add(_data, take * 2);
            _remaining -= take * 2;
        }
        return copied;
    }

    private bool AdvanceToNextSample()
    {
        // Release the buffer we just finished draining.
        if (_buffer != null)
        {
            _buffer.As<UnlockDlg>(4)(_buffer.Raw);
            _buffer.Dispose();
            _buffer = null;
            _data = IntPtr.Zero;
            _remaining = 0;
        }

        if (_endOfStream) return false;

        while (true)
        {
            int hr = _reader.As<MediaFoundation.ReadSampleDlg>(9)(
                _reader.Raw, MediaFoundation.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0,
                out _, out uint flags, out _, out IntPtr samplePtr);
            if (hr < 0 || samplePtr == IntPtr.Zero)
            {
                _endOfStream = true;
                return false;
            }

            using (var sample = new ComPointer(samplePtr))
            {
                // IMFSample::ConvertToContiguousBuffer (slot 41: IUnknown 0-2 + 30 IMFAttributes
                // methods, so IMFSample starts at 33; ConvertToContiguousBuffer = 41).
                int hr2 = sample.As<ConvertToContiguousBufferDlg>(41)(sample.Raw, out IntPtr bufPtr);
                if (hr2 < 0) { _endOfStream = true; return false; }
                var buffer = new ComPointer(bufPtr);
                // IMFMediaBuffer::Lock (slot 3): BYTE** ppbBuffer, DWORD* pcbMaxLength, DWORD* pcbCurrentLength.
                int hr3 = buffer.As<LockDlg>(3)(buffer.Raw, out IntPtr pData, out _, out uint curLen);
                if (hr3 < 0) { buffer.Dispose(); _endOfStream = true; return false; }
                _data = pData;
                _remaining = (int)curLen;
                _buffer = buffer;
            }
            // The IMFSample is released here; the contiguous buffer it produced stays valid
            // until we Unlock + Release it above. Skip empty samples and keep pulling.
            if (_remaining > 0) return true;
            _buffer.As<UnlockDlg>(4)(_buffer.Raw);
            _buffer.Dispose();
            _buffer = null;
            _data = IntPtr.Zero;
            _remaining = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_buffer != null)
        {
            _buffer.As<UnlockDlg>(4)(_buffer.Raw);
            _buffer.Dispose();
            _buffer = null;
        }
        _reader?.Dispose();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ConvertToContiguousBufferDlg(IntPtr self, out IntPtr ppBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LockDlg(IntPtr self, out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnlockDlg(IntPtr self);

    private static ComPointer CreatePcmType(ConversionSettings s)
    {
        HResult.ThrowIfFailed(MediaFoundation.MFCreateMediaType(out IntPtr mt), "MFCreateMediaType");
        var cp = new ComPointer(mt);
        try
        {
            MediaFoundation.SetAttrGUID(cp, MediaFoundation.MF_MT_MAJOR_TYPE, MediaFoundation.MFMediaType_Audio);
            MediaFoundation.SetAttrGUID(cp, MediaFoundation.MF_MT_SUBTYPE, MediaFoundation.MFAudioFormat_PCM);
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_NUM_CHANNELS, (uint)s.Channels);
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)s.SampleRate);
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(s.SampleRate * s.Channels * 2));
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)(s.Channels * 2));
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        }
        catch
        {
            cp.Dispose();
            throw;
        }
        return cp;
    }
}
