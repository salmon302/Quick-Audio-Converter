// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using QuickAudioConverter.Engine;
using Xunit;

public class ConversionSettingsTests
{
    [Fact]
    public void Defaults_Match_Srs_Baseline()
    {
        var s = new ConversionSettings();
        Assert.Equal("mp3", s.OutputFormat);
        Assert.Equal(224, s.BitrateKbps);
        Assert.Equal(2, s.Channels);
        Assert.Equal(44100, s.SampleRate);
    }
}
