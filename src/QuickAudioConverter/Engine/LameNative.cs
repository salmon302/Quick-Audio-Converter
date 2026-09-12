// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// P/Invoke bindings for libmp3lame (the LAME MP3 encoder). Calling convention is cdecl
/// (confirmed via `dumpbin /exports` on the bundled libmp3lame.dll — names are undecorated).
/// Native-AOT compatible: plain P/Invoke, no COM, no reflection.
///
/// LAME is distributed under the LGPL; the bundled binary provenance/source is documented in
/// SRS.md (Audio Engine) and SNDEV/scripts/fetch-lame.ps1.
/// </summary>
internal static class LameNative
{
    private const string Dll = "libmp3lame";

    // MPEG_mode constants from lame.h.
    public const int LAME_STEREO = 0;
    public const int LAME_JOINT_STEREO = 1;
    public const int LAME_DUAL_CHANNEL = 2;
    public const int LAME_MONO = 3;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lame_init();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_set_in_samplerate(IntPtr gfp, int sampleRate);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_set_num_channels(IntPtr gfp, int numChannels);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_set_brate(IntPtr gfp, int bitrateKbps);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_set_mode(IntPtr gfp, int mode);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_set_quality(IntPtr gfp, int quality);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_init_params(IntPtr gfp);

    // pcm is interleaved 16-bit samples; numSamples = total samples (L+R counted as 2 per frame).
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_encode_buffer_interleaved(IntPtr gfp, short[] pcm, int numSamples, byte[] mp3buf, int mp3bufSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_encode_flush(IntPtr gfp, byte[] mp3buf, int mp3bufSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lame_close(IntPtr gfp);
}
