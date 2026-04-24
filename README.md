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
> **JRTI is a tech demo as much as it is a mod - an experiment to see how far KSP can be pushed.**
>
> The mod is stable and usable, but expect a few unpolished edges. You may still encounter bugs, performance issues, or missing features. If you do, please report them in the Issues tab with your log file attached. Prefixing the title with `Bug:` helps with triage.

---

>[!WARNING]
> **Mac OS**: Camera streaming works but uses a synchronous GPU readback fallback, as Metal does not support Unity's async readback API. This blocks the main thread briefly each captured frame and may cause minor stuttering at higher stream framerates.

## Overview

**Just Read The Instructions** (**JRTI**) is a mod for **Kerbal Space Program** that lets you view your **Hullcam VDS** camera feeds in a web browser, in the spirit of **OCISLY**.

## Requirements

* **Kerbal Space Program** `1.12.x`
* **[HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)** (latest version)

## Features

* View **Hullcam VDS** camera feeds in a web browser
* Externalize in-game camera views outside the main game window
* Record camera feeds from the web UI (locally on the KSP host, or Save-As on remote clients)
* Grab the raw MJPEG feed URL for OBS or other external tools

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

## Known Issues

A few known issues are tracked but not yet fixed:

* **Firefox recording output is unreliable.** The recorded file may be corrupt or unplayable. Use Chrome or Edge for recording until this is resolved.
* **Stale zero-byte buffer files** are sometimes left in the recordings folder after a session ends. They are safe to delete manually.
* **macOS is not properly supported.** A GPU async API used internally by this Unity version is unavailable on macOS, a legacy quirk inherited from KSP's Unity build. A fix is being investigated.
* **Performance degradation with Parallax.** Parallax integration is disabled by default. Enabling it in the Settings menu may cause significant frame-rate drops.

If you hit something not listed here, please open an issue with your log file attached. Prefixing the title with `Bug:` helps with triage.

## For Developers

### Prerequisites

* Visual Studio 2022 (Windows), or the .NET SDK with your editor of choice
* Kerbal Space Program `1.12.x`

HullcamVDS is declared as a dependency in the project and will be installed automatically via CKAN when you restore packages. If you already have HullcamVDS in your KSP `GameData`, it will be picked up from there without CKAN.

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

The compiled DLL is written directly to `GameData/JustReadTheInstructions/Plugins/`.

> Using `Debug` instead of `Release` will also copy a bunch of Unity and system DLLs into that folder - they're harmless since KSP ignores them, but Release keeps it clean.

To install, symlink or copy `GameData/JustReadTheInstructions/` into your KSP `GameData/`.

## License

This project is licensed under the MIT License.
