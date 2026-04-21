# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

### Added

- A new parameter `FixedPreviewAspectRatio` (boolean) to `settings.cfg`, allowing the in-game camera preview to be forced to a 1:1 aspect ratio
- Mod version label in settings menu

### Fixed

- More space for the streaming label

## v2.1.0 - 2026-04-22

### Added

- Draggable camera cards instead of static, "randomly" (not really :p) ordered camera cards
- New "render every other frame" setting exposed, allowing rendering every frame (with warning about performance)

### Changed

- We now show live feed when camera is already being watched (costs no performance and nice to have)
- The camera status ("Idle" label, by default) clearer, i.e : when a camera being watched
- The two notes above close issue #13 proposing nice QoL changes
- Refactored the camera streaming JavaScript code (no functionality change)
- We now take a snapshot whenever camera goes to an inactive state from an active one doesn't watse ressources since it was active up until that point, smoothly allows to transition between active and inactive view
- Fix customlos.png not found (error 404) in some instances, where the code would mistakenly interpret is as always present when it's only conditionally there
- Watch button now still usable after camera disconnects (in cases where signal may come back)
- Bumped up default settings (performance improvements since v1.0.0 allows for that!!)
- Use Hullcam's FoV instead of our "own" FoV (still allow to modify it with the slider, but by default match Hullcam's if it's applicable)

### Fixed

- Anti-Aliasing minimum option from 1 to 0 (so it's clearer 0 means NO AA)
- Mobile experience should feel a lot better with the new responsive layout and touch-friendly controls

## v2.0.1 - 2026-04-22

### Fixed

- "Stop" button now properly turns green if a client is requesting the camera feed

## v2.0.0 Web UI Recording, Polishing, QoL and more! - 2026-04-20

### Added

- Recording is now available in this new version
- Unified **Settings & Integrations** menu merging the former Settings and Debug menus into a single scrollable window with four collapsible sections: Stream/Capture, Visual Mod Integrations, Diagnostics, and Troubleshooting
- Visual mod integrations (Deferred, TUFX, Scatterer, EVE, Parallax, Firefly) are now toggleable directly from the unified menu with live availability indicators
- **HullcamVDS Camera Filter** integration — discovers the active Hullcam filter/overlay material at runtime via reflection and blits it over the stream frame; falls back silently to raw frame when unavailable or unset
- Integration enable/disable state now persists to `settings.cfg` and is restored on launch (previously runtime-only)
- Stream All button on in-game Flight UI — streams all available cameras in one click (shown only when more than one camera is available)
- Settings button on in-game Flight UI — opens the unified settings menu directly from the flight toolbar window
- Custom LOS screen support (drop `customlos.png` alongside `los.png` to override)
- `GET /session` endpoint returning a per-launch UUID, used by the web client to detect a fresh game session
- Troubleshooting section in the unified menu documenting when a camera or feed reload is needed, with a one-click "Reload Integrations" button

### Fixed

- Persisted offline/destroyed camera cards from a previous game launch are now cleared on page load when a new game session is detected — the web client compares the stored session UUID against the server's and wipes `localStorage` on mismatch
- Firefox integration now properly allows recording (however, not constantly reliable, see known issues)

### Changed

- Integration enable flags moved from `JRTIDebugMenu` (runtime-only statics) to `JRTISettings` (persisted properties) — **Parallax still defaults to `false`**
- `JRTIDebugMenu` removed; all functionality absorbed into the unified `JRTISettingsGUI`
- Ctrl+Alt+F8 (former debug menu) and Ctrl+Alt+F9 (former settings) now both open the same unified menu

### Known Issues

- Firefox recording output is unreliable — the recorded file may be corrupt or unplayable
- Stale zero-byte buffer files are sometimes left in the recordings folder after a recording session ends
- macOS is not properly supported — a GPU async API used by this Unity version is unavailable on macOS (legacy KSP/Unity quirk); a fix is being investigated
- Performance degradation with Parallax enabled — Parallax integration is disabled by default for this reason


## v2.0.0-beta.4 Web UI Recording (Beta 4) - 2026-04-17

There is nothing permanent except change

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
- Sometimes, there will be MP4 files with a size of 0 bytes that do not get cleaned up in the recordings folder. This problem is being investigated.


## v2.0.0-beta.3 Web UI Recording (Beta 3) - 2026-04-17

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


## v2.0.0-beta.2 Web UI Recording (Beta 2) - 2026-04-17

### Added

- Remote viewers (non-localhost) get a local Save-As dialog instead of uploading to the KSP machine
- "Copy URL" copies the viewer page link; new "Copy Raw" button copies the bare MJPEG feed URL for OBS and other external tools (with a hover tooltip)

### Fixed

- Removed `video/x-matroska;codecs=avc1` from the MIME candidate list — it is unsupported by all major browsers and was masking the correct VP9 WebM fallback on Linux
- Cameras no longer render when nobody is watching — the lazy rendering guard is now applied before `SetCamerasEnabled`, not after. Previously the GPU rendered every frame regardless of active clients
- Snapshot polling interval raised to 10 s (was 2 s) and snapshot interest window reduced to 3 s, so cameras sleep between polls rather than staying hot continuously
- Heartbeat and server-side session management are now skipped entirely for remote (local-save) recordings


## v2.0.0-beta.1 Web UI Recording (Beta 1) - 2026-04-16

### Added

- Basic recording support in the web UI (experimental)
- Recording support in the C# API


## v1.0.0 - 2026-04-15

### Added

- Initial public release
