
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [v2.0.0-beta.3] Web UI Recording (Beta 3) - 2026-04-17

### Added
- Manual pause/resume button on recording cards
- Loss-of-signal triggered pause now waits 5 s before acting — recording allowed to capture the LOS screen during that window

### Fixed
- Grid layout broken after live/offline section split — `#cameras` rule was orphaned; replaced with rules targeting `#cameras-live` and `#cameras-offline`
- Recording cards no longer show a double preview, recorder canvas is now absolutely positioned and the offline overlay is explicitly hidden on mount
- Snapshot loop fired immediately on all cards simultaneously after the jitter refactor; first fetch is now immediate, jitter only offsets the interval start

### Changed
- LOS signal to recorder decoupled from visual offline state, now the recorder has its own 5 s delay independent of the overlay delay
- Paused status text no longer says "signal lost" since pause is now also user-triggered (when pausing footage)

## [v2.0.0-beta.2] Web UI Recording (Beta 2) - 2026-04-17

### Added
- Remote viewers (non-localhost) get a local Save-As dialog instead of uploading to the KSP machine
- "Copy URL" copies the viewer page link; new "Copy Raw" button copies the bare MJPEG feed URL for OBS and external tools (hover tooltip included)

### Fixed
- Removed `video/x-matroska;codecs=avc1` from MIME candidate list — it is unsupported by all major browsers and was masking the correct VP9 webm fallback on Linux
- Cameras no longer render when nobody is watching — the lazy rendering guard is now applied before `SetCamerasEnabled`, not after. Previously the GPU rendered every frame regardless of active clients
- Snapshot polling interval raised to 10 s (was 2 s) and snapshot interest window reduced to 3 s, so cameras sleep between polls rather than staying hot continuously
- Heartbeat and server-side session management are skipped entirely for remote (local-save) recordings

## [v2.0.0-beta.1] Web UI Recording (Beta 1) - 2026-04-16

### Added
- Basic recording support in the web UI (experimental, may be buggy)
- Add support for recording within the C# API

## [1.0.0] - 2026-04-15

### Added
- Initial public release !!
- Web-viewable Hullcam VDS camera feeds via built-in HTTP stream server
- MJPEG stream and snapshot endpoints per camera
- In-game GUI to open, stream, and manage cameras
- Settings GUI with configurable resolution, FOV, anti-aliasing, stream port, JPEG quality, and max FPS (also accessible via Ctrl+Alt+F9)
- Integration support for Scatterer, EVE, TUFX, Deferred, Parallax, and Firefly
- Debug menu (Ctrl+Alt+F8) for toggling integrations at runtime
- Customizable Loss of Signal image

[unreleased]: https://github.com/RELMYMathieu/JustReadTheInstructions/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/RELMYMathieu/JustReadTheInstructions/releases/tag/v1.0.0
