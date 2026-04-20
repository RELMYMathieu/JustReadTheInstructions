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

> [!WARNING]
> **JRTI is currently an experimental public release.**
> Expect rough edges, compatibility quirks, and breaking changes over time.

>[!WARNING]
> Mac OS has limited compatibility, the mod may not function properly. This is still being looked into.

## Overview

**Just Read The Instructions** (**JRTI**) is a mod for **Kerbal Space Program** that lets you view your **Hullcam VDS** camera feeds in a web browser, in the spirit of **OCISLY**.

## Requirements

* **Kerbal Space Program** `1.12.x`
* **[HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)** (latest version)

## Features

* View **Hullcam VDS** camera feeds in a web browser
* Externalize in-game camera views outside the main game window

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

The **Loss of Signal** image shown in the web UI when a camera feed is unavailable can be replaced with any PNG of your choice (recommended: `1920×1080`).

Replace this file with your own:
```text
GameData/JustReadTheInstructions/Web/images/los.png
```

> [!WARNING]
> Your custom image will be overwritten on mod updates. Back it up beforehand.

> [!CAUTION]
> Editing files in the `Web` folder is not supported and may break the mod's functionality.

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
