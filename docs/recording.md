# Recording

Recordings are made by the game itself and saved on the machine running KSP, in `GameData/JustReadTheInstructions/Web/recordings/`. Live feeds and the stream URLs you give OBS are always MJPEG: the codecs below only apply to recordings.

## Recording a camera

Each camera card on the main page has a **Record** button. Recording:

* keeps going if you close or reload the page;
* leaves a playable file if the game crashes;
* holds the camera's sound, as heard from where the camera is (AAC, 48 kHz stereo). See [Sound](sound.md).

Files are named `[camera name]__cam[id]__[yyyy-mm-dd]_[hhmmss].mp4`. Characters a file name cannot hold are replaced with `_`, and a second recording in the same second gets a `_1`, `_2`... suffix.

Example: `Booster_Cam__cam10__2026-09-25_210455.mp4` is the camera named "Booster Cam", ID 10, recorded on 2026-09-25 at 21:04:55.

The **Recordings** button at the top of the page lists every recording, newest first. **Play** opens one in a new tab and **Download** saves a copy to the device you are using, so you can open the web UI from another machine and copy a recording to it.

## What a recording is

Every recording is an MP4 at the camera's render resolution, at a constant frame rate (Max FPS), with a keyframe every 2 seconds and a bitrate of 0.2 bits per pixel per frame (about 12 Mbps at 1080p 30 FPS).

## Choosing a codec

!!! warning
    Keep **H.264** unless you know your tools handle something else. H.264 opens in every editor, player, phone and OBS setup. Other codecs are for people who have checked that their whole workflow supports them.

Open **Settings** in the web UI and choose a **Video codec**. Only codecs that work on the machine running KSP are listed, and the choice is remembered per browser. JRTI's in-game settings window (`Ctrl` + `Alt` + `F8`) shows which encoders were found.

JRTI picks the first encoder that works, trying graphics card encoders before CPU encoders:

| Codec | Windows | Linux | macOS |
| --- | --- | --- | --- |
| **H.264** (default) | Media Foundation: graphics card, or Windows' own software encoder | `h264_nvenc` → `h264_vaapi` → `h264_qsv` → `h264_vulkan` → `libx264` → `libopenh264` | `h264_videotoolbox` → `libx264` → `libopenh264` |
| **AV1** | With ffmpeg: `av1_nvenc` → `av1_amf` → `av1_qsv` → `av1_vulkan` → `libsvtav1` | `av1_nvenc` → `av1_vaapi` → `av1_qsv` → `av1_vulkan` → `libsvtav1` | `libsvtav1` (Apple chips have no AV1 encoder) |

### About AV1

JRTI gives AV1 the same bitrate as H.264, so files are about the same size but keep more detail. Recent VLC, mpv and web browsers play AV1, and Windows needs Microsoft's *AV1 Video Extension* to preview it. Check that your editor imports AV1 before recording anything important with it.

## ffmpeg

### Windows

H.264 needs nothing extra. AV1 needs `ffmpeg.exe`, for example from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases), on your `PATH` or in `GameData/JustReadTheInstructions/PluginData/ffmpeg/`. JRTI then also uses it for H.264 if Media Foundation is missing (Windows N editions).

### macOS

Recording goes through [ffmpeg](https://ffmpeg.org/), which must be installed (for example with Homebrew: `brew install ffmpeg`), or placed in `GameData/JustReadTheInstructions/PluginData/ffmpeg/`, which JRTI checks first.

### Linux

See [Linux and Proton](linux.md), including KSP running through Proton.

### Checking your ffmpeg

To see which encoders your ffmpeg build has:

```bash
ffmpeg -hide_banner -encoders | grep -E "264|av1"
```

### Which encoder runs on what

| Encoder | Runs on |
| --- | --- |
| `*_nvenc` | NVIDIA graphics cards. AV1 needs an RTX 40 series or newer |
| `*_amf` | AMD graphics cards on Windows. AV1 needs an RX 7000 series or newer |
| `*_vaapi` | AMD and Intel graphics cards through Mesa or Intel's media driver. AV1 needs an AMD RX 7000 series, Intel Arc, Intel Core Ultra or newer |
| `*_qsv` | Intel graphics. AV1 needs Intel Arc, Intel Core Ultra or newer |
| `h264_videotoolbox` | Macs |
| `*_vulkan` | Graphics cards through Vulkan, when the encoders above are unavailable |
| `libx264`, `libopenh264`, `libsvtav1` | The CPU. Slower, and can drop frames at high resolutions while KSP is running |

## Record stays off

If the game cannot record (no encoder found), **Record** stays off and JRTI's settings window in KSP (`Ctrl` + `Alt` + `F8`) says why. On Linux and Proton see [Linux and Proton](linux.md#problems).
