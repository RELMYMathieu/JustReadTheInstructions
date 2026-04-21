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
    Kerbal_Space_Center__cam12345__2025-06-01_143022.webm

The recording is uploaded to disk in small chunks as it goes, so if the browser
is closed mid-recording, everything captured so far is kept.

You can choose what happens when a camera loses signal while recording.
Open the Settings button on the main page to pick one of:

    Auto-save       Stop and save what was recorded so far  (default)
    Pause           Pause the recording and resume if signal returns
    Discard         Stop and delete the recording


REMOTE RECORDING
-----------------

When you open the web page from a different machine than the one running KSP,
the Record button saves the file to YOUR machine (a Save-As dialog) instead
of to the KSP host's recordings folder. Nothing is uploaded over the network
in that case - the browser does all the encoding and writing itself.


FOLDER STRUCTURE
----------------

    index.html          Main camera dashboard
    viewer.html         Full-screen single-camera view
    css/styles.css      Page styles
    js/                 Frontend logic
    images/             UI images (including los.png and your customlos.png)
    recordings/         Where recorded feeds are saved
    README.txt          This file