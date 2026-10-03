<p align="center">
  <img src="./docs/iconfull.png" alt="JRTI Icon" width="682">
</p>

<p align="center">
    <a href="https://ko-fi.com/relmymathieu"><img src="https://img.shields.io/badge/ko--fi-support-ff5f5f.svg?style=flat&logo=ko-fi&logoColor=white" alt="Ko-fi"/></a>
    <a href="https://github.com/RELMYMathieu/JustReadTheInstructions/releases/latest"><img src="https://img.shields.io/github/downloads/RELMYMathieu/JustReadTheInstructions/total.svg?style=flat&logo=github&logoColor=white" alt="Total downloads" /></a>
    <a href="https://github.com/RELMYMathieu/JustReadTheInstructions/releases/latest"><img src="https://img.shields.io/github/release/RELMYMathieu/JustReadTheInstructions.svg?style=flat&logo=github&logoColor=white" alt="Latest release" /></a>
    <a href="https://spacedock.info/mod/4212/Just%20Read%20The%20Instructions"><img src="https://img.shields.io/badge/spacedock-download-4f86c6.svg?style=flat&logoColor=white" alt="SpaceDock"/></a>
    <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/license-MIT-97ca00.svg?style=flat&logoColor=white" alt="MIT License" /></a>
</p>

<p align="center">
  A web-viewable Hullcam VDS camera feed mod for Kerbal Space Program.
</p>

<p align="center">
  Spiritual successor to <strong><a href="https://github.com/jrodrigv/OfCourseIStillLoveYou">OfCourseIStillLoveYou</a></strong>.
</p>

---

> [!NOTE]
> **JRTI is in a stable state, but you may encounter bugs due to the experimental nature of this mod.**
>
> The mod is stable, but expect a few quirks. You may still encounter bugs or performance issues, but nothing that should hold you back from using the mod. If you do, please report them in the Issues tab with your log file attached. Prefixing the title with `Bug:` helps with triage.

---

>[!WARNING]
> **Mac OS**: Camera streaming works but uses a synchronous GPU readback fallback, as Metal does not support Unity's async readback API. This blocks the main thread briefly each captured frame and may cause minor stuttering at higher stream framerates.

## Overview

**Just Read The Instructions** (**JRTI**) is a mod for **Kerbal Space Program** that lets you view your **Hullcam VDS** camera feeds in a web browser, in the spirit of **OCISLY**.

## Project Status

> [!NOTE]
> **JRTI is now considered stable and feature-complete.** It works great, does what it set out to do, and I'm happy to call this a finished release rather than a perpetual work in progress.

After a long and genuinely enjoyable time building it, I'm stepping back from **frequent** major updates. The mod is in a good place, and the ideas I have left are either too big or fall outside its core purpose, and I'd rather leave it polished and whole than keep piling on for the sake of it.

It is **not** abandoned, though. I'll still come back for:

* **Major feature updates** which may still come out but at a much slower pace and depend a lot on how busy life gets
* **Great community suggestions** that fit the vision and that most players would benefit from
* **Quality-of-life tweaks** I get reminded of along the way
* **Security fixes** and important patches (we never know what could happen!)

Realistically, expect minor patches and QoL roughly every few weeks *if* suggestions keep rolling in, and otherwise I'll drop by now and then as the mood strikes, just less often when there's no feedback to ponder on. University has started as of this README commit, so my time will be a more limited towards working on the mod.

For anything improvement suggestions, pull requests are always welcome and I'm very open-minded, as long as a change fits the mod's vision, I'd love to see it.
I will **gladly** read any issues or PRs that may come up at any time.

Thank you to everyone who has downloaded the mod, gave feedback, or contributed... This has been a wonderful thing to build.
o7 and have fun :D

## Requirements

* **Kerbal Space Program** `1.12.x`
* **[HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)** (latest version)

## Features

* View **Hullcam VDS** camera feeds in a web browser, on the KSP computer or any device on your network
* Externalize in-game camera views outside the main game window
* Build multi-camera layouts (grid, spotlight, edge to edge split screens) by dragging cameras onto the screen, and save them in the game so every device and OBS source shows the same layout
* Record camera feeds as MP4 inside the game with the graphics card's encoder, then play or download the recordings from the web UI
* Grab the raw MJPEG feed URL for OBS or other external tools
* Adjust brightness, contrast, gamma, and FOV per camera from the web viewer - applied server-side so all viewers on the local network see the same image
* Hear the game from each camera's position, with a mic per camera (game mix, external or onboard), in the browser, on the clean feed for OBS and in recordings - see [Camera Sound](#camera-sound)
* Name cameras and assign a stable numeric ID from the part's right-click menu in the editor - kept in the craft file, so the stream URL stays the same across relaunches

## Controls & Settings

| Shortcut (in flight) | Opens |
| --- | --- |
| `Ctrl` + `Alt` + `F7` | JRTI's main window (camera list, open, stream) |
| `Ctrl` + `Alt` + `F8` or `F9` | Settings & integrations |
| `Ctrl` + `Alt` + `F6` | Performance overlay |

The web UI lives at `http://localhost:8080/` (or the port set in the settings). Settings are saved in `GameData/JustReadTheInstructions/PluginData/settings.cfg`, which mod updates never overwrite.

Visual mods are applied to camera feeds as in the main view and can be turned off one by one under integrations: Deferred, TUFX (without motion blur, which smears feeds that render at their own pace), Scatterer, EVE, Parallax, Firefly, and the stock re-entry and Mach effects when Firefly is not installed.

## Camera Layouts

The **Layout** button in the web UI opens `layout.html`: several cameras in one window, sized to fill it. Open **Cameras** to drag cameras onto the screen, drag a tile onto another to swap them, double-click a tile (or press `1` to `9`) to spotlight it, and use **Fill** for edge to edge split screens.

The menu next to the layout's name saves it **in the game** under a name. `http://localhost:8080/layout.html?layout=Launch` then shows that layout on any device and in OBS, and follows every change made to it from another screen, so a phone can rearrange what an OBS browser source shows while you fly.

For live shows, point one OBS browser source at the **clean feed**, `http://localhost:8080/layout.html?program`. It never shows controls and displays whichever saved layout is **on air**. On the layout page, press **Take on air** or `Shift` + `1` to `9` (the first nine saved layouts, in the order the menu lists them) and the clean feed switches at once, with cameras gliding to their new places. A Stream Deck or any tool that can open a URL can switch too: `http://localhost:8080/program/take/Launch`, or `/program/clear` for a black feed.

## Camera Sound

Every camera has its own sound: the game's sounds as heard from where the camera is, mixed inside the game without changing what you hear. Turn it on with **Listen** (`M`) in the camera viewer or on the layout page; it plays in that browser tab only. The layout page plays the spotlight tile, or the tile picked with its speaker button, and saves that choice with the layout.

For OBS, add `&audio=1` to a viewer or clean feed URL (`http://localhost:8080/layout.html?program&audio=1`) and tick "Control audio via OBS" on the browser source. The clean feed plays the sound of the layout on air and crossfades on Take. In-game recordings always include the camera's sound.

Each camera card on the main page has a **Mic** picker in its bottom border, so you can set every camera's mic before recording without opening a viewer. **Adjust** in the viewer sets the **Mic**, **Gain**, **Auto gain** and **Mastering** under **Sound**, for every viewer and recording:

| Mic | What it hears |
| --- | --- |
| Game mix (default) | The game's sound from where the camera is, clean: no air delay, echo or hull muffling |
| External | Sound crossing the air: it arrives late from far away, changes pitch as things fly past, gets duller, hollower (ground echo) and grittier with distance and thinner air, grittiest behind a vessel and thinner ahead of it, and stops in vacuum except for the camera's own vessel through the hull |
| Onboard | Its own vessel through the structure and the cabin, other vessels muffled by the hull |

Big engines carry further on every mic: an engine's sound reaches further the more thrust it is making, so a pad camera still hears a heavy launch kilometres away while small engines fade as before. A part with several engines, like a Raptor cluster, counts all of them. With Rocket Sound Enhancement, whose engine sounds are already louder for more thrust, the extra reach is smaller so the total stays physical.

**Auto gain** (off by default) keeps a camera near a steady level: it slowly lifts quiet sound, up to +24 dB, and brings loud sound down, which leaves room for booms and ignitions to stand out; it lifts wind and background noise too. **Mastering** (off by default) compresses the camera like the game does with Rocket Sound Enhancement (its Limiter Amount from RSE's settings): denser and louder, with less difference between quiet and loud.

Each camera remembers its mic, gain, auto gain and mastering, in `PluginData/camera-sound.cfg`: they come back when the camera opens again, after a revert, a quickload or relaunching the same craft. A second copy of a craft flying at the same time starts on the defaults.

Works with **Rocket Sound Enhancement**: its engine layers play on cameras without the player camera's Doppler, and when **Harmony** is installed (RSE requires it) its ignition, flameout and decoupler sounds are heard too. Each RSE sound keeps the air treatment its config gives it (engines get echo and grit, while RCS, jetpacks and collisions only get duller with distance), and each vessel's re-entry roar follows its own heating, not the vessel you fly.

Sonic booms use RSE's sounds. A boom reaches a camera each time a vessel's speed toward that camera crosses the speed of sound, after the time the sound takes to get there: a flyby booms once, and a booster coming back (boostback, entry burn, descent, landing burn) can boom several times. Each is a double boom-boom from the nose and tail shocks, further apart the longer the vessel and the further the camera. Ahead of a supersonic vessel its sound is a faint rumble, and it comes back gradually after the cone passes, as in RSE. Cameras riding on that vessel hear a boom instead each time it crosses Mach 1 in air, going up or slowing down. Booms follow RSE's own scaling (heavier vessels boom louder and deeper), hit hardest on External and come through the hull clearly on Onboard. RSE Default's far boom, the rumble meant for 0.5 to 5 km, plays on External and Onboard (RSE itself only ever plays the near one). A few sounds built into KSP, such as explosions, are kept compressed by Unity and cannot be read yet; they are listed in `KSP.log` and counted on the Diagnostics page.

## Camera Naming & IDs

Right-click a camera part in the VAB / SPH to open the **JRTI** group, where you can **Set Name** and **Set ID**. Both are saved in the craft file - no external config needed.

The ID is what appears in the camera's stream URL (`http://localhost:<port>/camera/<id>/stream`), so giving a camera a fixed ID lets you point OBS (or any tool) at the same URL every time you fly that craft.

A few details worth knowing about how IDs resolve at runtime:

* **Leave the ID blank (or 0) to auto-assign** the lowest free number, starting at `1`.
* **IDs are unique among cameras loaded at the same time.** They key the live stream endpoints, so two active cameras can never share one number.
* **Collisions are resolved automatically.** If two cameras would claim the same ID simultaneously - for example two separate craft both set to ID `1` while within physics range - the first one to load keeps it and the others are bumped to the next free number. Their feeds still work; only the numeric ID shifts.
* **IDs are scoped to a flight session.** They are freed when a craft is recovered or unloaded, and reset when you re-enter the flight scene, so relaunching a single craft reliably restores its chosen IDs.

## Screenshot

![JRTI Screenshot](./docs/screenshot-1.png)

## Installation

**Via CKAN:** Search for `JustReadTheInstructions` in [CKAN](https://github.com/KSP-CKAN/CKAN) and install it from there.

**Via SpaceDock:** Download from the [SpaceDock page](https://spacedock.info/mod/4212/Just%20Read%20The%20Instructions) and follow the manual install steps below.

**Via GitHub:** Download the latest release ZIP from the [Releases page](https://github.com/RELMYMathieu/JustReadTheInstructions/releases), extract it, and move the `JustReadTheInstructions` folder into your KSP `GameData` folder.

Your final install should look like this:

```text
Kerbal Space Program/
└── GameData/
    └── JustReadTheInstructions/
```

## Customization

The **Loss of Signal** image shown in the web UI when a camera feed is unavailable can be customized with any PNG of your choice (recommended: `1920×1080`).

Add this file:

```text
GameData/JustReadTheInstructions/Web/images/customlos.png
```

If `customlos.png` is not present, JRTI automatically falls back to the built-in:

```text
GameData/JustReadTheInstructions/Web/images/los.png
```

> [!CAUTION]
> Editing files in the `Web` folder is not supported and may break the mod's functionality.

## Recording & Codecs

Recordings are started from the web UI and saved on the machine running KSP, in `GameData/JustReadTheInstructions/Web/recordings/`. The **Recordings** panel in the web UI lists them, so any device on your network can play or download them. Live feeds and the stream URLs you give OBS are always MJPEG: the codecs below only apply to recordings.

> [!WARNING]
> Keep **H.264** unless you know your tools handle something else. H.264 opens in every editor, player, phone and OBS setup. Other codecs are for people who have checked that their whole workflow supports them.

### Picking a codec

Open **Settings** in the web UI and choose a **Video codec**. Only codecs that work on the machine running KSP are listed, and the choice is remembered per browser. JRTI's in-game settings window shows which encoders were found.

### In-game recorder (default)

Every recording is an MP4 at the camera's render resolution, at a constant frame rate (Max FPS), with a keyframe every 2 seconds and a bitrate of 0.2 bits per pixel per frame (about 12 Mbps at 1080p 30 FPS). It also holds the camera's sound, as heard from where the camera is (AAC, 48 kHz stereo). JRTI picks the first encoder that works, trying graphics card encoders before CPU encoders:

| Codec | Windows | Linux | macOS |
| --- | --- | --- | --- |
| **H.264** (default) | Media Foundation: graphics card, or Windows' own software encoder | `h264_nvenc` → `h264_vaapi` → `h264_qsv` → `h264_vulkan` → `libx264` → `libopenh264` | `h264_videotoolbox` → `libx264` → `libopenh264` |
| **AV1** | With ffmpeg: `av1_nvenc` → `av1_amf` → `av1_qsv` → `av1_vulkan` → `libsvtav1` | `av1_nvenc` → `av1_vaapi` → `av1_qsv` → `av1_vulkan` → `libsvtav1` | `libsvtav1` (Apple chips have no AV1 encoder) |

Linux and macOS record through [ffmpeg](https://ffmpeg.org/), which must be installed (for example `sudo apt install ffmpeg` or `brew install ffmpeg`), or placed in `GameData/JustReadTheInstructions/PluginData/ffmpeg/`, which JRTI checks first. On Windows, H.264 needs nothing extra; AV1 needs `ffmpeg.exe`, for example from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases), on your `PATH` or in the same `PluginData/ffmpeg/` folder. JRTI then also uses it for H.264 if Media Foundation is missing (Windows N editions). To see which encoders your ffmpeg build has:

```bash
ffmpeg -hide_banner -encoders | grep -E "264|av1"
```

| Encoder | Runs on |
| --- | --- |
| `*_nvenc` | NVIDIA graphics cards. AV1 needs an RTX 40 series or newer |
| `*_amf` | AMD graphics cards on Windows. AV1 needs an RX 7000 series or newer |
| `*_vaapi` | AMD and Intel graphics cards through Mesa or Intel's media driver. AV1 needs an AMD RX 7000 series, Intel Arc, Intel Core Ultra or newer |
| `*_qsv` | Intel graphics. AV1 needs Intel Arc, Intel Core Ultra or newer |
| `h264_videotoolbox` | Macs |
| `*_vulkan` | Graphics cards through Vulkan, when the encoders above are unavailable |
| `libx264`, `libopenh264`, `libsvtav1` | The CPU. Slower, and can drop frames at high resolutions while KSP is running |

**Fedora:** its stock graphics drivers leave out H.264 hardware encoding, so H.264 falls back to the CPU unless you install RPM Fusion's drivers (`mesa-va-drivers-freeworld`). AV1 still uses the graphics card.

### KSP through Proton (Linux)

Windows' encoder does not exist under Proton, so JRTI records the way native Linux does, through a Linux build of ffmpeg. Proton runs games inside Steam's container, which cannot run the ffmpeg from your package manager, so use a static build:

1. Download `ffmpeg-...-linux64-gpl-....tar.xz` from [BtbN's FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds/releases).
2. Extract it into `GameData/JustReadTheInstructions/PluginData/ffmpeg/` (the whole folder or just the `ffmpeg` file).
3. Start KSP. JRTI's in-game settings window lists the encoders it found, for example `av1_vaapi, h264_vaapi (ffmpeg)`.

Encoders are tried in the Linux order above, so AMD and Intel cards use VA-API and NVIDIA cards use NVENC.

**About AV1:** JRTI gives AV1 the same bitrate as H.264, so files are about the same size but keep more detail. Recent VLC, mpv and web browsers play AV1, and Windows needs Microsoft's *AV1 Video Extension* to preview it. Check that your editor imports AV1 before recording anything important with it.

If the game cannot record (no encoder found), Record stays off and JRTI's settings window in KSP (Ctrl+Alt+F8) says why. The browser recorder from earlier versions is gone.

## Known Issues

A few known issues are tracked but not yet fixed:

* **Firefox recording output is unreliable.** The recorded file may be corrupt or unplayable. Use Chrome or Edge for recording until this is resolved.
* **macOS is not properly supported.** A GPU async API used internally by this Unity version is unavailable on macOS, a legacy quirk inherited from KSP's Unity build. A fix is being investigated.
* **Performance degradation with Parallax.** Parallax integration is disabled by default. Enabling it in the Settings menu may cause significant frame-rate drops.

If you hit something not listed here, please open an issue with your log file attached. Prefixing the title with `Bug:` helps with triage.

## For Developers

### Prerequisites

* Visual Studio 2022 (Windows), or the .NET SDK with your editor of choice
* Kerbal Space Program `1.12.x`
* HullcamVDS-Continued installed in that KSP install (see below)

#### Getting HullcamVDS

HullcamVDS is declared as a dependency in the project, and the build compiles against the copy in your KSP `GameData`.

* **Linux / macOS:** if the [CKAN](https://github.com/KSP-CKAN/CKAN) command-line tool (`ckan`) is on your `PATH`, the build installs HullcamVDS into your KSP install for you.
* **Windows:** automatic install is turned off, because KSPBuildTools' CKAN step doesn't work under `cmd.exe` (it silently does nothing). Install HullcamVDS yourself first, through the CKAN app or manually.

If HullcamVDS is already in your `GameData`, it is used as-is on every platform.

### Setup

Create a `JustReadTheInstructions.csproj.user` file next to the `.csproj` and point it at your KSP install:

**Windows:**

```xml
<Project>
  <PropertyGroup>
    <KSPBT_GameRoot>C:\Your\KSP\Install</KSPBT_GameRoot>
  </PropertyGroup>
</Project>
```

**Linux / macOS (native KSP):**

```xml
<Project>
  <PropertyGroup>
    <KSPBT_GameRoot>/home/you/KSP</KSPBT_GameRoot>
  </PropertyGroup>
</Project>
```

> `*.csproj.user` is gitignored and will never be committed.

> [!NOTE]
> **Running KSP through Proton on Linux?** KSPBuildTools expects a `KSP_Data` folder but the Windows/Proton build ships `KSP_x64_Data` instead. Create a symlink to fix this:
> ```bash
> ln -s "/path/to/Kerbal Space Program/KSP_x64_Data" "/path/to/Kerbal Space Program/KSP_Data"
> ```
> Replace `/path/to/Kerbal Space Program` with your actual Steam install path, e.g. `/home/you/.local/share/Steam/steamapps/common/Kerbal Space Program`.

### Building

```bash
dotnet build -c Release
```

The compiled DLL is written directly to `GameData/JustReadTheInstructions/Plugins/` (`Debug` builds also copy the `.pdb`).

The mod version lives in the `<Version>` property of `Source/JustReadTheInstructions.csproj`; the `create-release` workflow bumps it automatically. Building also regenerates `GameData/JustReadTheInstructions/JustReadTheInstructions.version` (the KSP-AVC version file) from it, so don't edit that file by hand.

To install, symlink or copy `GameData/JustReadTheInstructions/` into your KSP `GameData/`.

## License

This project is licensed under the MIT License.
