// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;

/// <summary>
/// Stable identities for the IExplorerCommand COM server and the sparse package.
/// These MUST stay in sync with <c>packaging/sparse/AppxManifest.xml</c>: the comServer
/// <c>Class Id</c> must equal <see cref="ExplorerCommandClsid"/>, and the package
/// <c>Identity Name/Publisher</c> must equal <see cref="PackageName"/> / <see cref="PackagePublisher"/>.
/// </summary>
internal static class ExplorerCommandGuids
{
    /// <summary>CLSID for the IExplorerCommand implementation (out-of-process local server).</summary>
    public static readonly Guid ExplorerCommandClsid =
        new("B7E2C1A0-5F3E-4C8A-9B1D-2C4F6E8A0D11");

    /// <summary>Canonical command name returned by IExplorerCommand.GetCanonicalName.</summary>
    public static readonly Guid CommandCanonicalName =
        new("C0A1F2B3-4D5E-6F70-8192-A3B4C5D6E7F8");

    public static readonly Guid IID_IExplorerCommand =
        new("A2CFF57C-0E3A-4A48-BD7E-22BD8AB7D7A5");
    public static readonly Guid IID_IObjectWithSite =
        new("FC4801A3-2BA9-11CF-A229-00AA003D7352");
    public static readonly Guid IID_IUnknown =
        new("00000000-0000-0000-C000-000000000046");

    /// <summary>Sparse-package identity — MUST match AppxManifest.xml.</summary>
    public const string PackageName = "QuickAudioConverter.Sparse";
    public const string PackagePublisher = "CN=QuickAudioConverter";
}
