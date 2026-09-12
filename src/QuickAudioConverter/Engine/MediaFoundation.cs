// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Low-level Media Foundation bindings using raw P/Invoke plus manual vtable calls.
/// This avoids declaring COM interfaces (and their IIDs) and is fully Native-AOT compatible.
/// </summary>
internal static class MediaFoundation
{
    public const int MF_VERSION = 0x00020070;
    public const uint MF_SOURCE_READER_FIRST_AUDIO_STREAM = 0xFFFFFFFD;
    public const uint MF_SOURCE_READERF_ENDOFSTREAM = 0x1;

    // Attribute GUIDs (from mfapi.h).
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    public static readonly Guid MF_MT_SUBTYPE = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37E48BF5-645E-4C5B-89DE-ADA9E29B696A");
    public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA");
    public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1AAB75C8-CFEF-451C-AB95-AC034B8E1731");
    public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322DE230-9EEB-43BD-AB7A-FF412251541D");
    public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669");
    public static readonly Guid MF_MT_USER_DATA = new("B6BC765F-4C3B-40A4-BD51-2535B66FE09D");

    // Major / subtype GUIDs (MEDIASUBTYPE pattern: {0000XXXX-0000-0010-8000-00AA00389B71}).
    public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormat_MP3 = new("00000055-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormat_AAC = new("000000FF-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFAudioFormat_FLAC = new("0000F1AC-0000-0010-8000-00AA00389B71");

    [DllImport("mfplat.dll")]
    public static extern int MFStartup(int version, int dwFlags);

    [DllImport("mfplat.dll")]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    public static extern int MFCreateMediaType(out IntPtr ppMediaType);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    public static extern int MFCreateSourceReaderFromURL(string pwszURL, IntPtr pAttributes, out IntPtr ppSourceReader);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    public static extern int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream, IntPtr pAttributes, out IntPtr ppSinkWriter);

    // NOTE: MFInitMediaTypeFromWaveFormatEx is intentionally NOT used. Output media types are built
    // manually via IMFAttributes::SetBlob (MF_MT_USER_DATA) to avoid the native helper's AV under AOT.

    // ---- Vtable delegate signatures (self = COM interface pointer) ----

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetUINT32Dlg(IntPtr self, ref Guid guidKey, uint value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetGUIDDlg(IntPtr self, ref Guid guidKey, ref Guid guidValue);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetBlobDlg(IntPtr self, ref Guid guidKey, IntPtr pBuf, uint cbBufSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetCurrentMediaTypeDlg(IntPtr self, uint dwStreamIndex, IntPtr pReserved, IntPtr pMediaType);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetCurrentMediaTypeDlg(IntPtr self, uint dwStreamIndex, out IntPtr ppMediaType);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int ReadSampleDlg(IntPtr self, uint dwStreamIndex, uint dwControlFlags, out uint pdwActualStreamIndex, out uint pdwStreamFlags, out long pllTimestamp, out IntPtr ppSample);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int AddStreamDlg(IntPtr self, IntPtr pMediaType, out uint pdwStreamIndex);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetInputMediaTypeDlg(IntPtr self, uint dwStreamIndex, IntPtr pMediaType, IntPtr pEncodingParameters);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int BeginWritingDlg(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int WriteSampleDlg(IntPtr self, uint dwStreamIndex, IntPtr pSample);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int FinalizeDlg(IntPtr self);

    // ---- Shared IMFAttributes helpers (vtable slots verified against SDK headers) ----

    internal static void SetAttrGUID(ComPointer cp, Guid key, Guid val)
    {
        Guid k = key, v = val;
        HResult.ThrowIfFailed(cp.As<SetGUIDDlg>(24)(cp.Raw, ref k, ref v), "SetGUID");
    }

    internal static void SetAttrUINT32(ComPointer cp, Guid key, uint val)
    {
        Guid k = key;
        HResult.ThrowIfFailed(cp.As<SetUINT32Dlg>(21)(cp.Raw, ref k, val), "SetUINT32");
    }

    internal static void SetAttrBlob(ComPointer cp, Guid key, byte[] data)
    {
        Guid k = key;
        IntPtr buf = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buf, data.Length);
            int hr = cp.As<SetBlobDlg>(26)(cp.Raw, ref k, buf, (uint)data.Length);
            HResult.ThrowIfFailed(hr, "SetBlob");
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    internal static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "qac_debug.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); }
        catch { }
    }
}

/// <summary>
/// Wraps a raw COM interface pointer and invokes methods by vtable slot.
/// The slot numbers are 0-based from IUnknown (IMFAttributes.SetUINT32 = 21, .SetGUID = 24,
/// .SetBlob = 26; IMFSinkWriter.AddStream = 3, .SetInputMediaType = 4, .BeginWriting = 5,
/// .WriteSample = 6, .Finalize = 11; IMFSourceReader.SetCurrentMediaType = 7,
/// .GetCurrentMediaType = 6, .ReadSample = 9). Verified against the Windows 11 SDK headers.
/// </summary>
internal sealed class ComPointer : IDisposable
{
    private IntPtr _p;
    public ComPointer(IntPtr p) => _p = p;
    public IntPtr Raw => _p;

    public T As<T>(int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_p), slot * IntPtr.Size));

    public void Dispose()
    {
        if (_p != IntPtr.Zero)
        {
            Marshal.Release(_p);
            _p = IntPtr.Zero;
        }
    }
}

internal static class HResult
{
    public static void ThrowIfFailed(int hr, string op)
    {
        if (hr < 0)
            throw new InvalidOperationException($"{op} failed: HRESULT 0x{hr:X8}");
    }
}
