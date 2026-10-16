# Sound

Every camera has its own sound: the game's sounds as heard from where the camera is, mixed inside the game without changing what you hear.

## Turning it on

* **Browser:** press **Listen** (`M`) in the camera viewer or on the layout page. It plays in that browser tab only. The layout page plays the spotlight tile, or the tile picked with its speaker button, and saves that choice with the layout.
* **OBS:** add `&audio=1` to a viewer or clean feed URL and tick "Control audio via OBS" on the browser source. See [Layouts and OBS](layouts-obs.md#sound-in-obs).
* **Recordings:** [in-game recordings](recording.md) always include the camera's sound.

## Choosing a mic

Each camera card on the main page has a **Mic** picker in its bottom border, so you can set every camera's mic before recording without opening a viewer. **Adjust** in the viewer sets the **Mic**, **Gain**, **Auto gain** and **Mastering** under **Sound**, for every viewer and recording.

| Mic | What it hears |
| --- | --- |
| Game mix (default) | The game's sound from where the camera is, clean: no air delay, echo or hull muffling |
| External | Sound crossing the air: it arrives late from far away, changes pitch as things fly past, gets duller, hollower (ground echo) and grittier with distance and thinner air, and stops in vacuum except for the camera's own vessel through the hull |
| Onboard | Its own vessel through the structure and the cabin, other vessels muffled by the hull |

On External, sound is grittiest behind a vessel and thinner ahead of it.

### Big engines

An engine's sound reaches further the more thrust it is making, on every mic, so a pad camera still hears a heavy launch kilometres away while small engines fade as before. A part with several engines, like a Raptor cluster, counts all of them. With Rocket Sound Enhancement, whose engine sounds are already louder for more thrust, the extra reach is smaller so the total stays physical.

## Gain, auto gain and mastering

* **Gain** sets the camera's level.
* **Auto gain** (off by default) keeps a camera near a steady level: it slowly lifts quiet sound, up to +24 dB, and brings loud sound down, which leaves room for booms and ignitions to stand out. It lifts wind and background noise too.
* **Mastering** (off by default) compresses the camera like the game does with Rocket Sound Enhancement (its Limiter Amount from RSE's settings): denser and louder, with less difference between quiet and loud.

Each camera remembers its mic, gain, auto gain and mastering in `PluginData/camera-sound.cfg`. They come back when the camera opens again, after a revert, a quickload or relaunching the same craft. A second copy of a craft flying at the same time starts on the defaults.

## Rocket Sound Enhancement

JRTI works with **Rocket Sound Enhancement** (RSE):

* RSE's engine layers play on JRTI cameras without the player camera's Doppler.
* When **Harmony** is installed (RSE requires it), its ignition, flameout and decoupler sounds are heard too.
* Each RSE sound keeps the air treatment its config gives it: engines get echo and grit, while RCS, jetpacks and collisions only get duller with distance.
* Each vessel's re-entry roar follows its own heating, not the vessel you fly.

### Sonic booms

Sonic booms use RSE's sounds.

* A boom reaches a camera each time a vessel's speed toward that camera crosses the speed of sound, after the time the sound takes to get there. A flyby booms once, and a booster coming back (boostback, entry burn, descent, landing burn) can boom several times.
* Each is a double boom-boom from the nose and tail shocks, further apart the longer the vessel and the further the camera.
* Ahead of a supersonic vessel, its sound is a faint rumble. It comes back gradually after the cone passes, as in RSE.
* A camera on the vessel itself hears no boom: the shock trails behind the vessel and never reaches it.
* Booms follow RSE's own scaling (heavier vessels boom louder and deeper), hit hardest on External, and come through on Onboard from other vessels, a little quieter and duller than on External.
* RSE Default's far boom, the rumble meant for 0.5 to 5 km, plays on External and Onboard. RSE itself only ever plays the near one.

## Limits

A few sounds built into KSP, such as explosions, are kept compressed by Unity and cannot be read yet. They are listed in `KSP.log` and counted on the Diagnostics page.
