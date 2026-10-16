# Layouts and OBS

!!! warning "Work in progress"
    Layouts and live switching are still a work in progress. Expect rough edges and changes between versions, and report anything odd on [GitHub](https://github.com/RELMYMathieu/JustReadTheInstructions/issues).

## Layouts

The **Layout** button in the web UI opens `layout.html`: several cameras in one window, sized to fill it.

* Open **Cameras** (or press `C`) to add cameras. Click one to add it, drag it onto a tile to replace that feed, or use **Add all**.
* Drag a tile onto another to swap them.
* Double-click a tile (or press `1` to `9`) to **spotlight** it: one tile large with the others beside it.
* **Fill** removes the gaps and black bars and crops each feed to its tile, for edge to edge split screens.
* **Columns** (`L`) fixes how many tiles sit side by side instead of letting the page pick the biggest tiles.
* Removing or swapping tiles offers **Undo**, and `Ctrl` + `Z` undoes any change. Press `?` for every shortcut.

### Keeping a layout

| Where | How it works |
| --- | --- |
| This browser | The default. Remembered by camera name in this browser, so it comes back on your next flight |
| Saved | The menu next to the layout's name saves it in the game (`PluginData/Layouts/`) under a name |
| Link | **Copy link** on an unsaved layout gives an address like `http://localhost:8080/layout.html?cams=10,11,12` that always shows those camera IDs |

A saved layout opens anywhere with `http://localhost:8080/layout.html?layout=Launch`. Every screen showing it follows changes made from any other screen, so a phone can rearrange what an OBS browser source shows while you fly.

## OBS

* **A single camera:** add a browser source pointed at `http://localhost:8080/viewer.html?id=<id>`, or use the raw MJPEG URL from [Cameras](cameras.md#viewing-a-feed).
* **A saved layout:** point a browser source at `layout.html?layout=YourName` once, then rearrange it from any browser.
* **Live shows:** use the clean feed below.

### Clean feed and live switching

```text
http://localhost:8080/layout.html?program
```

The clean feed never shows controls or a cursor, and displays whichever saved layout is **on air**. Add it once as an OBS browser source.

To switch what is on air:

* On the layout page, press **Take on air**.
* Press `Shift` + `1` to `9` to take the first nine saved layouts, in the order the layout menu lists them.
* Open a URL, for example from a Stream Deck button:

```text
http://localhost:8080/program/take/Launch
http://localhost:8080/program/clear
```

`/program/clear` puts nothing on air, which shows black.

The clean feed switches at once: cameras that stay glide to their new place, new ones fade in, removed ones fade out. The on-air layout is remembered by the game (`PluginData/program.txt`).

### Sound in OBS

Add `&audio=1` to a viewer or clean feed URL and tick "Control audio via OBS" on the browser source:

```text
http://localhost:8080/layout.html?program&audio=1
```

The clean feed plays the sound of the layout on air and crossfades on Take. See [Sound](sound.md).

### Split screens

For a seamless split screen, such as two cameras side by side on a second monitor, turn on **Fill**, then **Fullscreen**. To avoid the crop, set JRTI's Render Width and Height to the tile size, for example 960 x 1080 for two halves of a 1920 x 1080 screen. This applies to every camera.
