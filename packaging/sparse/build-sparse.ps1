<#
.SYNOPSIS
    Builds and signs the Quick Audio Converter sparse package (.msix) used for the Windows 11
    top-level context-menu integration.

.DESCRIPTION
    A "sparse package" is an MSIX with no payload; it grants the desktop EXE a package identity so
    File Explorer promotes the IExplorerCommand to the top-level menu. This script:
      1. Creates placeholder 1x1 logo assets (visual assets are required by schema validation).
      2. Packs AppxManifest.xml into QuickAudioConverter.Sparse.msix with makeappx.exe.
      3. Creates a self-signed cert (if needed) and signs the .msix with signtool.exe.
    The Windows SDK (makeappx/signtool) must be on PATH; if absent the script reports what is
    missing and exits non-zero without producing a package.

.PARAMETER InstallDir
    Directory containing QuickAudioConverter.exe; the manifest is packed with -ExternalLocation
    pointing here so the relative Executable resolves at Add-AppxPackage time. Defaults to the
    directory containing this script's parent (the project's published output).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Resolve-Path (Join-Path $PSScriptRoot "..\..\src\QuickAudioConverter\bin\Release\net9.0-windows\win-x64\publish")),
    [string]$Publisher  = "CN=QuickAudioConverter",
    [string]$OutputName = "QuickAudioConverter.Sparse.msix"
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Find-SdkTool {
    param([string]$Tool)
    $p = Get-Command $Tool -ErrorAction SilentlyContinue
    if ($p) { return $p.Source }
    # Common Windows SDK / VS install locations.
    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\$Tool",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\$Tool",
        "${env:ProgramFiles}\Microsoft Visual Studio\*\*\VC\Tools\*\bin\Hostx64\x64\$Tool"
    )
    foreach ($c in $candidates) {
        $found = Resolve-Path $c -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($found) { return $found.Path }
    }
    return $null
}

$makeappx = Find-SdkTool makeappx.exe
$signtool = Find-SdkTool signtool.exe

if (-not $makeappx) {
    Write-Warning "makeappx.exe not found on PATH or in the Windows SDK. Install the Windows SDK and re-run."
    Write-Warning "Without it, the sparse package cannot be built here; copy a prebuilt $OutputName next to the EXE instead."
    exit 2
}

# 1) Placeholder logo assets (1x1 transparent PNG).
$assetsDir = Join-Path $here "Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$logo = Join-Path $assetsDir "Logo.png"
if (-not (Test-Path $logo)) {
    # Minimal valid 1x1 PNG (89 50 4E 47 0D 0A 1A 0A ...). Precomputed bytes for a 1x1 transparent image.
    $png = [byte[]]@(
        0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
        0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
        0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
        0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
        0x42,0x60,0x82
    )
    [System.IO.File]::WriteAllBytes($logo, $png)
}

# 2) Pack.
$output = Join-Path $here $OutputName
if (Test-Path $output) { Remove-Item $output -Force }
Write-Host "Packing sparse package -> $output"
& $makeappx pack /d $here /p $output /o /nv
if ($LASTEXITCODE -ne 0) { Write-Error "makeappx failed ($LASTEXITCODE)."; exit $LASTEXITCODE }

# 3) Sign.
if (-not $signtool) {
    Write-Warning "signtool.exe not found. The .msix was created but is UNSIGNED; Add-AppxPackage will reject it unless a trusted cert is installed separately."
    exit 3
}

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Publisher } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating self-signed cert '$Publisher'..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
        -KeyUsage DigitalSignature -FriendlyName "Quick Audio Converter Sparse Package" `
        -CertStoreLocation Cert:\CurrentUser\My
    # Export + trust into TrustedPeople so Add-AppxPackage (non-Store) accepts it.
    $pfx = Join-Path $env:TEMP "qac_sparse.pfx"
    $null = Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString -String "qac" -Force -AsPlainText)
    $null = Import-Certificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\TrustedPeople
}

Write-Host "Signing sparse package with cert $($cert.Thumbprint)..."
& $signtool sign /fd SHA256 /a /sm /s My /n $Publisher $output
if ($LASTEXITCODE -ne 0) { Write-Error "signtool failed ($LASTEXITCODE)."; exit $LASTEXITCODE }

Write-Host "Built and signed: $output"
Write-Host "Install with: Add-AppxPackage -Path '$output' -ExternalLocation '$InstallDir'"
exit 0
