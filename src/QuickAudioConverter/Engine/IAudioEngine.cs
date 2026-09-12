// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Abstraction over a conversion backend (Windows Media Foundation in this build).
/// </summary>
internal interface IAudioEngine
{
    Task ConvertAsync(string inputPath, string outputPath, ConversionSettings settings, CancellationToken ct = default);
}
