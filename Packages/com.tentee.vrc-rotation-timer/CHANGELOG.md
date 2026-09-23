# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-09-23

### Added

- A fifth audio cue (`AllFinished`) that plays when the whole schedule completes, reusing `rotation_end.wav` by default. `cueClips` / `cueVolumes` / `cueRepeats` grew from 3 to 5 entries (index 3, `Countdown`, is intentionally silent).
- Two-step confirmation for the skip control ("次へ" / "本当に？"), matching the existing reset confirmation: the first press arms a 4-second window, the second press skips.
- Proximity gating for the control panel: the `GraphicRaycaster` is only enabled while the local player is within `RotationAlertDisplay.interactRange` (default 1 m, with 0.5 m hysteresis), checked every 0.25 s.

### Changed

- UI images and TMP text now use dedicated generated materials (`Generated/RotationAlert UI Material.mat`, `Generated/RotationAlert TMP Material.mat`) with an explicit render queue and `ZTest LessEqual`, instead of relying on `Canvas.sortingOrder` (now `0`) to draw above other transparent world geometry.
- Default audio source volume raised from `0.6` to `1.0`.

## [0.1.1] - 2026-09-21

### Fixed

- Pause/resume could silently fail to engage in a live, already-running instance because "paused" was inferred from the sign of a synced server timestamp, which VRChat's wrapping server clock can make negative. Replaced with an explicit synced `paused` flag.

## [0.1.0] - 2026-09-20

### Added

- Initial VPM package release of Rotation Alert.
- Synchronized rotation timer, local cue audio, display prefab, and shader bridge.
- `Tools > TenteEEEE > Rotation Alert` editor menu grouping.
