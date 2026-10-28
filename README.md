# icon-cli

[![release](https://img.shields.io/github/v/release/Dieses4lipp/icon-cli?sort=semver)](https://github.com/Dieses4lipp/icon-cli/releases/latest)

A simple tool for changing the color of your Desktop Icons

Icon sets live in an `Icons` folder on your desktop, or wherever `ICON_CLI_ROOT`
points. A set named
after a colour picks it up from its own name - `white`, `black`, `blue`, `teal`,
`crimson`, any CSS colour name. Anything else needs `--color`.

## Install

Windows only. No .NET install needed, no administrator rights:

```powershell
irm https://raw.githubusercontent.com/Dieses4lipp/icon-cli/main/install.ps1 | iex
```

That downloads the self-contained `icon-cli.exe` from the latest release, checks it
against the SHA256 published next to it, puts it in
`%LOCALAPPDATA%\Programs\icon-cli` and adds that folder to your User PATH. Open a
new terminal afterwards.

It needs no administrator rights and nothing goes into `Program Files`. Alongside
the exe it keeps a copy of itself as `icon-cli-installer.ps1` and registers
icon-cli under **Settings → Apps → Installed apps** for the current user only, so
it can be removed from there without the network. A specific release:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/Dieses4lipp/icon-cli/main/install.ps1))) -Version 0.1.0
```

Piping a script straight into `iex` runs whatever that URL serves, so read
[install.ps1](install.ps1) first if that bothers you — or download the `.exe` and
`.exe.sha256` from the [releases page](https://github.com/Dieses4lipp/icon-cli/releases/latest)
and compare the hash yourself with `Get-FileHash`.

### As a dotnet tool

With the .NET 10 SDK, from a clone:

```
dotnet pack -c Release
dotnet tool install -g icon-cli --add-source ./artifacts/nupkg
```

After changing the code, bump `<Version>` in `icon-cli.csproj`, pack again, and run
`dotnet tool update -g icon-cli --add-source ./artifacts/nupkg`.

## Uninstall

The one-liner install, from **Settings → Apps → Installed apps → icon-cli →
Uninstall**, or from a terminal:

```powershell
& "$env:LOCALAPPDATA\Programs\icon-cli\icon-cli-installer.ps1" -Uninstall
```

Either deletes `%LOCALAPPDATA%\Programs\icon-cli`, takes its PATH entry back out
and removes the Installed apps entry. Neither needs the network. If the folder is
already gone, the remote script does the same cleanup:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/Dieses4lipp/icon-cli/main/install.ps1))) -Uninstall
```

The dotnet tool install:

```
dotnet tool uninstall -g icon-cli
```

## Verifying the installer in Windows Sandbox

The claim that you need no .NET install is only worth as much as the box it was
tested on. [sandbox/](sandbox/README.md) holds a harness that runs the published
one-liner inside Windows Sandbox, on a clean Windows with no runtime, and asserts
the exe installs, runs, converts a real image and uninstalls again.

It also stages what no script can check, an unsigned copy carrying the Mark of
the Web a browser download attaches, so you can click it yourself and see what
SmartScreen says. Run it with `.\sandbox\Invoke-SandboxTest.ps1`.

## Commands

```
icon-cli <set-name>              apply the set's .ico files to matching desktop shortcuts
icon-cli <set-name> --dry-run    report what that would change, without writing anything
icon-cli <set-name> --only <n>   restrict the run to shortcuts whose name contains <n>
icon-cli extract <set-name>      pull each shortcut's current icon, recolour it, save it into the set
icon-cli convert <set-name>      build .ico files from .png/.jpg/.jpeg/.bmp already in the set folder
```

## extract parameters

Everything below applies to `extract`.

| Parameter | Default | What it does |
| --- | --- | --- |
| `--mode shade\|ink\|silhouette` | `shade` | `shade` maps the whole icon into a band of near-white greys, keeping its brightness order. `ink` erases the background and paints what is left one flat colour. `silhouette` fills the entire opaque area — line art only. |
| `--floor #RRGGBB` | 59% of the set colour's lightness | Dark end of the band; the set colour is the light end. A lower floor means more contrast and more visible detail. Derived in the set's own hue, so a blue set floors at a dark blue. `shade` only. |
| `--curve <n>` | `3` | Bends the mapping. `1` is a straight line. Above `1` pushes the middle towards white, which most app icons need — a large dark badge otherwise sits on the floor. `shade` only. |
| `--cut <pct>` | `0` | Drops the darkest `pct` of the range to transparent. Cuts by brightness, not by what is behind what, so on an icon with a light background it removes the logo instead. `shade` only. |
| `--spread` | off | Places each shade by how much of the icon is darker than it, so the band gets used evenly whatever the source looks like. `shade` only. |
| `--color #RRGGBB` | from set name | Set colour, needed when the folder name is not a colour. |
| `--only <name>` | all | Only shortcuts whose name contains this. |
| `--force` | off | Rebuild icons that already exist in the set. |
| `--refresh` | off | Re-read the icon from the shortcut instead of reusing the archived original. |

`convert` takes `--only`, `--color`, `--force`, and `--raw` to skip the tint and keep
the source colours. Applying a set takes `--only` and `--dry-run`.

Applying a set rewrites shortcuts and keeps no record of what it replaced, so preview
it first:

```
icon-cli white --dry-run
```

`--only` narrows a run to one shortcut, which pairs with `--dry-run` for a first pass:

```
icon-cli white --only Postman --dry-run
icon-cli white --only Postman
```

Originals are archived to `Icons\_originals` on first extract and reused afterwards, so a
set can be rebuilt at any time without touching the shortcuts.

## Examples

Build a set using the defaults, then apply it:

```
icon-cli extract white
icon-cli white
```

Rebuild an existing set after changing your mind about the look:

```
icon-cli extract white --floor #A0A0A0 --curve 2 --force
```

Try one icon before committing to the whole set:

```
icon-cli extract white --only Postman --force
```

Flatter, brighter icons with less internal shading:

```
icon-cli extract white --floor #C8C8C8 --curve 1 --force
```

Drop the dark background entirely, leaving the logo on transparency:

```
icon-cli extract white --cut 10 --force
```

The older look, background removed and everything else one flat colour:

```
icon-cli extract white --mode ink --force
```

## Colours other than white

`shade` mixes the two ends of the band in Oklab, so the hue holds all the way down
the ramp instead of drifting or dipping through the middle. Nothing extra to pass -
name the folder after the colour:

```
icon-cli extract blue
icon-cli extract teal
icon-cli extract crimson
```

A set whose name is not a colour:

```
icon-cli extract midnight --color #202020
```

Dark tints need a shallower band, or the shading disappears into the wallpaper. Set
the floor by hand when the automatic one is too dark:

```
icon-cli extract navy --color #001F5B --floor #7FA6E0 --curve 1.5
```
