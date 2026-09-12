// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Audio conversion engine.
///
/// Decoding/demux is always performed by the Media Foundation Source Reader, which works natively
/// on every Windows machine for M4A/AAC, WAV, MP3, and most other containers.
///
/// Encoding is format-specific (see the Recommended Architecture in SRS.md):
///   - WAV  : a 44-byte RIFF header is written manually in C# (no Sink Writer / COM overhead).
///   - MP3  : raw PCM is fed to libmp3lame (LAME) via P/Invoke. This bypasses the Media Foundation
///            MP3 encoder MFT, which is absent on Windows N / un-provisioned installs.
///   - other: legacy Sink Writer path (requires an encoder MFT that may be absent on this OS).
/// </summary>
internal sealed class WmfAudioEngine : IAudioEngine
{
    public Task ConvertAsync(string inputPath, string outputPath, ConversionSettings settings, CancellationToken ct = default)
    {
        return Task.Run(() => ConvertSync(inputPath, outputPath, settings, ct), ct);
    }

    private static void ConvertSync(string input, string output, ConversionSettings s, CancellationToken ct)
    {
        string fmt = s.OutputFormat.ToLowerInvariant();
        MediaFoundation.Log("start fmt=" + fmt);
        HResult.ThrowIfFailed(MediaFoundation.MFStartup(MediaFoundation.MF_VERSION, 0), "MFStartup");
        try
        {
            if (fmt == "wav")
                ConvertViaPcm(input, output, s, ct, toWav: true);
            else if (fmt == "mp3")
                ConvertViaPcm(input, output, s, ct, toWav: false);
            else
                ConvertViaSinkWriter(input, output, s, ct);
        }
        finally
        {
            MediaFoundation.MFShutdown();
            MediaFoundation.Log("shutdown-done");
        }
    }

    /// <summary>
    /// Decode through the Source Reader, then either write a manual WAV header + PCM, or encode
    /// the PCM to MP3 with LAME. This is the path for the two recommended output formats.
    /// </summary>
    private static void ConvertViaPcm(string input, string output, ConversionSettings s, CancellationToken ct, bool toWav)
    {
        using var decoder = WmfPcmDecoder.Open(input, s);
        using var fs = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        int chunkFrames = 4096;
        var buf = new short[chunkFrames * decoder.Channels];

        if (toWav)
        {
            using var wav = new WavWriter(fs, decoder.SampleRate, decoder.Channels, 16);
            int n;
            while (!ct.IsCancellationRequested && (n = decoder.Read(buf)) > 0)
                wav.WriteSamples(buf, n);
        }
        else
        {
            using var lame = new LameEncoder(decoder.SampleRate, decoder.Channels, s.BitrateKbps, quality: 2);
            int n;
            while (!ct.IsCancellationRequested && (n = decoder.Read(buf)) > 0)
                lame.Encode(buf, n, fs);
            lame.Flush(fs);
        }

        MediaFoundation.Log("pcm-done " + (toWav ? "wav" : "mp3"));
    }

    // ---- Legacy Sink Writer path (compressed formats other than MP3) ----

    private static void ConvertViaSinkWriter(string input, string output, ConversionSettings s, CancellationToken ct)
    {
        var inType = CreatePcmType(s);
        MediaFoundation.Log("intype-ok");
        try
        {
            HResult.ThrowIfFailed(
                MediaFoundation.MFCreateSourceReaderFromURL(input, IntPtr.Zero, out IntPtr readerPtr),
                "MFCreateSourceReaderFromURL");
            MediaFoundation.Log("reader-ok");
            using var reader = new ComPointer(readerPtr);

            reader.As<MediaFoundation.SetCurrentMediaTypeDlg>(7)(
                reader.Raw, MediaFoundation.MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, inType.Raw);
            MediaFoundation.Log("setcurrentmediatype-done");

            IntPtr inputTypeForSink = inType.Raw;

            HResult.ThrowIfFailed(
                MediaFoundation.MFCreateSinkWriterFromURL(output, IntPtr.Zero, IntPtr.Zero, out IntPtr writerPtr),
                "MFCreateSinkWriterFromURL");
            MediaFoundation.Log("writer-ok");
            using var writer = new ComPointer(writerPtr);

            using var outType = CreateOutputType(s);
            MediaFoundation.Log("outtype-ok");
            HResult.ThrowIfFailed(writer.As<MediaFoundation.AddStreamDlg>(3)(writer.Raw, outType.Raw, out uint streamIndex), "AddStream");
            MediaFoundation.Log("addstream-ok stream=" + streamIndex);
            HResult.ThrowIfFailed(
                writer.As<MediaFoundation.SetInputMediaTypeDlg>(4)(writer.Raw, streamIndex, inputTypeForSink, IntPtr.Zero),
                "SetInputMediaType");
            MediaFoundation.Log("setinput-ok");
            HResult.ThrowIfFailed(writer.As<MediaFoundation.BeginWritingDlg>(5)(writer.Raw), "BeginWriting");
            MediaFoundation.Log("beginwriting-ok");

            int samples = 0;
            while (!ct.IsCancellationRequested)
            {
                int hr = reader.As<MediaFoundation.ReadSampleDlg>(9)(
                    reader.Raw, MediaFoundation.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0,
                    out _, out uint flags, out _, out IntPtr samplePtr);
                if (hr < 0) { MediaFoundation.Log("readsample hr<0 break"); break; }
                if ((flags & MediaFoundation.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    if (samplePtr != IntPtr.Zero)
                    {
                        writer.As<MediaFoundation.WriteSampleDlg>(6)(writer.Raw, streamIndex, samplePtr);
                        Marshal.Release(samplePtr);
                        samples++;
                    }
                    MediaFoundation.Log("eos break samples=" + samples);
                    break;
                }
                if (samplePtr == IntPtr.Zero)
                {
                    MediaFoundation.Log("empty-read break samples=" + samples);
                    break;
                }
                writer.As<MediaFoundation.WriteSampleDlg>(6)(writer.Raw, streamIndex, samplePtr);
                Marshal.Release(samplePtr);
                samples++;
                if ((samples & 0x3FF) == 0) MediaFoundation.Log("wrote sample " + samples);
            }
            MediaFoundation.Log("loop-done samples=" + samples);

            HResult.ThrowIfFailed(writer.As<MediaFoundation.FinalizeDlg>(11)(writer.Raw), "Finalize");
            MediaFoundation.Log("finalize-ok");
        }
        finally
        {
            inType.Dispose();
        }
    }

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

    private static ComPointer CreateOutputType(ConversionSettings s)
    {
        string fmt = s.OutputFormat.ToLowerInvariant();
        HResult.ThrowIfFailed(MediaFoundation.MFCreateMediaType(out IntPtr mt), "MFCreateMediaType");
        var cp = new ComPointer(mt);
        try
        {
            Guid subtype = fmt switch
            {
                "mp3" => MediaFoundation.MFAudioFormat_MP3,
                "aac" => MediaFoundation.MFAudioFormat_AAC,
                "m4a" => MediaFoundation.MFAudioFormat_AAC,
                "flac" => MediaFoundation.MFAudioFormat_FLAC,
                _ => MediaFoundation.MFAudioFormat_PCM
            };
            MediaFoundation.SetAttrGUID(cp, MediaFoundation.MF_MT_MAJOR_TYPE, MediaFoundation.MFMediaType_Audio);
            MediaFoundation.SetAttrGUID(cp, MediaFoundation.MF_MT_SUBTYPE, subtype);
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_NUM_CHANNELS, (uint)s.Channels);
            MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)s.SampleRate);

            if (subtype == MediaFoundation.MFAudioFormat_PCM)
            {
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(s.SampleRate * s.Channels * 2));
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)(s.Channels * 2));
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            }
            else
            {
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(s.BitrateKbps * 1000 / 8));
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BLOCK_ALIGNMENT, 1);
                MediaFoundation.SetAttrUINT32(cp, MediaFoundation.MF_MT_AUDIO_BITS_PER_SAMPLE, 0);
                if (subtype == MediaFoundation.MFAudioFormat_MP3)
                {
                    byte[] userData = BuildMp3UserData((ushort)s.Channels, (uint)s.SampleRate, (uint)(s.BitrateKbps * 1000 / 8));
                    MediaFoundation.SetAttrBlob(cp, MediaFoundation.MF_MT_USER_DATA, userData);
                }
            }
        }
        catch
        {
            cp.Dispose();
            throw;
        }
        return cp;
    }

    private static byte[] BuildMp3UserData(ushort channels, uint sampleRate, uint avgBytesPerSec)
    {
        var blob = new byte[30];
        using var ms = new MemoryStream(blob);
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0x0055);   // wFormatTag = WAVE_FORMAT_MPEGLAYER3
        w.Write(channels);         // nChannels
        w.Write(sampleRate);       // nSamplesPerSec
        w.Write(avgBytesPerSec);   // nAvgBytesPerSec
        w.Write((ushort)1);        // nBlockAlign
        w.Write((ushort)0);        // wBitsPerSample
        w.Write((ushort)12);       // cbSize of the MPEG layer-3 specific data
        w.Write((ushort)1);        // wID = MPEGLAYER3_ID_MPEG
        w.Write((uint)0);          // fdwFlags = MPEGLAYER3_FLAG_PADDING_OFF
        w.Write((ushort)1);        // nBlockSize
        w.Write((ushort)1);        // nFramesPerBlock
        w.Write((ushort)0x0571);   // nCodecDelay (standard)
        return blob;
    }
}
