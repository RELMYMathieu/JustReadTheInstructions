<p align="center">
  <img src="./docs/iconfull.png" alt="JRTI Icon" width="682">
</p>

<p align="center">
  A web-viewable Hullcam VDS camera feed mod for Kerbal Space Program.
</p>

<p align="center">
  Spiritual successor to <strong><a href="https://github.com/jrodrigv/OfCourseIStillLoveYou">OfCourseIStillLoveYou</a></strong>.
</p>

---

> [!WARNING]
> **JRTI is currently an experimental public release / proof of concept.**
>
> It is being released to see how people like it, how it behaves on different installs, and how far this idea can be pushed in KSP. Expect rough edges, compatibility quirks, and breaking changes over time.

## Overview

**Just Read The Instructions** (**JRTI**) is a mod for **Kerbal Space Program** that allows you to view your **Hullcam VDS** camera feeds in a web browser.

The goal of the project is to push externalized camera viewing in KSP further, in the spirit of **OCISLY**, while exploring how far the game can be taken with this kind of setup.

Even though this release is experimental in nature, it is versioned as **1.0.0** because the project is intended to stay iterative by design rather than wait for some imaginary “perfectly finished” state. In that sense, while experimental, it is still a proper public release.

## Requirements

* **Kerbal Space Program** `1.12.x`
* **latest version of [HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)**

## Features

* View **Hullcam VDS** camera feeds in a web browser
* Externalize in-game camera views outside the main game window
* Experimental foundation for pushing KSP camera systems further

## Screenshot

![JRTI Screenshot](./docs/screenshot-1.png)

## Installation

1. Go to the **Releases** page of this repository
2. Download the latest release ZIP
3. Extract the contents of the ZIP
4. Move the included **JustReadTheInstructions** folder into your KSP **GameData** folder

Your final install should look like this:

```text
Kerbal Space Program/
└── GameData/
    └── JustReadTheInstructions/
```

## Customization

The **Loss of Signal** image displayed in the web UI when a camera feed is unavailable can be replaced with any PNG of your choice (recommended resolution: `1920×1080`).

Replace this file with your own:
```text
GameData/JustReadTheInstructions/Web/images/los.png
```

> [!WARNING]
> Your custom image will be overwritten if you update the mod. Back it up before updating.

> [!CAUTION]
> Editing the HTML page you may find in the Web folder is strongly discouraged and may break the mod's functionality. You are free to do so on your own copy, but it is not supported.

## For Developers

### Prerequisites

* Visual Studio 2022, or MSBuild 17+
* Kerbal Space Program `1.12.x`
* [HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued) installed in your KSP's GameData directory

### Setup

1. Clone the repository
2. Copy `KSPPath.props.example` to `KSPPath.props` at the repository root
3. Edit `KSPPath.props` and set `<KSPPath>` to your KSP install directory (at game root level)
4. If HullcamVDS is installed at a non-standard path, also override `<HullcamVDSPath>`

```xml
<Project ToolsVersion="12.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <KSPPath>C:\Your\KSP\Install</KSPPath>
    <!-- Optional: only if HullcamVDS isn't at GameData\HullCameraVDS\Plugins\ -->
    <!-- <HullcamVDSPath>C:\Your\KSP\Install\GameData\HullCameraVDS\Plugins\HullcamVDSContinued.dll</HullcamVDSPath> -->
  </PropertyGroup>
</Project>
```

> `KSPPath.props` is gitignored and will never be committed, as it is meant to represent your local game install path.

### Building

Open `JustReadTheInstructions.sln` in Visual Studio and build it normally, or run:

```powershell
msbuild JustReadTheInstructions.sln /p:Configuration=Release
```

The built mod will be output to:

```text
JustReadTheInstructions\Distribution\GameData\JustReadTheInstructions\
```

To install the development build, copy the produced `JustReadTheInstructions` folder into your KSP `GameData` directory.
