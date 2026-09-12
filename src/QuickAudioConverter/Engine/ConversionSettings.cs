// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

/// <summary>
/// Encoding parameters for a single conversion.
/// </summary>
internal sealed class ConversionSettings
{
    public string OutputFormat { get; init; } = "mp3";
    public int BitrateKbps { get; init; } = 224;
    public int Channels { get; init; } = 2; // 1 = mono, 2 = stereo
    public int SampleRate { get; init; } = 44100;
}
