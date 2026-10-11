# Settings and mods

## Shortcuts

| Shortcut (in flight) | Opens |
| --- | --- |
| `Ctrl` + `Alt` + `F7` | JRTI's main window (camera list, open, stream) |
| `Ctrl` + `Alt` + `F8` or `F9` | Settings and integrations |
| `Ctrl` + `Alt` + `F6` | Performance overlay |

## Web UI and port

The web UI lives at `http://localhost:8080/`, or the port set in the settings. Other devices on your network use the address shown at the bottom of the page.

If another program already uses the port, JRTI says so on screen and in `KSP.log`. See [Troubleshooting](troubleshooting.md#the-web-ui-does-not-open).

## Settings file

Settings are saved in `GameData/JustReadTheInstructions/PluginData/settings.cfg`. Defaults live in the mod itself, so updates never reset your choices.

Per-camera sound choices are in `PluginData/camera-sound.cfg`, and saved layouts in `PluginData/Layouts/`.

## Visual mods

Visual mods are applied to camera feeds as in the main view, and can be turned off one by one under **integrations** in the settings window:

* Deferred
* TUFX, without motion blur, which smears feeds that render at their own pace
* Scatterer
* EVE
* Parallax
* Firefly
* The stock re-entry and Mach effects, when Firefly is not installed

!!! warning
    Parallax integration is off by default. Enabling it can cause significant frame-rate drops.

## Loss of signal image

The image shown in the web UI when a camera feed is unavailable can be replaced with any image of your choice (recommended: `1920×1080`). Add this file:

```text
GameData/JustReadTheInstructions/Web/images/customlos.png
```

If `customlos.png` is not present, JRTI falls back to the built-in `GameData/JustReadTheInstructions/Web/images/los.png`. That one is overwritten on every update, so do not edit it. Delete `customlos.png` to go back to the default.

!!! warning
    Editing other files in the `Web` folder is not supported and may break the mod.
