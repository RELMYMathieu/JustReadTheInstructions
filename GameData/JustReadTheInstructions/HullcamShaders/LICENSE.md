# HullcamShaders - license notice

`shaders.linux` in this folder is HullcamVDS's own shaders, not JRTI code, and is not covered by JRTI's MIT license.

It is a Unity asset bundle compiled from the shader sources of [HullcamVDSContinued](https://github.com/linuxgurugamer/HullcamVDSContinued) (`HullCameraAssets`), licensed under the GNU General Public License v3.0, and is distributed under the GPL-3.0 on the same terms. Its source is HullcamVDSContinued's repository.

Why it is here: HullcamVDS ships a `shaders.linux` built for a Unity target removed in Unity 2019.2, which renders black on modern Linux drivers (linuxgurugamer/HullcamVDSContinued#27). This copy is the same shaders compiled with Unity 2019.4.40f1 for `StandaloneLinux64` (SHA-256 `5840e0e8aeb70575f9adc3b2a2326944acb5a708a51797f5b4416e9f11f4d1ea`). On native Linux only, JRTI hands it to HullcamVDS before HullcamVDS loads its own. JRTI does not link it; it travels here as a separate work (GPL-3.0 "mere aggregation"). Delete this folder to go back to HullcamVDS's own bundle.

- Source: https://github.com/linuxgurugamer/HullcamVDSContinued
- License text: https://www.gnu.org/licenses/gpl-3.0.txt
