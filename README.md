<p align="center">
  <img src="./docs/iconfull.png" alt="JRTI Icon" width="682">
</p>

<p align="center">
    <a href="https://ko-fi.com/relmymathieu"><img src="https://img.shields.io/badge/ko--fi-support-ff5f5f.svg?style=flat&logo=ko-fi&logoColor=white" alt="Ko-fi"/></a>
    <a href="https://github.com/RELMYMathieu/JustReadTheInstructions/releases/latest"><img src="https://img.shields.io/github/downloads/RELMYMathieu/JustReadTheInstructions/total.svg?style=flat&logo=github&logoColor=white" alt="Total downloads" /></a>
    <a href="https://github.com/RELMYMathieu/JustReadTheInstructions/releases/latest"><img src="https://img.shields.io/github/release/RELMYMathieu/JustReadTheInstructions.svg?style=flat&logo=github&logoColor=white" alt="Latest release" /></a>
    <a href="https://spacedock.info/mod/4212/Just%20Read%20The%20Instructions"><img src="https://img.shields.io/badge/spacedock-download-4f86c6.svg?style=flat&logoColor=white" alt="SpaceDock"/></a>
    <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/license-MIT-97ca00.svg?style=flat&logoColor=white" alt="MIT License" /></a>
</p>

<p align="center">
  A web-viewable Hullcam VDS camera feed mod for Kerbal Space Program.
</p>

<p align="center">
  Spiritual successor to <strong><a href="https://github.com/jrodrigv/OfCourseIStillLoveYou">OfCourseIStillLoveYou</a></strong>.
</p>

<p align="center">
  <strong><a href="https://relmymathieu.github.io/JustReadTheInstructions/">Read the documentation</a></strong>
</p>

---

**Just Read The Instructions** (**JRTI**) lets you view your **Hullcam VDS** camera feeds in a web browser, in the spirit of **OfCourseIStillLoveYou**. It runs entirely inside the game.

![JRTI Screenshot](./docs/screenshot-1.png)

> [!NOTE]
> JRTI is stable and feature-complete. Updates now come at a slower pace: quality-of-life tweaks, good suggestions and fixes. Found a bug? Open an issue with your `KSP.log` attached and prefix the title with `Bug:`.
>
> **macOS:** streaming works but uses a synchronous GPU readback, which may cause minor stuttering at higher stream framerates.

## Features

* View camera feeds in a browser, on the KSP computer or any device on your network - [Cameras](https://relmymathieu.github.io/JustReadTheInstructions/cameras/)
* Build multi-camera layouts and switch a clean feed live for OBS (work in progress) - [Layouts and OBS](https://relmymathieu.github.io/JustReadTheInstructions/layouts-obs/)
* Pan, tilt and track with the TurretCam and LaunchCam - [Cameras](https://relmymathieu.github.io/JustReadTheInstructions/cameras/#pan-and-tracking)
* Hear the game from each camera's position - [Sound](https://relmymathieu.github.io/JustReadTheInstructions/sound/)
* Record camera feeds as MP4 inside the game - [Recording](https://relmymathieu.github.io/JustReadTheInstructions/recording/)
* Raw MJPEG feed URLs for OBS or other tools, with stable camera IDs

## Install

Requires **Kerbal Space Program** `1.12.x` and **[HullcamVDS-Continued](https://github.com/linuxgurugamer/HullcamVDSContinued)**.

* **CKAN:** search for `JustReadTheInstructions`
* **SpaceDock:** [download the mod](https://spacedock.info/mod/4212/Just%20Read%20The%20Instructions)
* **GitHub:** download the latest ZIP from [Releases](https://github.com/RELMYMathieu/JustReadTheInstructions/releases) and put the `JustReadTheInstructions` folder in your KSP `GameData` folder

## Use

Fly a craft with a Hullcam VDS camera, then open `http://localhost:8080/` in a browser. `Ctrl` + `Alt` + `F7` opens JRTI's window inside the game, and `Ctrl` + `Alt` + `F8` its settings.

Everything else, from OBS setups to recording on Linux and Proton, is in the [documentation](https://relmymathieu.github.io/JustReadTheInstructions/).

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for building the mod.

## License

This project is licensed under the MIT License.
