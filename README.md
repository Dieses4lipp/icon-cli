# icon-cli

An simple tool for changing the color of your Desktop Icons

Icon sets live in an `Icons` folder on your desktop, or wherever `ICON_CLI_ROOT`
points. A set named
after a colour picks it up from its own name - `white`, `black`, `blue`, `teal`,
`crimson`, any CSS colour name. Anything else needs `--color`.

## Commands

```
icon-cli <set-name>              apply the set's .ico files to matching desktop shortcuts
icon-cli <set-name> --dry-run    report what that would change, without writing anything
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
the source colours. Applying a set takes `--dry-run`.

Applying a set rewrites shortcuts and keeps no record of what it replaced, so preview
it first:

```
icon-cli white --dry-run
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
