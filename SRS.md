# Software Requirements Specification (SRS)
## Project: Quick Audio Converter

### 1. Introduction
**1.1 Purpose**
This document specifies the software requirements for the Quick Audio Converter desktop application. It defines the functional and non-functional requirements for audio file conversion, user interface interactions, and operating system shell integrations.

**1.2 Scope**
Quick Audio Converter is a Windows-based utility designed to transcode audio files between various formats (e.g., .m4a to .mp3). The system provides a Graphical User Interface (GUI) for detailed parameter configuration and batch processing, alongside Windows File Explorer context menu integration for immediate, headless execution.

### 2. Overall Description
**2.1 User Interfaces**
The primary GUI features a ribbon-style navigation paradigm with distinct tabs (Home, Convert, Effects, Options). The main workspace must include:
*   A primary toolbar with actionable icons: Add File(s), Remove, Play, Convert, Options, Tags.
*   A central drag-and-drop ingestion zone for individual files or entire directories.
*   An output routing and configuration section containing fields for "Save to folder", "Output Format", "Browse", and "Open Output Folder".
*   Boolean toggle checkboxes for "Save to source file folder" and "Copy source folder structure".
*   A dynamic status bar displaying the active encoding parameters (e.g., `Convert to .mp3 | CBR(224kbps)|High Quality|Mode(Mono)`).

**2.2 Operating Environment**
The software is designed for Microsoft Windows desktop environments. It requires administrative privileges during installation to modify the system registry for shell extension integration. The admin ("all users") installer path writes machine-wide `HKLM` COM/context-menu registration and installs the sparse package (see REQ-SHELL-01) that grants the Windows 11 top-level menu; it self-elevates via UAC (`runas`) when launched without administrator rights.

### 3. Functional Requirements

**3.1 Standard GUI Conversion (REQ-GUI-01)**
*   **Description:** Users can manually import files, configure global output parameters, and initiate batch conversions from the main application window.
*   **Inputs:** Audio files loaded via the drag-and-drop zone or the "Add File(s)" file picker.
*   **Processing:** The system applies the selected output format and encoding profile parameters to all files in the active queue.
*   **Outputs:** Encoded audio files written to the explicitly defined output directory (e.g., `G:\`), overriding source locations unless specified otherwise.

**3.2 Context Menu Integration (REQ-SHELL-01)**
*   **Description:** The application shall process audio format conversions directly through the Windows File Explorer context menu without launching the primary GUI workspace.
*   **Trigger:** The user highlights one or multiple source files, right-clicks the selection, and clicks the application's context menu extension (e.g., "Convert to mp3").
*   **Processing:** 
    *   The system captures the absolute paths of all selected files.
    *   The conversion engine initiates as a headless background process.
    *   The engine retrieves the last-used encoding parameters from the application's persistent configuration state.
    *   Output path resolution evaluates the saved global application settings to determine if files should write to a static target directory or the original source directory.
*   **Implementation note (Windows 11 top-level menu):** In addition to the legacy per-user static verb, the application implements the native **`IExplorerCommand`** COM interface (C#, hosted out-of-process as a COM local server — `QuickAudioConverter.exe --comserver`). To surface the command in the **first-class Windows 11 top-level** context menu (rather than behind "Show more options"), the EXE is granted **package identity** via a **sparse package** (an MSIX with no payload referencing the installed desktop EXE). The sparse package declares `windows.comServer` (the CLSID) and the `windows.fileExplorerContextMenu` app-extension. All-users installation (per §2.2) writes the `HKLM` `CLSID` + per-extension `ExplorerCommandHandler` shell keys and calls `Add-AppxPackage` on the sparse `.msix`; the per-extension registration is then performed under package identity so File Explorer promotes the entry to the top-level menu.

### 4. Non-Functional Requirements

**4.1 Performance Requirements**
*   Context menu actions must invoke the background conversion process within 1,000 milliseconds of the user click to prevent perceived system lag.
*   The application must support the concurrent ingestion and queueing of up to 5,000 files without exceeding standard memory allocation limits or crashing.

**4.2 Usability Requirements**
*   The interface must support standard OS drag-and-drop file mechanics.
*   Headless shell conversions must not steal window focus from the user's active applications.
*   The system must utilize OS-native toast notifications to inform the user when a background conversion task completes successfully or encounters a failure state.
To keep the application footprint minimal, prioritize native operating system APIs over third-party dependencies and monolithic runtimes.

Audio Engine

Recommended Architecture (adopted). Decode/demux is always performed by the **Windows Media Foundation Source Reader** (`IMFSourceReader`): 0 MB added footprint, built into every Windows machine, handles M4A/AAC (and WAV/MP3) demuxing and decoding natively. Encoding is format-specific and is intentionally performed *outside* WMF:

- **WAV (PCM passthrough):** a 44-byte RIFF/WAVE header is written directly in C# (`BinaryWriter`/`FileStream`). This eliminates all Sink Writer / COM / MFT overhead for the PCM path — the file is just a header followed by the raw PCM buffer.
- **MP3:** the raw 16-bit PCM buffer from the Source Reader is encoded by **libmp3lame (LAME)** via P/Invoke (`lame_init`, `lame_set_in_samplerate`, `lame_set_num_channels`, `lame_set_brate`, `lame_set_mode`, `lame_set_quality`, `lame_init_params`, `lame_encode_buffer_interleaved`, `lame_encode_flush`, `lame_close`). This deliberately bypasses the Media Foundation MP3 encoder MFT, which is **absent on Windows N / un-provisioned installs**, and ships a single ~360 KB LGPL native DLL (`libmp3lame.dll`) next to the executable.
- **M4A / AAC / FLAC:** encoded by a bundled or system **FFmpeg** invoked headlessly (`-c:a aac` for M4A/AAC, `-c:a flac` for FLAC). This bypasses the Media Foundation AAC/FLAC encoder MFTs, which are likewise absent on Windows N / un-provisioned installs. FFmpeg is used *only* for these container formats; decoding and WAV/MP3 writing remain native (WMF + LAME). See the NFR §4.1 exception note below.

Custom-Trimmed FFmpeg Static Binary: ~15–25 MB. Optional alternative if broader container/codec coverage is required; compile a bespoke minimal FFmpeg executable with all demuxers/decoders/encoders disabled except aac, m4a, and libmp3lame and execute it headlessly.

> **NFR §4.1 exception.** §4.1 ("prioritize native operating system APIs over third-party dependencies") is intentionally relaxed for (a) the MP3 encoder via **libmp3lame** (LAME), because Windows does not ship a usable MP3 encoder MFT on N / un-provisioned editions, and (b) the M4A / AAC / FLAC encoders via **FFmpeg**, because Windows likewise ships no AAC/FLAC encoder MFT on those builds. FFmpeg is resolved from a side-by-side `ffmpeg.exe` next to the app (or system PATH) and invoked headlessly; it is staged automatically by `SNDEV/scripts/deploy.ps1` when present. All *decoding* and *WAV/MP3 writing* remain 100% native OS APIs (Media Foundation + LAME). libmp3lame provenance is staged via `SNDEV/scripts/fetch-lame.ps1`; ffmpeg is git-ignored and not redistributed in the source repository.
>
> **LGPL compliance (libmp3lame).** The application links `libmp3lame.dll` *dynamically* via P/Invoke, so the Native AOT executable is a separate work that merely uses the library. The DLL is shipped side-by-side with the EXE and is user-replaceable. Attribution is provided in the published `licenses/` directory (`THIRD_PARTY.md` + `LGPL-2.1.txt`), and LAME source is referenced at <https://sourceforge.net/projects/lame/>. The binary itself is git-ignored and not redistributed in the source repository.

> **FFmpeg usage (M4A/AAC/FLAC).** `ffmpeg.exe` is invoked as a *separate process* (not linked), so the Native AOT executable is a separate work; the binary is shipped side-by-side, is user-replaceable, and is git-ignored. When bundling, select an FFmpeg build whose license terms (GPL/LGPL depending on configuration) are acceptable for your distribution.

Application Framework & GUI

C++ with Win32 / WTL (Windows Template Library): Generates an executable under 3–5 MB with zero runtime dependencies, instant startup time, and direct access to native Windows UI controls, drag-and-drop messages (WM_DROPFILES), and the system tray.

C# (.NET 8/9 with Native AOT & WinForms/WPF): Enables faster UI development while stripping unused framework assemblies during compilation. Generates a standalone binary in the 15–25 MB range without requiring an external .NET runtime pre-installed.

Rust with windows-rs + Slint: Produces an isolated, memory-safe executable under 10 MB with lightweight, GPU-accelerated declarative UI components.

Shell Integration & OS Utilities

Registry Verbs (Static Integration): 0 KB dependencies. Register commands under HKCU\Software\Classes\SystemFileAssociations\.m4a\Shell\ConvertToMP3\Command. Pass arguments directly into the binary (e.g., app.exe --headless --input "%1").

Sparse Package / COM Sparse MSIX (Windows 11 Context Menu): Requires no third-party libraries; implement the native IExplorerCommand interface via C++ or C# to insert the action into Windows 11's top-level context menu without hitting "Show more options".

Native Windows Notification API: Use the OS-provided Windows.UI.Notifications WinRT API to fire completion toast notifications without bundling external notification daemons.