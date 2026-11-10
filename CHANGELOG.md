# Changelog

All notable changes to icon-cli. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

The release workflow publishes the section matching the pushed tag as the GitHub
release notes, so every tag needs a section here first.

## [0.1.1] - 2026-11-10

### Changed

- CLI output and the README are plain ASCII: em dashes and arrows replaced.
- The README leads with a quickstart and documents the SmartScreen prompt on
  browser downloads of the unsigned exe.

### Added

- CI runs the .NET tests and a Pester suite for `install.ps1` on every push, under
  both PowerShell 7 and Windows PowerShell 5.1.
- A Windows Sandbox harness that runs the published one-liner on a clean Windows
  with no .NET and checks the exe installs, runs, converts an image and uninstalls.

## [0.1.0] - 2026-10-25

First release.

### Added

- `icon-cli extract <set>` copies each desktop shortcut's icon, recolors it and
  saves it into the set. Modes `shade`, `ink` and `silhouette`, tuned with
  `--floor`, `--curve`, `--cut` and `--spread`.
- `icon-cli convert <set>` builds `.ico` files from images in the set folder.
- `icon-cli <set>` applies a set to matching `.lnk` and `.url` shortcuts, with
  `--only` and `--dry-run`.
- Sets named after a CSS color pick up that color. `ICON_CLI_ROOT` moves the icon
  library off the desktop.
- A self-contained `icon-cli.exe` with a one-line installer that needs no .NET and
  no administrator rights, and a dotnet tool package.

[0.1.1]: https://github.com/Dieses4lipp/icon-cli/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/Dieses4lipp/icon-cli/releases/tag/v0.1.0
