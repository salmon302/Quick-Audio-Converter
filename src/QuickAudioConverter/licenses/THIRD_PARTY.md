# Third-Party Licenses

This directory accompanies the published `QuickAudioConverter` binary and satisfies the
attribution/notice requirements of the LGPL for the bundled native MP3 encoder.

## libmp3lame (LAME) — MP3 encoder
- **Purpose:** MP3 (MPEG-1/2 Layer III) encoding. The Native AOT application loads
  `libmp3lame.dll` at runtime via P/Invoke (dynamic import / `LoadLibrary`).
- **License:** GNU Lesser General Public License (LGPL) v2.1 or later. Full text:
  `LGPL-2.1.txt` in this directory.
- **Dynamic-linking compliance:** Because the application interfaces with the library
  dynamically (not by static linkage into the executable), the application binary is a
  separate work that *uses* the library. The library remains **user-replaceable**: end users
  may swap `libmp3lame.dll` in the application directory with their own compatible build of
  LAME without modifying the application.
- **Bundled version:** 3.99 release 5 (FileVersion `3.99 release 5`, vendor `lame.sf.net`).
- **Source:** https://sourceforge.net/projects/lame/ (official LAME source repository).
  Exact release: https://sourceforge.net/projects/lame/files/lame/3.99/
- **Provenance / staging:** Staged locally via `SNDEV/scripts/fetch-lame.ps1`. The binary is
  intentionally **git-ignored** and is NOT redistributed in the source repository; it is
  resolved at build/packaging time per the distributor's own license obligations.

## Other components
- Windows Media Foundation (used for decode/demux) is part of the Windows OS and is not
  redistributed by this application.
