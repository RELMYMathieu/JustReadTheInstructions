# API

Everything the web UI does goes through a plain HTTP API, so you can build your own page, a phone app, a desktop app or an overlay on top of the same feeds. Any language with an HTTP client works.

## Start here

Open `http://localhost:8080/api/v1` (or your port). It answers with the mod version, the API version, what this game supports and every route with a one-line description:

```json
{
  "name": "Just Read The Instructions",
  "version": "3.3.0.0",
  "api": 1,
  "launchId": "…",
  "features": { "inGameRecording": true, "codecs": ["h264", "av1"], "controlKeyRequired": false },
  "routes": [
    { "id": "listCameras", "methods": ["GET"], "path": "/api/v1/cameras", "summary": "Cameras with their name, viewers, mic, pan and recording state", "writes": false }
  ]
}
```

The full description, with every request and response field, is an [OpenAPI 3.1 document](openapi.json). The game serves the same file at `/api/v1/openapi.json`. Feed it to a generator such as `openapi-generator` to get a typed client in Rust, Kotlin, Swift, TypeScript or most other languages.

## What you get

| Kind | Format | Example |
| --- | --- | --- |
| State | JSON | `GET /api/v1/cameras`, `GET /api/v1/camera/<id>/settings` |
| Changes | JSON body | `POST /api/v1/camera/<id>/settings` with `{"fov": 40}` |
| Live updates | Server-Sent Events | `GET /api/v1/events` (on-air and layout changes) |
| Video | MJPEG over HTTP | `GET /api/v1/camera/<id>/stream`, or one JPEG with `/snapshot` |
| Several feeds at once | One multipart stream | `GET /api/v1/streams?ids=10,11`, each part tagged with an `X-Camera-Id` header |
| Sound | WAV stream | `GET /api/v1/camera/<id>/audio` |

Each camera in `/cameras` carries its own addresses: `videoUrl` (the MJPEG stream), `previewUrl` (the same, smaller) and `snapshotUrl` (one JPEG). `streamUrl` is the viewer page meant for people, not the video.

### Good to know

* **Changing settings** answers with all the settings after the change. Values out of range are clamped (a brightness of 5 comes back as 1) and unknown fields are ignored, so read the answer to see what was applied. A field of view change shows from the next frame on.
* **Stopping a recording** (`POST /api/v1/camera/<id>/recording/stop`, or `/discard` to delete it) answers with the file name and frame counts, or `null` when nothing was recording. The file is finished a moment later and then shows in `/recordings` with its size.
* **Saved layouts** in `/layouts` are sorted by name, ignoring case. The web UI's `Shift` + `1` to `9` use this order, so a new layout can shift the numbers of the ones after it.
* **Events** from `/events` come in two kinds: `program`, with what is on air (sent once when you connect and on every change), and `layout`, with the `name` of a layout that was saved or deleted. A comment line every 15 seconds keeps the connection open.

### Errors

Errors come back with a standard HTTP status and a short plain-text message:

| Status | Means |
| --- | --- |
| `401` | A change needs the [control key](#control-key) and it is missing or wrong |
| `403` | The address uses a name the game does not answer to (see [Opening it by name](#opening-it-by-name)), on any route. On changes, it can also mean the request came from a web page on another website |
| `404` | Unknown route, camera or layout |
| `405` | Wrong method: the `Allow` header lists the right ones |

## Versions

Within `v1`, routes and fields are only added, never renamed or removed, so ignore fields you do not know. Anything that has to break goes to `/api/v2`, with `v1` kept next to it for a while.

The older short addresses without `/api/v1` (such as `http://localhost:8080/camera/<id>/stream` for OBS, or `/program/take/<name>` from a Stream Deck) keep working: they are the same routes, with the same rules.

## From another device

The API listens on your network like the web UI, with nothing to set up. To check a flight from your phone while you are away, use a private network such as [Tailscale](https://tailscale.com/) and open the computer's Tailscale address with the port.

!!! warning "Do not put it on the internet"
    Without a control key, anyone who can reach the port can watch your cameras, change their settings and start recordings. Forwarding the port on your router puts it on the whole internet, and so does **Tailscale Funnel**. Plain Tailscale, or `tailscale serve`, only reaches your own devices: use that instead.

### Control key

Watching is always open. To stop other devices from changing anything, tick **Require a key to control from other devices** in the settings window (see [Settings](settings.md#stream-capture)). From then on, changing camera settings, recording, saving or deleting layouts and switching what is on air need the key, except on the KSP computer itself when the page is opened as `localhost`, `127.0.0.1` or `[::1]`. Through its network address, or through a proxy such as `tailscale serve`, the KSP computer needs the key like any other device.

* In the web UI, open the link from **Copy link** once on each device, or type the key when the page asks. The browser remembers it.
* In your own app, send it as `Authorization: Bearer <key>`, or add `?key=<key>` to the address (handy for Stream Deck buttons).
* **New key** makes a fresh one, which logs every device out.

A request without the right key gets `401`. Routes that need the key are marked `writes: true` in `/api/v1` and carry a `security` entry in the OpenAPI document.

### What stays open

The key only protects changes. Anyone who can reach the port can still:

* watch and listen to every streaming camera,
* play and download your recordings,
* read the layouts, what is on air, the performance numbers and the computer's network addresses (from `/session`).

So keep the port on networks you trust, even with the key on.

Without the key, one more thing is possible: in an old browser, a page from another website could open `/program/take/<name>` or `/program/clear` and switch what is on air. Current Chrome, Edge, Firefox and Safari block this. Turn on the key if that matters to you.

### Opening it by name

The game answers to `localhost`, to any IP address, to names without a dot (such as the computer's own name, `my-pc`) and to names that only exist on a private network: ending in `.local`, `.lan`, `.home`, `.home.arpa`, `.internal`, `.localdomain` or Tailscale's `.ts.net`. Any other name gets `403`, so a website cannot point its own name at your computer to reach the feeds.

To use another name, for example one your router gives out such as `my-pc.fritz.box`, add it to `AllowedHosts` in `GameData/JustReadTheInstructions/PluginData/settings.cfg` and restart the game. Separate several names with commas:

```text
AllowedHosts = cams.example.org
```

## Building a web page

Web pages from other websites cannot change anything here (they get `403`), so a page you find online cannot start recordings or switch your layouts behind your back. Your own pages work from two places:

* **The Apps folder.** Put a folder in `GameData/JustReadTheInstructions/PluginData/Apps/`, for example `Apps/my-overlay/index.html`, and open `http://localhost:8080/apps/my-overlay/`. It is served by the game itself, so it reaches the API with no extra setup, from any device, and mod updates never touch it.
* **A development server on your computer.** Pages served from `localhost` or `127.0.0.1` on any port can call the API from the browser, so tools like Vite work while you build.

A page opened straight from a file (`file://`) cannot change anything: put it in the Apps folder instead.

!!! warning "Apps have full control"
    A page in the Apps folder is served by the game like the bundled web UI, so it can do everything the web UI does, including using the key a browser has remembered. Only add apps you trust, as you would a mod.

## Changing the web UI

To change one of the bundled pages, copy it from `GameData/JustReadTheInstructions/Web/` into `GameData/JustReadTheInstructions/PluginData/Web/` at the same place, and edit the copy. A file there replaces the shipped one, for example `PluginData/Web/css/styles.css` or `PluginData/Web/images/los.png`. Updates never overwrite it.

Your copy also stays as it is when the mod updates the original, so a copied page can stop working after an update. Delete the copy to go back to the shipped file.
