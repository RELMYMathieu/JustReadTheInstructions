# Contributing

Bug reports, suggestions and pull requests are welcome. If a change fits the mod's vision, it is very likely to be merged. Open an issue first for anything big.

* **Bugs:** open an issue with your `KSP.log` attached and prefix the title with `Bug:`.
* **Documentation:** the user docs are Markdown files in [`docs/`](docs/), published at <https://relmymathieu.github.io/JustReadTheInstructions/>. Edit them in the same pull request as the feature.

## Prerequisites

* Visual Studio 2022 (Windows), or the .NET SDK with your editor of choice
* Kerbal Space Program `1.12.x`
* HullcamVDS-Continued installed in that KSP install (see below)

### Getting HullcamVDS

HullcamVDS is declared as a dependency in the project, and the build compiles against the copy in your KSP `GameData`.

* **Linux / macOS:** if the [CKAN](https://github.com/KSP-CKAN/CKAN) command-line tool (`ckan`) is on your `PATH`, the build installs HullcamVDS into your KSP install for you.
* **Windows:** automatic install is turned off, because KSPBuildTools' CKAN step doesn't work under `cmd.exe` (it silently does nothing). Install HullcamVDS yourself first, through the CKAN app or manually.

If HullcamVDS is already in your `GameData`, it is used as-is on every platform.

## Setup

Create a `JustReadTheInstructions.csproj.user` file next to the `.csproj` and point it at your KSP install.

**Windows:**

```xml
<Project>
  <PropertyGroup>
    <KSPBT_GameRoot>C:\Your\KSP\Install</KSPBT_GameRoot>
  </PropertyGroup>
</Project>
```

**Linux / macOS (native KSP):**

```xml
<Project>
  <PropertyGroup>
    <KSPBT_GameRoot>/home/you/KSP</KSPBT_GameRoot>
  </PropertyGroup>
</Project>
```

`*.csproj.user` is gitignored and will never be committed.

**Running KSP through Proton on Linux?** KSPBuildTools expects a `KSP_Data` folder but the Windows/Proton build ships `KSP_x64_Data` instead. Create a symlink to fix this:

```bash
ln -s "/path/to/Kerbal Space Program/KSP_x64_Data" "/path/to/Kerbal Space Program/KSP_Data"
```

Replace `/path/to/Kerbal Space Program` with your actual Steam install path, for example `/home/you/.local/share/Steam/steamapps/common/Kerbal Space Program`.

## Building

```bash
dotnet build -c Release
```

The compiled DLL is written directly to `GameData/JustReadTheInstructions/Plugins/` (`Debug` builds also copy the `.pdb`).

The mod version lives in the `<Version>` property of `Source/JustReadTheInstructions.csproj`; the `create-release` workflow bumps it automatically. Building also regenerates `GameData/JustReadTheInstructions/JustReadTheInstructions.version` (the KSP-AVC version file) from it, so don't edit that file by hand.

To install, symlink or copy `GameData/JustReadTheInstructions/` into your KSP `GameData/`.

## Working on the web UI without KSP

The web files in `GameData/JustReadTheInstructions/Web/` are static and need no build. A mock server fakes the game's endpoints:

```bash
node Tools/mock-server.mjs
```

It serves the Web folder on port 8099. Keep it in step when you add an endpoint.

## Working on the docs

```bash
python -m pip install mkdocs-material
python -m mkdocs serve
```

The preview is at `http://127.0.0.1:8000/` and reloads as you edit.

The site is rebuilt and published by the `docs` workflow whenever `docs/` changes on `main`.

## Code style

* The code should read on its own. For things that are finicky, comments can be used.
* Keep it simple: DRY, KISS, SOLID, YAGNI. My CS teacher said so at least...
