// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using System.Text.Json;
using QuickAudioConverter.Engine;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void Json_RoundTrip_Preserves_Fields()
    {
        var s = new AppSettings
        {
            OutputFormat = "wav",
            BitrateKbps = 320,
            Channels = 1,
            SampleRate = 48000,
            Quality = 5,
            SaveToSource = false,
            DefaultSaveFolder = @"C:\out",
            CopySourceStructure = true,
            ShellIntegrationEnabled = true
        };
        s.RegisteredExtensions.Add(".m4a");
        s.RegisteredExtensions.Add(".mp3");

        string json = JsonSerializer.Serialize(s, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal("wav", back!.OutputFormat);
        Assert.Equal(320, back.BitrateKbps);
        Assert.Equal(1, back.Channels);
        Assert.Equal(48000, back.SampleRate);
        Assert.Equal(5, back.Quality);
        Assert.False(back.SaveToSource);
        Assert.Equal(@"C:\out", back.DefaultSaveFolder);
        Assert.True(back.CopySourceStructure);
        Assert.True(back.ShellIntegrationEnabled);
        Assert.Equal(2, back.RegisteredExtensions.Count);
    }

    [Fact]
    public void DecodableExtensions_Are_Populated_And_Dotted()
    {
        Assert.Contains(".m4a", AppSettings.DecodableExtensions);
        Assert.Contains(".wav", AppSettings.DecodableExtensions);
        Assert.Contains(".mp3", AppSettings.DecodableExtensions);
        Assert.All(AppSettings.DecodableExtensions, e => Assert.StartsWith(".", e));
    }

    [Fact]
    public void Defaults_Are_Sensible()
    {
        var s = new AppSettings();
        Assert.Equal("mp3", s.OutputFormat);
        Assert.Equal(224, s.BitrateKbps);
        Assert.Equal(2, s.Channels);
        Assert.Equal(44100, s.SampleRate);
        Assert.True(s.SaveToSource);
    }
}
