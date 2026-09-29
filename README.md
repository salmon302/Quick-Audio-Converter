# Quick Audio Converter

A small, dependency-light Windows utility for transcoding audio files between common
formats. It ships a WinForms GUI for batch conversion and a Windows File Explorer
context-menu integration so you can convert files directly from the shell without
opening the app.

> **Status:** This is an early implementation. The **Convert** and **Options** tabs are
> functional; the **Home** and **Effects** tabs, the **Play** button, and the **Tags**
> button are placeholders that are not yet wired to features.

---

## Features

- **GUI batch conversion** — drag-and-drop files or whole folders, pick an output format
  and encoding profile, and convert.
- **Explorer context-menu conversion** — right-click one or more supported audio files and
  choose *Convert to &lt;format&gt;*. Runs headlessly (no window), reuses your last-used
  settings, and shows an OS toast when done.
- **Native, minimal footprint** — decoding uses the OS-built-in **Windows Media Foundation**
  Source Reader (0 MB added). WAV is written with a hand-built 44-byte RIFF header; MP3 is
  encoded by **libmp3lame** (LAME) via P/Invoke; M4A/AAC/FLAC are encoded by **FFmpeg**
  invoked headlessly.
- **Persistent "last-used" profile** — the GUI and the context-menu path share the same
  settings file, so the shell uses whatever you last configured.

### Supported formats

| Direction | Formats |
|-----------|---------|
| **Input (decode)** | `.m4a`, `.aac`, `.mp3`, `.wav`, `.flac`, `.wma`, `.ogg`, `.opus`, `.aiff`, `.aif` (decoded via Media Foundation) |
| **Output (encode)** | `mp3` (LAME), `wav` (native RIFF), `m4a`/`aac` (FFmpeg AAC), `flac` (FFmpeg FLAC) |

Output profiles: bitrate (128/192/224/256/320 kbps), channels (Stereo/Mono), quality
(High/Standard/Fast — maps to LAME quality 2/5/7), and sample rate (44.1 kHz by default).

---

## Architecture at a glance

```
                ┌─────────────────────────────┐
   Input file → │ Windows Media Foundation     │  decode/demux (native, 0 deps)
                │ Source Reader (IMFSourceReader)│
                └──────────────┬──────────────┘
                               │ raw 16-bit PCM
                ┌──────────────┴──────────────┐
         Output │                                │
          format│                                │
       ┌────────┴───────┐              ┌─────────┴──────────┐
       │ WAV            │              │ MP3                │
       │ 44-byte RIFF   │              │ libmp3lame (LAME)  │
       │ header in C#   │              │ P/Invoke (LGPL)    │
       └────────────────┘              └────────────────────┘
                                   ┌─────────┴──────────┐
                                   │ M4A / AAC / FLAC   │
                                   │ FFmpeg (headless)  │
                                   └────────────────────┘
```

- **Decoder (always):** `IMFSourceReader` from Media Foundation — no third-party demuxer.
- **WAV encoder:** a 44-byte `RIFF/WAVE` header is written directly in C#
  (`BinaryWriter`/`FileStream`), followed by the raw PCM. No Sink Writer / COM overhead.
- **MP3 encoder:** raw PCM → **libmp3lame** via P/Invoke (`lame_init`, `lame_encode_buffer_interleaved`,
  `lame_encode_flush`, …). This is deliberate: the Media Foundation MP3 encoder MFT is
  absent on Windows N / un-provisioned installs.
- **M4A / AAC / FLAC encoders:** a bundled or system **FFmpeg** is invoked headlessly
  (`-c:a aac` / `-c:a flac`). The Media Foundation AAC/FLAC encoder MFTs are likewise
  absent on those Windows builds.
- **Shell verbs:** static registry verbs under `HKCU` (per-user, no admin).
- **Notifications:** Windows.UI.Notifications (WinRT) toast — no bundled daemon.

### Third-party binaries (git-ignored, not committed)

| Binary | Role | License | How it gets staged |
|--------|------|---------|--------------------|
| `libmp3lame.dll` | MP3 encoding | LGPL-2.1 | `SNDEV/scripts/fetch-lame.ps1` → `src/QuickAudioConverter/native/libmp3lame.dll`, copied next to the EXE. |
| `ffmpeg.exe` | M4A/AAC/FLAC encoding | GPL/LGPL (per build) | Placed next to the EXE or resolved from `PATH`. |

Both are **side-by-side, user-replaceable**, and excluded from the source repository
(see `.gitignore`). Attribution for LAME ships in `src/QuickAudioConverter/licenses/`
(`THIRD_PARTY.md` + `LGPL-2.1.txt`), which is copied next to the published EXE.

---

## Building from source

### Prerequisites

- **.NET 9 SDK** (the repo pins `9.0.317` with `rollForward: latestFeature` in `global.json`).
  The maintainer build uses a user-local SDK at `C:\Users\salmo\.dotnet9\dotnet.exe`;
  any `dotnet` 9.x on `PATH` works too.
- Windows 10/11 (x64). The app is `win-x64` AOT by default.
- *(Optional)* `libmp3lame.dll` for MP3 output, and/or `ffmpeg.exe` for M4A/AAC/FLAC output.

### Build

```powershell
# Debug build
dotnet build QuickAudioConverter.sln

# Standalone Native AOT publish (no .NET runtime required on the target machine)
dotnet publish src/QuickAudioConverter/QuickAudioConverter.csproj -c Release -r win-x64
# Output: src/QuickAudioConverter/bin/Release/net9.0-windows/win-x64/publish/
```

The published folder contains `QuickAudioConverter.exe` plus the `licenses/` directory.

### Stage the optional native encoders

MP3 and M4A/AAC/FLAC output need their native binaries next to the EXE:

```powershell
# MP3 — LAME (LGPL). Copy any compatible build to:
#   src/QuickAudioConverter/native/libmp3lame.dll   (auto-copied to output on build/publish)
# Or run the maintainer helper (git-ignored, agent-internal):
#   SNDEV/scripts/fetch-lame.ps1

# M4A / AAC / FLAC — FFmpeg
# Place ffmpeg.exe next to the published QuickAudioConverter.exe, or ensure it is on PATH.
```

If `libmp3lame.dll` is missing, MP3 conversion fails at runtime with a clear message.
If `ffmpeg.exe` is missing, M4A/AAC/FLAC output fails with a clear message. WAV output
needs nothing extra.

> **Note:** `SNDEV/` (including `scripts/deploy.ps1`, `fetch-lame.ps1`) is git-ignored and
> is agent-internal tooling, not part of the shipped source. The commands above use the
> SDK directly so the project builds from a clean clone.

---

## Running

### GUI

```powershell
# From a build output directory:
QuickAudioConverter.exe
# or simply double-click it
```

On launch it opens to the **Convert** tab:

- **Toolbar:** `Add File(s)`, `Remove`, `Play` (stub), `Convert`, `Options`, `Tags` (stub).
- **Drop zone:** drag individual files or entire folders.
- **Output panel:** *Save to folder* + *Browse*, *Output Format*, *Open Output Folder*,
  and checkboxes for *Save to source file folder* and *Copy source folder structure*.
- **Status bar:** shows the active encoding profile, e.g.
  `Convert to .mp3 | CBR(224kbps)|High Quality|Mode(Stereo) | -> source folder | 3 file(s)`.

The **Options** tab is where you set the default encoding profile (used by the context-menu
path) and toggle shell integration.

### Command-line interface

| Command | Description |
|---------|-------------|
| *(no args)* | Launch the GUI. |
| `--headless --input "<path>" [--format mp3] [--out <dir>] [--mono\|--stereo] [--bitrate 224]` | Convert one file (or pass `--input` multiple times) without a window. Output settings default to the saved profile unless overridden. |
| `--register` | Register the Explorer context-menu verbs (per-user, `HKCU`). |
| `--unregister` | Remove the Explorer context-menu verbs. |
| `--comserver` | Host the `IExplorerCommand` COM local server. Invoked by File Explorer when the modern context-menu entry is clicked; not intended for manual use. |
| `--install [--all-users]` | Install the modern `IExplorerCommand` integration. Per-user registers a `HKCU` local-server verb; with `--all-users` (elevated) it writes `HKLM` and installs the sparse package for the **Windows 11 top-level** menu. |
| `--uninstall [--all-users]` | Remove the modern integration (and the sparse package when `--all-users`). |
| `--selftest` | Run a built-in self-test (WAV, MP3 via LAME, M4A decode→WAV/MP3, and M4A output via FFmpeg if present). Prints `SELFTEST PASS/FAIL`. |
| `--gentestwav <path> [seconds]` | Generate a 440 Hz test WAV for manual testing. |

Example:

```powershell
QuickAudioConverter.exe --headless --input "C:\Music\clip.m4a" --format mp3 --out "D:\Out"
```

---

## Windows File Explorer context-menu integration

The context menu lets you convert files without opening the app. It is implemented as
**static per-user registry verbs** under:

```
HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QacConvertTo<fmt>
    (default)         = "Convert to <FMT>"
    command\(default) = ""<exe>" --headless --input "%1""
```

where `<ext>` is each decodable extension (`.m4a`, `.mp3`, `.wav`, …) and `<fmt>` is your
currently selected default output format (e.g. `mp3`).

### How to enable

**Option A — from the GUI (recommended):**
1. Open **Quick Audio Converter → Options**.
2. Set your desired *Default output format*, bitrate, quality, mode, and *Default save folder*.
   These become the profile the context menu uses.
3. Tick **Enable Explorer context-menu integration (Convert to &lt;format&gt;)**.
   The verbs are written to `HKCU` immediately — **no administrator rights required**.

**Option B — from a command prompt:**

```powershell
# Run from the published/exe directory, or use the full path:
QuickAudioConverter.exe --register
QuickAudioConverter.exe --unregister   # to remove it
```

### How to use it

1. In File Explorer, select one or more supported audio files.
2. Right-click the selection.
3. Click **Convert to &lt;FMT&gt;** (e.g. *Convert to MP3*).
   - Windows launches the `--headless` converter **once per selected file**; each run is
     independent and runs hidden so it never steals focus from your active window.
4. The conversion reads your **last-used** profile from
   `%LOCALAPPDATA%\QuickAudioConverter\settings.json`:
   - **Output format / bitrate / channels / quality:** from that saved profile.
   - **Output location:** if *Save to source file folder* is on, files are written next to
     the originals; otherwise they go to the configured *Default save folder* (with
     *Copy source folder structure* preserving relative paths when enabled).
5. When finished (or if it fails), a **Windows toast notification** appears.

> **Windows 11 note:** The legacy per-user verb above lives in the **classic** right-click menu;
> on Windows 11 you may need **Show more options** to reach it. For a **first-class top-level**
> Windows 11 menu entry, use the installer below (sparse-package + `IExplorerCommand`).

### Windows 11 top-level menu (sparse package + IExplorerCommand)

Quick Audio Converter also implements the native **`IExplorerCommand`** COM interface (in C#, hosted
out-of-process as a COM local server — `QuickAudioConverter.exe --comserver`). To promote that command
to the **top-level** Windows 11 context menu (i.e. *not* behind "Show more options"), the app is given
a **package identity** via a **sparse package** (an MSIX with no payload that simply references the
already-installed desktop EXE).

How it works:

1. **`IExplorerCommand` COM server** (`src/QuickAudioConverter/Shell/ExplorerCommandImpl.cs`) —
   Explorer launches `QuickAudioConverter.exe --comserver`; the EXE registers a class object, pumps a
   message loop, and on `Invoke` enumerates the selected `IShellItemArray` and spawns a detached
   `--headless` conversion (same engine as the legacy path, never steals focus).
2. **Sparse package** (`packaging/sparse/AppxManifest.xml`) —
   declares `windows.comServer` (the CLSID) and the `windows.fileExplorerContextMenu` app-extension,
   granting the package identity Windows 11 requires for the top-level menu. The manifest uses a
   *relative* `Executable`, so the installer passes `-ExternalLocation <install dir>` to `Add-AppxPackage`.
3. **Admin install** — the installer writes the `HKLM` `CLSID` + per-extension `ExplorerCommandHandler`
   shell keys (per SRS §2.2, which requires administrative privileges to modify the system registry)
   and runs `Add-AppxPackage` on the sparse `.msix`. The all-users registration is then run under the
   package identity (via `Invoke-CommandInDesktopPackage --register-modern-packaged`) so the keys
   virtualize into the package.

#### Install (all users, administrator)

```powershell
# Option A — from the GUI (recommended): Options → "Install Windows 11 top-level menu (all users…)".
# This re-launches the app elevated (UAC) and runs the installer.

# Option B — from an elevated command prompt:
QuickAudioConverter.exe --install --all-users

# Option C — full deployment script (copies files, builds the sparse package via the Windows SDK,
# then installs). Must be run from an elevated PowerShell prompt:
SNDEV/scripts/deploy.ps1
```

Uninstall: `QuickAudioConverter.exe --uninstall --all-users` (elevated) or the GUI button.

> **Build the sparse package:** `packaging/sparse/build-sparse.ps1` packs and self-signs
> `QuickAudioConverter.Sparse.msix` using `makeappx`/`signtool` from the Windows SDK. It gracefully
> reports when the SDK is absent so the package can be produced on a machine that has it. The binary
> itself is git-ignored and not committed.

> **Verification caveat:** the top-level promotion and a signed `.msix` can only be validated on
> **Windows 11** with the Windows SDK present. On Windows 10 (and on Windows 11 *without* the sparse
> package) the `IExplorerCommand` still works — it simply appears under "Show more options".

### Where settings live

`%LOCALAPPDATA%\QuickAudioConverter\settings.json` — your last-used encoding profile,
output routing, and shell-integration state. Corrupt or missing files fall back to defaults
(`mp3`, 224 kbps, stereo, save-to-source).

---

## Testing

Unit/integration tests use **xUnit**:

```powershell
dotnet test QuickAudioConverter.sln
```

- Engine integration tests exercise WAV and MP3 encoding and M4A decoding. They require
  `libmp3lame.dll` to be staged (MP3 leg) and use a committed `.m4a` fixture under
  `tests/fixtures/`.
- A built-in `--selftest` mode (see above) validates the full pipeline end-to-end from the
  published EXE and reports `SELFTEST PASS/FAIL`.

---

## Licensing

- Application source: **MIT** (see SPDX headers in each source file).
- Bundled **libmp3lame** (LAME) MP3 encoder: **LGPL-2.1**. Dynamically loaded via P/Invoke,
  user-replaceable, with attribution in `licenses/THIRD_PARTY.md` and `licenses/LGPL-2.1.txt`.
- **FFmpeg** (for M4A/AAC/FLAC): license terms depend on the build you bundle; select a
  build whose terms are acceptable for your distribution.
- Windows Media Foundation (decode/demux) is part of Windows and is not redistributed.

---

## Project layout

```
QuickAudioConverter.sln
global.json                      # pins .NET 9 SDK
src/QuickAudioConverter/
  Program.cs                     # entry point + CLI (GUI / --headless / --register / --selftest)
  MainForm.cs                    # ribbon UI (Home/Convert/Effects/Options)
  Ui/                           # Ribbon, DropZone, OutputPanel, OptionsPanel
  Engine/                       # WMF decode, WAV/MP3/LAME, FFmpeg, settings, routing
  Shell/                        # ShellIntegration (HKCU verbs), ToastNotifier (WinRT)
  licenses/                     # LGPL attribution (shipped with the EXE)
tests/QuickAudioConverter.Tests/  # xUnit tests
```
