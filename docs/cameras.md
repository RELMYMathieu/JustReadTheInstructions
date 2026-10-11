# Cameras

## Viewing a feed

Every camera has a card on the main page. Open one to get the full-screen viewer, or grab its raw MJPEG feed for OBS or other tools:

```text
http://localhost:8080/camera/<id>/stream
```

**Adjust** in the viewer sets brightness, contrast, gamma and field of view. These are applied on the game side, so every viewer, stream and recording shows the same image.

Every camera can zoom in to 0.25° from the viewer's field of view slider, or by holding `+` and `-`.

## Naming and IDs

Right-click a camera part in the VAB / SPH to open the **JRTI** group, where you can **Set Name** and **Set ID**. Both are saved in the craft file, so no external config is needed.

The ID is what appears in the camera's stream URL, so giving a camera a fixed ID lets you point OBS (or any tool) at the same URL every time you fly that craft.

How IDs resolve at runtime:

* **Leave the ID blank (or 0) to auto-assign** the lowest free number, starting at `1`.
* **IDs are unique among cameras loaded at the same time.** They key the live stream endpoints, so two active cameras can never share one number.
* **Collisions are resolved automatically.** If two cameras would claim the same ID simultaneously, for example two separate craft both set to ID `1` while within physics range, the first one to load keeps it and the others are bumped to the next free number. Their feeds still work; only the numeric ID shifts.
* **IDs are scoped to a flight session.** They are freed when a craft is recovered or unloaded, and reset when you re-enter the flight scene, so relaunching a single craft reliably restores its chosen IDs.

## Pan and tracking

Two HullcamVDS cameras have a head that turns, and JRTI can steer it:

| Camera | Pan | Tilt |
| --- | --- | --- |
| TurretCam | All the way round | -45° to 90° (the view tilts, the head stays level) |
| LaunchCam | All the way round | -60° to 90° (straight up) |

On these cameras, **Pan** (`P`) in the camera viewer opens the controls: hold an arrow button or the arrow keys to turn, **Center** to point straight ahead.

**Track** follows the active vessel or your target and zooms with its distance. Zooming while tracking sets how tight it frames. Panning by hand stops tracking. The same Track choices are in the **Pan** menu on each camera card.

The head turns in the game too, so every viewer and recording sees the same shot.

Other cameras have no moving head, so pan and tracking are limited to these two for now.
