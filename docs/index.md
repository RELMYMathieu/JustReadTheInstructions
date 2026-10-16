# Just Read The Instructions

**JRTI** is a mod for **Kerbal Space Program** that shows your **Hullcam VDS** camera feeds in a web browser, in the spirit of OfCourseIStillLoveYou. It runs entirely inside the game: no extra program, no setup beyond the mod and HullcamVDS-Continued.

![JRTI web UI](screenshot-1.png)

## Quick start

1. [Install](install.md) JRTI and HullcamVDS-Continued.
2. Fly a craft that has a Hullcam VDS camera.
3. Open `http://localhost:8080/` in a browser, on the KSP computer or any device on your network.

Press `Ctrl` + `Alt` + `F7` in flight for JRTI's window inside the game.

## What it does

* **Web viewer** - camera feeds in any browser, with brightness, contrast, gamma and field of view adjusted per camera. [Cameras](cameras.md)
* **Layouts** (work in progress) - several cameras in one window (grid, spotlight, edge to edge split screens), saved in the game so every device and OBS source shows the same thing. [Layouts and OBS](layouts-obs.md)
* **Live switching** (work in progress) - a clean feed for OBS that follows whichever saved layout is on air, switched from the browser, hotkeys or a Stream Deck. [Layouts and OBS](layouts-obs.md)
* **Pan and tracking** - turn the TurretCam and LaunchCam from the browser, or let them follow the active vessel or your target. [Cameras](cameras.md)
* **Camera sound** - hear the game from each camera's position, in the browser, on the clean feed and in recordings. [Sound](sound.md)
* **In-game recording** - MP4 files made with the graphics card's encoder, played or downloaded from the web UI. [Recording](recording.md)
* **Raw MJPEG feeds** - one URL per camera for OBS or other tools. [Cameras](cameras.md)
* **Stable IDs** - name cameras and give them a numeric ID from the part's right-click menu in the editor, so the stream URL never changes. [Cameras](cameras.md)

## Status

JRTI is stable and feature-complete. Updates now arrive at a slower pace: quality-of-life tweaks, good community suggestions, and fixes. Bug reports, suggestions and pull requests are welcome on [GitHub](https://github.com/RELMYMathieu/JustReadTheInstructions/issues).

JRTI is the spiritual successor to [OfCourseIStillLoveYou](https://github.com/jrodrigv/OfCourseIStillLoveYou).
