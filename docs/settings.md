# Settings and mods

## Shortcuts

| Shortcut (in flight) | Opens |
| --- | --- |
| `Ctrl` + `Alt` + `F7` | JRTI's main window (camera list, open, stream) |
| `Ctrl` + `Alt` + `F8` or `F9` | Settings and integrations |
| `Ctrl` + `Alt` + `F6` | Performance overlay |

## Settings window

Open it in flight with `Ctrl` + `Alt` + `F8` or `F9`. Everything is saved when you change it.

### Stream / Capture

| Setting | Default | What it does |
| --- | --- | --- |
| Enable web streaming server | On | Turn off to run JRTI with in-game camera windows only: no web UI, browser streaming or recording. Lighter on weak machines. Takes effect when you next enter a flight scene |
| Port | `8080` | The web UI and stream port (1024 to 65535). Changing it needs a game restart |
| JPEG Quality | `90` | Quality of the MJPEG stream, 1 to 100. Lower is smaller and faster |
| Max FPS | `30` | The most renders per second any camera makes, in its window and in streams alike (1 to 120). Each render costs about as much as the game's own view, so lower it if the game slows down |
| Render Width and Height | `1280` x `720` | The resolution every camera renders at, and the size of recordings. Applies the next time a camera opens |
| Anti-Aliasing | `2` | `0` (off), `1`, `2`, `4` or `8`. Applies the next time a camera opens |
| Max Open Cameras | `32` | How many cameras can be open at once (1 to 64) |
| Spread camera renders across frames | On | Cameras take turns instead of all rendering on the same frame, for smoother frame times. Each camera keeps its own frame rate |

### Camera windows (also under Stream / Capture)

| Setting | Default | What it does |
| --- | --- | --- |
| Render overlay with telemetry on docking cameras | On | Shows docking telemetry on docking cameras |
| Fixed preview aspect ratio | On | Keeps the camera window's preview square |
| Camera windows open minimized by default | Off | Double-click a preview to toggle it per window |

The window also shows which video encoders were found. See [Recording](recording.md).

## Web UI and port

The web UI lives at `http://localhost:8080/`, or the port set in the settings. Other devices on your network use the address shown at the bottom of the page.

If another program already uses the port, JRTI says so on screen and in `KSP.log`. See [Troubleshooting](troubleshooting.md#the-web-ui-does-not-open).

## Settings file

Settings are saved in `GameData/JustReadTheInstructions/PluginData/settings.cfg`. Defaults live in the mod itself, so updates never reset your choices.

Per-camera sound choices are in `PluginData/camera-sound.cfg`, and saved layouts in `PluginData/Layouts/`.

## Visual mods

Visual mods are applied to camera feeds as in the main view, and can be turned off one by one under **Visual Mod Integrations** in the settings window. Changes apply at once to every open camera; re-open a camera to fully reinitialize its integration stack.

* Deferred
* TUFX, without motion blur, which smears feeds that render at their own pace
* Scatterer
* EVE
* Parallax
* Firefly
* The stock re-entry and Mach effects, when Firefly is not installed
* The HullcamVDS camera filter, which applies the active filter or overlay (night vision, CRT and so on) to the feed

!!! warning
    Parallax integration is off by default. Enabling it can cause significant frame-rate drops.

Mods that only change how parts look, such as Losket's scorch and dust marks, show on feeds with no setting.

### Trajectories

The Trajectories prediction line is hidden on camera feeds and recordings, so for exampe, you can land a rocket with it in the main view and keep your footage clean. Tick **Trajectories (Show on Feeds)** under **Visual Mod Integrations** to show it on feeds too. The impact and target markers are only ever drawn in the main view.

## Loss of signal image

The image shown in the web UI when a camera feed is unavailable can be replaced with any image of your choice (recommended: `1920×1080`). Add this file:

```text
GameData/JustReadTheInstructions/Web/images/customlos.png
```

If `customlos.png` is not present, JRTI falls back to the built-in `GameData/JustReadTheInstructions/Web/images/los.png`. That one is overwritten on every update, so do not edit it. Delete `customlos.png` to go back to the default.

!!! warning
    Editing other files in the `Web` folder is not supported and may break the mod.
