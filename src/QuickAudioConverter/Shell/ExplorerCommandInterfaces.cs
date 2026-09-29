// SPDX-License-Identifier: MIT
namespace QuickAudioConverter.Shell;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// COM interface definitions for the IExplorerCommand integration. All are IUnknown-derived
/// ([InterfaceType(InterfaceIsIUnknown)]), so the runtime supplies AddRef/Release/QueryInterface;
/// we declare only our methods in the exact vtable order expected by the shell.
/// </summary>

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent(out IShellItem? ppsi);
    [PreserveSig] int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttributes);
    [PreserveSig] int Compare(IShellItem? psi, uint hint, out int piOrder);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
internal interface IShellItemArray
{
    [PreserveSig] int BindToStorage(IntPtr pkey, IntPtr riid, out IntPtr ppv);
    [PreserveSig] int GetCount(out uint pdwCount);
    [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem? ppsi);
    [PreserveSig] int EnumItems(out IntPtr ppenum);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A2CFF57C-0E3A-4A48-BD7E-22BD8AB7D7A5")]
internal interface IExplorerCommand
{
    [PreserveSig] int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName);
    [PreserveSig] int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon);
    [PreserveSig] int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip);
    [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
    [PreserveSig] int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out EXPCMDSTATE pCmdState);
    [PreserveSig] int Invoke(IShellItemArray? psiItemArray, IntPtr pbc);
    [PreserveSig] int GetFlags(out EXPCMDFLAGS pFlags);
    [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352")]
internal interface IObjectWithSite
{
    [PreserveSig] int SetSite(IntPtr pUnkSite);
    [PreserveSig] int GetSite(ref Guid riid, out IntPtr ppvSite);
}
