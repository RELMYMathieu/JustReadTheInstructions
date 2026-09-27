Just Read The Instructions - Web Assets
========================================

This folder contains the web UI served by the mod at:

    http://localhost:8080/

Open that address in any browser while KSP is running in a flight to see your camera feeds.
Other devices on your network use the address shown at the bottom of the page (for example
http://192.168.1.10:8080/). The pages load nothing from the internet, so they work offline.


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

The Recordings button at the top of the page lists every recording in that folder, newest
first. Play opens one in a new tab and Download saves a copy to the device you are using.

Recording happens inside the game: your graphics card encodes an H.264 MP4 at
the camera's render resolution and a steady 30 FPS (Max FPS). It keeps going
if you close or reload the page, and the file stays playable if the game
crashes. Windows uses its built-in encoder; Linux and macOS need ffmpeg
installed (for example "sudo apt install ffmpeg" or "brew install ffmpeg").

"Video codec" in Settings picks the recording codec. Keep H.264 unless you
know your tools handle something else: it opens everywhere. AV1 is offered
on Linux and macOS when ffmpeg has an AV1 encoder; many editors, older
players and phones cannot open AV1 files.

"Video codec" and the other recorder options are in the Settings panel (top right).

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

The Layout button on the main page opens layout.html: several cameras in one
window, resized to fill it. Open Cameras (or press C) and click a camera to
add it, drag it onto a tile to replace that feed, or use "Add all". Drag a
tile onto another to swap them. Spotlight (double-click a tile, or press 1
to 9) makes one tile large with the others beside it, Fullscreen shows only
the cameras, Names picks whether camera names show on the tiles, and
Columns (L) fixes how many tiles sit side by side instead of letting the
page pick the biggest tiles.
Removing or swapping tiles offers an Undo button, and Ctrl+Z undoes any
change. Press ? for every shortcut. The controls fade out on their own.

Three ways to keep a layout:

    This browser    The default: remembered by camera name in this browser,
                    so it comes back on your next flight.
    Saved           The menu next to the layout's name saves it in the game
                    (PluginData/Layouts/). Open it anywhere with
                        http://localhost:8080/layout.html?layout=Launch
                    Every screen showing a saved layout follows the changes
                    made to it from any other screen, OBS included.
    Link            "Copy link" on an unsaved layout gives an address like
                        http://localhost:8080/layout.html?cams=10,11,12
                    that always shows those camera IDs.

For an OBS browser source, a saved layout is the easiest: point OBS at
layout.html?layout=YourName once, then rearrange it from any browser.


CLEAN FEED (LIVE SWITCHING)
----------------------------

    http://localhost:8080/layout.html?program

is the clean feed: it never shows controls or a cursor, and it displays
whichever saved layout is "on air". Add it once as an OBS browser source.

To switch what is on air, open a saved layout on the layout page and press
Take on air, or press Shift + 1 to 9 to open and take the first nine saved
layouts (their numbers are shown in the layout menu). The clean feed
changes at once: cameras that stay glide to their new place, new ones fade
in, removed ones fade out. Anything that can open a URL can switch too, for
example a Stream Deck button:

    http://localhost:8080/program/take/Launch
    http://localhost:8080/program/clear          (nothing on air: black)

The on-air layout is remembered by the game (PluginData/program.txt).

For a seamless split screen (for example two cameras side by side on a
second monitor), turn on Fill, then Fullscreen. Fill removes the gaps and
black bars and crops each feed to its tile. To avoid the crop, set JRTI's
Render Width and Height to the tile size, for example 960 x 1080 for two
halves of a 1920 x 1080 screen (this applies to every camera).


REMOTE RECORDING
-----------------

When you open the web page from a different machine than the one running KSP,
recordings are still made by the game and saved on the KSP machine. Use
Recordings, then Download, to copy one to YOUR machine. The legacy browser
recorder (picked in Settings) can still save straight to your machine with a
Save-As dialog; that option goes away with the legacy recorder in v3.0.0.


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