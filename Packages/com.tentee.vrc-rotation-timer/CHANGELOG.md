# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2026-09-21

### Fixed

- Pause/resume could silently fail to engage in a live, already-running instance because "paused" was inferred from the sign of a synced server timestamp, which VRChat's wrapping server clock can make negative. Replaced with an explicit synced `paused` flag.

## [0.1.0] - 2026-09-20

### Added

- Initial VPM package release of Rotation Alert.
- Synchronized rotation timer, local cue audio, display prefab, and shader bridge.
- `Tools > TenteEEEE > Rotation Alert` editor menu grouping.
