# Linux and Proton

JRTI runs on the native Linux build of KSP and on the Windows build through Proton. Everything works the same as on Windows except recording, which needs ffmpeg set up once.

## Which one are you running?

Look inside KSP's folder:

| Folder | Build |
| --- | --- |
| `KSP_Data` | Native Linux |
| `KSP_x64_Data` | The Windows build, so KSP is running through Proton |

## What works without setup

The web UI, live feeds, layouts, pan and tracking, sound and the Hullcam filters (night vision and the others) all work out of the box. Only [recording](recording.md) needs the steps below.

## Native Linux

Install ffmpeg from your distribution's package manager:

| Distribution | Command |
| --- | --- |
| Debian, Ubuntu, Linux Mint, Pop!_OS | `sudo apt install ffmpeg` |
| Arch, Manjaro, EndeavourOS | `sudo pacman -S ffmpeg` |
| Fedora | `sudo dnf install ffmpeg-free` (hardware encoders and AV1 work; for the full build with libx264, enable [RPM Fusion](https://rpmfusion.org/Howto/Multimedia) and run `sudo dnf swap ffmpeg-free ffmpeg --allowerasing`) |
| openSUSE | `sudo zypper install ffmpeg` (the official build has hardware encoders, OpenH264 and AV1 but no libx264; the [Packman](https://en.opensuse.org/Additional_package_repositories#Packman) repository provides the full build) |
| Anything else | Your package manager's `ffmpeg` package, or the static build below |

Start KSP and [check the encoders](#check-it-worked).

The package is not required: a static build works on any distribution, and is the way to go on immutable systems such as SteamOS and the Steam Deck. Follow the [Proton steps](#proton) below. JRTI checks `GameData/JustReadTheInstructions/PluginData/ffmpeg/` before the system's ffmpeg.

## Proton

Windows' encoder (Media Foundation) does not exist under Proton, so JRTI records the way native Linux does, through a Linux build of ffmpeg. Proton runs games inside Steam's container, which cannot run the ffmpeg from your package manager, so use a static build:

1. Download `ffmpeg-...-linux64-gpl-....tar.xz` from [BtbN's FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds/releases).
2. Extract it into `GameData/JustReadTheInstructions/PluginData/ffmpeg/` (the whole folder or just the `ffmpeg` file).
3. Start KSP and [check the encoders](#check-it-worked).

## Check it worked

Open JRTI's settings window in the game (`Ctrl` + `Alt` + `F8`). It lists the encoders it found, for example `av1_vaapi, h264_vaapi (ffmpeg)`. Then open **Settings** in the web UI and pick a **Video codec**: only codecs that work on your machine are listed.

If nothing is found, **Record** stays off and the settings window says why. Under Proton without ffmpeg it tells you to put a Linux build in the `PluginData/ffmpeg/` folder.

## Which encoder it uses

JRTI tries graphics card encoders before CPU encoders, in this order:

| Codec | Order |
| --- | --- |
| **H.264** (default) | `h264_nvenc` → `h264_vaapi` → `h264_qsv` → `h264_vulkan` → `libx264` → `libopenh264` |
| **AV1** | `av1_nvenc` → `av1_vaapi` → `av1_qsv` → `av1_vulkan` → `libsvtav1` |

In practice NVIDIA cards use NVENC, and AMD and Intel cards use VA-API. What each encoder needs is listed in [Recording](recording.md#which-encoder-runs-on-what). To see what your ffmpeg build has:

```bash
ffmpeg -hide_banner -encoders | grep -E "264|av1"
```

Keep **H.264** unless you have checked that your whole workflow handles AV1. See [Choosing a codec](recording.md#choosing-a-codec).

!!! note "Fedora with an AMD graphics card"
    Fedora's Mesa drivers leave out H.264 and H.265 hardware encoding, so H.264 falls back to the CPU unless you install RPM Fusion's `mesa-va-drivers-freeworld`. AV1 hardware encoding on supported cards should still work. Intel and NVIDIA cards use their own drivers and are not affected.

## Problems

* **Record stays off.** The settings window says why. The usual cause is ffmpeg missing, or a copy that cannot run: under Proton the system's ffmpeg does not work, use the static build above.
* **Only CPU encoders are listed** (`libx264`, `libsvtav1`). Your graphics driver has no hardware encoder, or your ffmpeg build lacks it. CPU encoding is slower and can drop frames at high resolutions while KSP is running.
* **The web UI does not open.** See [Troubleshooting](troubleshooting.md#the-web-ui-does-not-open).

Building the mod yourself under Proton is covered in [CONTRIBUTING.md](https://github.com/RELMYMathieu/JustReadTheInstructions/blob/main/CONTRIBUTING.md).
