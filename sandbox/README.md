# Windows Sandbox verification

Two things about the published installer cannot be checked on a development
machine. This harness checks them on a throwaway Windows box.

1. **`icon-cli.exe` really is self-contained.** The sandbox guest has no .NET
   installed, so an exe that needs a runtime cannot start there at all. The run
   asserts that no runtime is present, then runs `icon-cli --help` and a real
   `icon-cli convert`, which pulls `System.Drawing.Common`'s native bits out of
   the single-file bundle.
2. **What SmartScreen does with the unsigned exe.** `install.ps1` downloads with
   `Invoke-WebRequest -OutFile`, which writes the bytes itself and attaches no
   Mark of the Web, so the installed exe is expected to carry no
   `Zone.Identifier` and to raise no SmartScreen prompt. A browser download does
   attach `ZoneId=3`, and that is the path where SmartScreen fires. The run
   asserts the absence on the installer path and stages a synthetic `ZoneId=3`
   copy on the sandbox desktop for the browser path. Both halves are answered;
   the verdict is at the end of this file.

## Run it

```powershell
.\sandbox\Invoke-SandboxTest.ps1
.\sandbox\Invoke-SandboxTest.ps1 -Version 0.1.0
.\sandbox\Invoke-SandboxTest.ps1 -Ref my-branch -Wait
```

`-Ref` picks which `install.ps1` the guest fetches from
`raw.githubusercontent.com`, `-Version` picks the release tag, and `-Wait` blocks
until the guest has written `result.json` and then prints it.

The host script stages `artifacts\sandbox\payload`, generates
`artifacts\sandbox\icon-cli-sandbox.wsb` and launches it. The `.wsb` is generated
rather than committed because a mapped folder needs an absolute host path.
Rerunning clears the staging folder first.

## One-time setup

Windows Sandbox is an optional feature. From an elevated prompt:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -NoRestart
```

Then reboot. It also needs hardware virtualization enabled in firmware. The host
script checks for `WindowsSandbox.exe` and prints this command rather than
failing with something obscure.

## Results

`artifacts\sandbox\results\result.json` and `result.txt`, written by the guest
into a read-write mapped folder, so they survive the sandbox being closed.
`result.txt` is the log: every check records what it found, including the
installer's own output, the exe's SHA256, the `--help` and `convert` output, the
Authenticode status and the SmartScreen policy values that were in effect.

Each check is either an `Assert`, which fails the run, or an `Observe`, which
only records. The guest exits non-zero if any assert failed.

## The part only a human can judge

SmartScreen's warning is a GUI gate. No script can assert on it. The guest stages
`icon-cli-downloaded.exe` on the sandbox desktop with a `ZoneId=3`
`Zone.Identifier` and does not launch it. The window stays open afterwards so you
can double-click that copy yourself and see whether a **Windows protected your
PC** dialog appears.

Windows Sandbox resets SmartScreen and Defender state with every session, so it
has no local reputation history and no record of this exe. Its verdict is a
reasonable proxy for a real user's first run, not an identical one.

Note that the run's last check uninstalls, so by the time the guest gives you a
prompt, `icon-cli` is no longer on the guest's PATH. The desktop copy is a
separate file the guest wrote itself, which the uninstall does not touch, and it
still runs from its own path. Launching it from a shell rather than Explorer
skips the gate, because the gate is the Explorer shell's.

### Verdict, 2026-10-29

Run against `main` at version 0.1.0, 9 of 9 asserts passed, 0 failures.

SmartScreen fires on the browser-download path. The dialog is **Windows
protected your PC**, Defender SmartScreen prevented an unknown app from
starting. Its default button is **Don't run**; **More info** expands to reveal
**Run anyway**. So that user needs two extra clicks, and the `install.ps1` path
has no gate at all, which the `InstalledExeHasNoMotw` assert covers.

That verdict is the default behaviour, not an artefact of the guest:
`SmartScreenEnabled` is not set in HKCU and no SmartScreen policy values are
present under HKLM, both of which `SmartScreenPolicy` records.

Only an Authenticode signature removes the dialog, immediately with an EV
certificate and after reputation builds with an OV one. Until that is worth a
hardware token, `install.ps1` stays the documented install route.
