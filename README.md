<p align="center">
  <img src="./docs/iconfull.png" alt="JRTI Icon" width="682">
</p>

<p align="center">
    <a href="https://ko-fi.com/relmymathieu"><img src="https://img.shields.io/badge/ko--fi-support-ff5f5f.svg?style=flat&logo=ko-fi&logoColor=white" alt="Ko-fi"/></a>
    <a href="https://github.com/RELMYMathieu/JustReadTheInstructions/releases"><img src="https://img.shields.io/github/downloads/RELMYMathieu/JustReadTheInstructions/total.svg?style=flat&logo=github&logoColor=white" alt="Total downloads" /></a>
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

> [!WARNING]
> **JRTI is currently an experimental public release.**
>
> Expect rough edges, compatibility quirks, and breaking changes over time.

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

The `2.0.0` line is still in beta. A few rough edges are tracked but not yet fixed:

* **Scrubbing recorded files is unreliable.** The footage itself is intact, but seeking inside the resulting `.webm` / `.mp4` can jump around, show heavy artifacting, show the wrong duration, or freeze briefly. This is a limitation of how the browser's `MediaRecorder` writes chunks straight to disk, as the final file has no seek index. A post-finalize remux pass is planned. In the meantime, playing the file end-to-end works fine; for clean scrubbing, re-encode once through ffmpeg or VLC.
* **Odd reflection/shadow artifact on planets.** With Scatterer and/or EVE installed, looking at Kerbin or the Mun through a JRTI camera can render a noticable secondary reflection or shadow that isn't present on the main game camera. The scene lighting and actual shadows render correctly - this appears to be a probe or hook tied to the main camera's frustum bleeding into the mod camera. Under investigation.

If you hit something not listed here, please open an issue with your log file attached.
Additionally, being issue with "Bug:" in the title, it will help me it triage faster.

## For Developers

### Prerequisites

* Visual Studio 2022, or MSBuild 17+
* Kerbal Space Program `1.12.x`
* [HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued) installed in your KSP `GameData`

### Setup

1. Clone the repository
2. Copy `KSPPath.props.example` to `KSPPath.props` at the repository root
3. Set `<KSPPath>` to your KSP install directory
4. If HullcamVDS is at a non-standard path, also override `<HullcamVDSPath>`

```xml
<Project ToolsVersion="12.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <KSPPath>C:\Your\KSP\Install</KSPPath>
    <!-- Optional: only if HullcamVDS isn't at GameData\HullCameraVDS\Plugins\ -->
    <!-- <HullcamVDSPath>C:\Your\KSP\Install\GameData\HullCameraVDS\Plugins\HullcamVDSContinued.dll</HullcamVDSPath> -->
  </PropertyGroup>
</Project>
```

> `KSPPath.props` is gitignored and will never be committed.

### Building

Open `JustReadTheInstructions.sln` in Visual Studio and build normally, or run:

```powershell
msbuild JustReadTheInstructions.sln /p:Configuration=Release
```

The built mod will be output to:

```text
JustReadTheInstructions\Distribution\GameData\JustReadTheInstructions\
```

Copy the produced `JustReadTheInstructions` folder into your KSP `GameData` to install the development build.

## License

This project is licensed under the MIT License.
