Just Read The Instructions - Web Assets
========================================

This folder contains the web UI served by the mod at:

    http://localhost:8080/

Open that address in any browser while KSP is running in a flight to see your camera feeds.


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


RECORDING CAMERA FEEDS
-----------------------

Each camera card on the main page has a Record button. Click it to start
recording that feed. Recordings are saved directly on the machine running KSP:

    GameData/JustReadTheInstructions/Web/recordings/

Files are named like:
    Kerbal_Space_Center__cam12345__2025-06-01_143022.mp4

Recording happens inside the game: your graphics card encodes an H.264 MP4 at
the camera's render resolution and a steady 30 FPS (Max FPS). It keeps going
if you close or reload the page, and the file stays playable if the game
crashes. Windows uses its built-in encoder; Linux and macOS need ffmpeg
installed (for example "sudo apt install ffmpeg" or "brew install ffmpeg").

"Video codec" in Settings picks the recording codec. Keep H.264 unless you
know your tools handle something else: it opens everywhere. AV1 is offered
on Linux and macOS when ffmpeg has an AV1 encoder; many editors, older
players and phones cannot open AV1 files.

If in-game recording cannot start, the legacy browser recorder takes over. It
is deprecated and will be removed in v3.0.0. "Record with" in Settings picks
one; in-game recording can also be turned off in JRTI's settings window.

You can choose what happens when a camera loses signal while recording.
Open the Settings button on the main page to pick one of:

    Auto-save       Stop and save what was recorded so far  (default)
    Pause           Pause the recording and resume if signal returns
    Discard         Stop and delete the recording


CAMERA LAYOUT
--------------

The grid button on the main page opens layout.html: several cameras in one
window. Add tiles, pick a camera for each, and the tiles resize to fill the
window. Spotlight (or double-click a tile) makes one tile large, and
Fullscreen shows only the cameras. The layout is remembered by camera name,
so it comes back on your next flight.

For an OBS browser source, use "Copy link" on the layout page. It gives an
address like

    http://localhost:8080/layout.html?cams=10,11,12

that always shows those camera IDs. The controls fade out on their own.

For a seamless split screen (for example two cameras side by side on a
second monitor), turn on Fill, then Fullscreen. Fill removes the gaps and
black bars and crops each feed to its tile. To avoid the crop, set JRTI's
Render Width and Height to the tile size, for example 960 x 1080 for two
halves of a 1920 x 1080 screen (this applies to every camera).


REMOTE RECORDING
-----------------

When you open the web page from a different machine than the one running KSP,
recordings are still made by the game and saved on the KSP machine. To save to
YOUR machine instead (a Save-As dialog), pick the legacy browser recorder in
Settings: the browser then does all the encoding and writing itself. This
option goes away with the legacy recorder in v3.0.0.


FOLDER STRUCTURE
----------------

    index.html          Main camera dashboard
    viewer.html         Full-screen single-camera view
    layout.html         Several cameras in one window
    css/styles.css      Page styles
    js/                 Frontend logic
    images/             UI images (including los.png and your customlos.png)
    recordings/         Where recorded feeds are saved
    README.txt          This file