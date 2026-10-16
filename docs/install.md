# Install

## Requirements

* **Kerbal Space Program** `1.12.x`
* **[HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)** (latest version)

## Install the mod

**CKAN:** search for `JustReadTheInstructions` in [CKAN](https://github.com/KSP-CKAN/CKAN) and install it.

**SpaceDock:** download from the [SpaceDock page](https://spacedock.info/mod/4212/Just%20Read%20The%20Instructions) and install it the same way as the GitHub ZIP below.

**GitHub:** download the latest ZIP from the [Releases page](https://github.com/RELMYMathieu/JustReadTheInstructions/releases), extract it, and move the `JustReadTheInstructions` folder into your KSP `GameData` folder.

Your final install should look like this:

```text
Kerbal Space Program/
└── GameData/
    └── JustReadTheInstructions/
```

## First run

1. Start KSP and fly a craft with a Hullcam VDS camera.
2. Press `Ctrl` + `Alt` + `F7` to open JRTI's window and see the camera list.
3. Open `http://localhost:8080/` in a browser. The page works offline: it loads nothing from the internet.

To open it from another device, such as a phone or the computer running OBS, use the network address shown at the bottom of the web UI, for example `http://192.168.1.10:8080/`. If the port is already used by another program, see [Troubleshooting](troubleshooting.md#the-web-ui-does-not-open).

## Updating

Extract the new release over the existing `JustReadTheInstructions` folder and overwrite the files. Do not delete the folder first: your settings, saved layouts and recordings live inside it, in `PluginData/` and `Web/recordings/`, and releases never contain those.

## Platforms

| Platform | Notes |
| --- | --- |
| Windows | Fully supported. H.264 recording needs nothing extra |
| Linux | Supported. Recording needs ffmpeg, see [Linux and Proton](linux.md) |
| Linux with Proton | Supported. Recording needs a static ffmpeg build, see [Linux and Proton](linux.md#proton) |
| macOS | Camera streaming works but uses a synchronous GPU readback, as Metal does not support Unity's async readback API. This blocks the main thread briefly each captured frame and may stutter at higher stream framerates |
