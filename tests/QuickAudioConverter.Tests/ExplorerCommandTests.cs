// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Tests;

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using QuickAudioConverter.Shell;
using Xunit;

/// <summary>
/// Static verification of the ExplorerCommand / sparse-package integration. These tests require no
/// Windows 11 or Windows SDK; they confirm the manifest is well-formed and that its identity, COM
/// CLSID, and the FileExplorerContextMenu extension stay in sync with the C# constants.
/// </summary>
public class ExplorerCommandTests
{
    private static string ManifestPath =>
        Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml");

    private static XDocument LoadManifest()
    {
        Assert.True(File.Exists(ManifestPath),
            "AppxManifest.xml was not copied next to the test assembly. Check the test csproj None include.");
        return XDocument.Load(ManifestPath);
    }

    [Fact]
    public void Manifest_Parses_And_Has_Sparse_Identity()
    {
        var doc = LoadManifest();
        var ns = doc.Root!.GetDefaultNamespace();
        var identity = doc.Root.Element(ns + "Identity");
        Assert.NotNull(identity);
        Assert.Equal("QuickAudioConverter.Sparse", identity!.Attribute("Name")!.Value);
        Assert.Equal("CN=QuickAudioConverter", identity.Attribute("Publisher")!.Value);
        Assert.Equal("x64", identity.Attribute("ProcessorArchitecture")!.Value);
    }

    [Fact]
    public void Manifest_ComServer_ClassId_Matches_Code()
    {
        var doc = LoadManifest();
        var ns = doc.Root!.GetDefaultNamespace();
        var comNs = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/com/windows10");

        var comExt = doc.Descendants(comNs + "Extension")
            .FirstOrDefault(e => e.Attribute("Category")!.Value == "windows.comServer");
        Assert.NotNull(comExt);

        var classEl = comExt!.Descendants(comNs + "Class").FirstOrDefault();
        Assert.NotNull(classEl);

        var expected = ExplorerCommandGuids.ExplorerCommandClsid.ToString("D").ToUpperInvariant();
        Assert.Equal(expected, classEl!.Attribute("Id")!.Value.ToUpperInvariant());
    }

    [Fact]
    public void Manifest_Declares_FileExplorerContextMenu_AppExtension()
    {
        var doc = LoadManifest();
        var uap3Ns = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/uap/windows10/3");

        var appExt = doc.Descendants(uap3Ns + "Extension")
            .FirstOrDefault(e => e.Attribute("Category")!.Value == "windows.appExtension");
        Assert.NotNull(appExt);

        var inner = appExt!.Element(uap3Ns + "AppExtension");
        Assert.NotNull(inner);
        Assert.Equal("com.microsoft.windows.fileexplorercontextmenu",
            inner!.Attribute("Name")!.Value);
    }

    [Fact]
    public void Manifest_References_Relative_Executable_And_ComServerArgs()
    {
        var doc = LoadManifest();
        var ns = doc.Root!.GetDefaultNamespace();
        var comNs = XNamespace.Get("http://schemas.microsoft.com/appx/manifest/com/windows10");

        var app = doc.Root.Element(ns + "Applications")?
            .Element(ns + "Application");
        Assert.NotNull(app);
        Assert.Equal("QuickAudioConverter.exe", app!.Attribute("Executable")!.Value);
        Assert.Equal("Windows.FullTrustApplication", app.Attribute("EntryPoint")!.Value);

        var oop = doc.Descendants(comNs + "OutOfProcessServer").FirstOrDefault();
        Assert.NotNull(oop);
        Assert.Equal("--comserver", oop!.Attribute("Arguments")!.Value);
    }

    [Fact]
    public void Guids_Are_WellFormed_And_Match_Spec()
    {
        // IExplorerCommand well-known IID.
        Assert.Equal("a2cff57c-0e3a-4a48-bd7e-22bd8ab7d7a5",
            ExplorerCommandGuids.IID_IExplorerCommand.ToString());
        // Our command CLSID is a valid, stable GUID.
        Assert.Equal("b7e2c1a0-5f3e-4c8a-9b1d-2c4f6e8a0d11",
            ExplorerCommandGuids.ExplorerCommandClsid.ToString());
        Assert.NotEqual(Guid.Empty, ExplorerCommandGuids.CommandCanonicalName);
    }

    [Fact]
    public void RegistryPaths_Built_Correctly_For_Hkcu_And_Hklm()
    {
        var clsid = "{" + ExplorerCommandGuids.ExplorerCommandClsid.ToString("B").ToUpperInvariant() + "}";

        Assert.Equal(
            @"HKEY_CURRENT_USER\Software\Classes\CLSID\" + clsid,
            ExplorerCommandRegistration.ClsIdPath(hklm: false));
        Assert.Equal(
            @"HKEY_LOCAL_MACHINE\Software\Classes\CLSID\" + clsid,
            ExplorerCommandRegistration.ClsIdPath(hklm: true));

        var hklmVerbs = ExplorerCommandRegistration.ShellVerbPaths(hklm: true);
        Assert.Contains(hklmVerbs,
            p => p == @"HKEY_LOCAL_MACHINE\Software\Classes\SystemFileAssociations\.m4a\shell\QacModernConvert");
        Assert.All(hklmVerbs, p => Assert.EndsWith(@"\shell\QacModernConvert", p));
    }
}
