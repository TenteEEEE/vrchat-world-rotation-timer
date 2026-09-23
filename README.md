# Rotation Alert

This repository publishes the `com.tentee.vrc-rotation-timer` VPM package for VRChat worlds.

The package is under `Packages/com.tentee.vrc-rotation-timer/`. The repository is based on the
official VRChat VPM package template; the release workflow builds the VPM zip and legacy
UnityPackage from that directory.

## Development

1. Open this repository as a Unity 2022.3 project.
2. Install the VRChat Worlds SDK and TextMeshPro Essential Resources.
3. Work from `Packages/com.tentee.vrc-rotation-timer/`.

## Releasing

1. Bump `package.json`, then merge the change.
2. Publish a GitHub release with the tag `<version>`; the workflow attaches the package files automatically.
3. Or run **Build Release** manually from `main` (or from a version tag to backfill an older release).
4. Set `dry_run` to build and verify the files without publishing.
5. `VPM_REPOS_TOKEN` is optional and lets the workflow trigger the VPM listing rebuild.
6. Without it, run **Build Repo Listing** manually in the `TenteEEEE/vpm-repos` Actions tab.

The package's user documentation is in its `README.md` and `README.ja.md`.
