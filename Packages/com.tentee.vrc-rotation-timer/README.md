# Rotation Alert

Rotation Alert is a synchronized timer and alert panel for rotation-based events in VRChat worlds,
such as meetups, exhibitions, and group conversations. It plays short local chimes for rotation start,
warning, and rotation end, and exposes schedule events and derived state for world-side integrations.

## Requirements

- Unity 2022.3
- VRChat SDK Worlds 3.x
- UdonSharp included with the VRChat Worlds SDK
- TextMeshPro Essential Resources

The package depends on `com.vrchat.worlds` `^3.8.0`.

## Install

1. Add the package through VCC using the TenteEEEE VPM listing.
2. Open the scene where the timer should be placed.
3. Run `Tools > TenteEEEE > Rotation Alert > Install Panel into Current Scene`.
4. The panel is placed at `(0, 1.3, 0)` unless an existing panel instance is found.
5. To add a read-only monitor, place `Prefabs/RotationAlert Display.prefab` and assign its
   `RotationAlertDisplay.Core` field to the panel's `RotationAlertCore`.

The package includes generated program assets and prefabs, so installation does not need to write to
Unity's immutable PackageCache. `Build Prefabs` is available under the same menu for a writable,
embedded package or the upstream `Assets/RotationAlert` authoring copy.

## Features

- Rotation, interval, count, and warning-minute controls.
- Start, pause/resume, extend, shorten, and two-step confirmation for skip and reset.
- Local-only audio with per-cue clips, volume, repeat count, and mute control. A dedicated cue
  plays when the whole schedule finishes.
- Listener events: `OnRotationStart`, `OnRotationWarning`, `OnRotationEnd`,
  `OnIntervalCountdown`, `OnAllFinished`, and `OnScheduleChanged`.
- Optional shader bridge values `_UdonRotAlertState` and `_UdonRotAlertFlags`.
- The control panel only responds to input once the local player is within about 1 m of it.

See [`docs/SPEC.md`](docs/SPEC.md) for the synchronization model and public component contract.

## License and credits

Code, generated UI art, and original audio are released under the MIT License in `LICENSE`.
The bundled Noto Sans JP font is provided under the SIL Open Font License 1.1; see
`Fonts/OFL-1.1.txt`. The audio generation credit is documented in the source README.
