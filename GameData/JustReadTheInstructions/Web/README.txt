Just Read The Instructions - Web Assets
========================================

This folder contains the web UI served by the mod at:

    http://localhost:8080/

Open that address in any browser while KSP is running in a flight to see your camera feeds.
Other devices on your network use the address shown at the bottom of the page (for example
http://192.168.1.10:8080/). The pages load nothing from the internet, so they work offline.

Full documentation (layouts, OBS and live switching, sound, recording, troubleshooting):

    https://relmymathieu.github.io/JustReadTheInstructions/


CUSTOM LOSS-OF-SIGNAL IMAGE
----------------------------

When a camera goes offline, the page shows a default "loss of signal" image
(images/los.png). This file gets overwritten on every mod update, so do not
edit it directly.

Instead, drop your own image here:

    GameData/JustReadTheInstructions/Web/images/customlos.png

The mod will automatically use it everywhere the default LoS image appears.
To go back to the default, just delete your customlos.png.

Any *standard* image format works (PNG recommended, 1920x1080).


RECORDINGS
----------

Recordings are made by the game and saved in the recordings/ folder here,
named [camera name]__cam[id]__[yyyy-mm-dd]_[hhmmss].mp4.

Example: Booster_Cam__cam10__2026-09-25_210455.mp4 is the camera named
"Booster Cam", ID 10, recorded on 2026-09-25 at 21:04:55.

The Recordings button at the top of the page lists, plays and downloads them.


FOLDER STRUCTURE
----------------

    index.html          Main camera dashboard
    viewer.html         Full-screen single-camera view
    layout.html         Several cameras in one window
    debug.html          Diagnostics (frame times, per-camera stats)
    css/styles.css      Page styles
    fonts/              Commit Mono, the interface font (SIL Open Font License, see CommitMono-OFL.txt)
    js/                 Frontend logic
    images/             UI images and icons (los.png, icons.svg, your customlos.png)
    recordings/         Where recorded feeds are saved
    README.txt          This file
