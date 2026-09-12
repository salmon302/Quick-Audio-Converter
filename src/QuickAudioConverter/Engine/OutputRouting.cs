// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Engine;

/// <summary>
/// Controls where converted files are written.
/// </summary>
internal sealed class OutputRouting
{
    public bool SaveToSource { get; init; }
    public string? SaveToFolder { get; init; }
    public bool CopySourceStructure { get; init; }
}
