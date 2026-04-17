# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Custom LOS screen (png `customlos.png`)

## [v2.0.0-beta.4] Web UI Recording (Beta 4) - 2026-04-17

### Added
- Server-side SIDX (segment index) injection on MP4 finalize, enabling frame-accurate scrubbing and seeking in all recorded files without any post-processing step

### Fixed
- Pause/Resume button on recording cards stayed visible when idle — `.btn` uses `display: inline-flex`, which overrode the HTML `hidden` attribute (whose default is `display: none`). Added a single `[hidden] { display: none !important; }` rule so the attribute works as expected everywhere it's used
- Camera-card footer size label now reads `LAST RECORDING SIZE = X MB` instead of a bare byte count, and persists after the recording ends instead of clearing the moment the state flips to idle
- Accidentally hardcoded WebM as mimeType instead of accepting whichever other flag was available in the candidate list, which caused recording to fail on certain occasions
- MP4 recordings were never seekable — `FixMp4` was silently crashing on every finalize due to a 4-byte header miscalculation in the SIDX builder (`28` → `32`), causing an `IndexOutOfRangeException` that was swallowed by the outer catch
- Offline camera cards disappeared on page refresh — `localStorage` was overwritten with only the live camera list on each sync, immediately evicting any card that had just gone offline. Persistence now snapshots all cards after each sync loop completes

### Changed
- `camera-card.js` refactor for readability (no behaviour change): state-dependent UI mutations now driven by a `REC_STATES` lookup table instead of a four-branch `if/else`, DOM construction split across `_buildPreview` / `_buildInfo` / `_buildFooter`, and the three near-identical copy-button blocks replaced by `makeButton` / `makeCopyButton` helpers
- The disabled-when-unsupported record button now uses a `.btn-unsupported` CSS class instead of inline `opacity` / `pointerEvents` styles, to match the existing `.btn.watch-disabled` pattern
- Removed WebM support due to inconsistent browser support. The list of MIME types is now just MP4 variants, which should be supported widely enough for the time being
- `JRTIStreamServer` split into partial class files by concern (`Http`, `Recording`, `Mp4`, `Types`) to reduce the size of the monolith
- `FixMp4` is now self-contained — wrapped in its own try-catch that logs full exceptions with stack trace and never propagates to `FinalizeRecordingSession`

### Known Issues
- Faint reflection/shadow artifact visible on Kerbin and the Mun through JRTI cameras when Scatterer and/or EVE are installed. Actual shadows render correctly, so this looks like a hook or reflection probe tied to the main camera's frustum bleeding into the mod camera. Under investigation
- Firefox doesn't support the current MIME codecs. Thus, the recording button is greyed out.

## [v2.0.0-beta.3] Web UI Recording (Beta 3) - 2026-04-17

### Added
- Manual pause/resume button on recording cards
- Loss-of-signal triggered pause now waits 5 s before acting — the recording is allowed to capture the LOS screen during that window

### Fixed
- Grid layout broken after live/offline section split — `#cameras` rule was orphaned; replaced with rules targeting `#cameras-live` and `#cameras-offline`
- Recording cards no longer show a double preview: the recorder canvas is now absolutely positioned and the offline overlay is explicitly hidden on mount
- Snapshot loop no longer fires immediately on every card at once after the jitter refactor; first fetch is immediate, jitter only offsets the interval start

### Changed
- LOS signal to the recorder is decoupled from the visual offline state — the recorder has its own 5 s delay independent of the overlay delay
- Paused status text no longer reads "signal lost", since pause is now also user-triggered

## [v2.0.0-beta.2] Web UI Recording (Beta 2) - 2026-04-17

### Added
- Remote viewers (non-localhost) get a local Save-As dialog instead of uploading to the KSP machine
- "Copy URL" copies the viewer page link; new "Copy Raw" button copies the bare MJPEG feed URL for OBS and other external tools (with a hover tooltip)

### Fixed
- Removed `video/x-matroska;codecs=avc1` from the MIME candidate list — it is unsupported by all major browsers and was masking the correct VP9 WebM fallback on Linux
- Cameras no longer render when nobody is watching — the lazy rendering guard is now applied before `SetCamerasEnabled`, not after. Previously the GPU rendered every frame regardless of active clients
- Snapshot polling interval raised to 10 s (was 2 s) and snapshot interest window reduced to 3 s, so cameras sleep between polls rather than staying hot continuously
- Heartbeat and server-side session management are now skipped entirely for remote (local-save) recordings

## [v2.0.0-beta.1] Web UI Recording (Beta 1) - 2026-04-16

### Added
- Basic recording support in the web UI (experimental)
- Recording support in the C# API

## [1.0.0] - 2026-04-15

### Added
- Initial public release