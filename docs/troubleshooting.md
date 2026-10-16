# Troubleshooting

## Reporting a bug

Open an issue on [GitHub](https://github.com/RELMYMathieu/JustReadTheInstructions/issues) and attach `KSP.log`, which is in the KSP folder. Prefixing the title with `Bug:` helps with triage.

## The web UI does not open

JRTI's page is `http://localhost:8080/` unless you changed the port. If the page is missing or shows another program's page, the port may already be used by another program.

JRTI usually warns you about this on screen when the game opens, and writes it to `KSP.log`. To fix it, pick another **Port** in JRTI's settings (`Ctrl` + `Alt` + `F8`), then restart the game.

If it works on the KSP computer but not from another device, use the network address shown at the bottom of the web UI instead of `localhost`, and check that the computer's firewall lets the game accept connections. On Windows, allow `KSP_x64.exe` through Windows Defender Firewall (Windows Security, Firewall & network protection, Allow an app through firewall, Allow another app), making sure the rule covers your network's Private profile. Or open the port from an administrator command prompt, replacing `8080` with your port:

```text
netsh advfirewall firewall add rule name="JRTI" dir=in action=allow protocol=TCP localport=8080
```

## Record stays off

JRTI found no working video encoder. Open its settings window in the game (`Ctrl` + `Alt` + `F8`): it says why and which encoders were found. See [Recording](recording.md#ffmpeg) for installing ffmpeg, or [Linux and Proton](linux.md) on Linux.

## Frame rate drops

* Turn off integrations you do not need in the settings window, under **Visual Mod Integrations**. [Parallax](settings.md#visual-mods) is off by default because it is expensive.
* Open the performance overlay with `Ctrl` + `Alt` + `F6`, or the **Diagnostics** page in the web UI, to see per-camera frame times, GPU time and video memory.
* Lower Max FPS or the render size in the [settings window](settings.md#stream-capture). Each camera render costs about as much as the game's own view.
* Keep **Spread camera renders across frames** on, so cameras take turns instead of rendering together.

## Known issues

* **macOS is not fully supported.** A GPU async API used internally by this Unity version is unavailable on macOS, a legacy quirk inherited from KSP's Unity build. Streaming works through a synchronous readback that may stutter at higher framerates.
* **Performance with Parallax.** Enabling the Parallax integration may cause significant frame-rate drops.
