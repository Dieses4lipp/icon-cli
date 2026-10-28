<#
.SYNOPSIS
    Stages and launches the icon-cli installer verification run in Windows Sandbox.

.DESCRIPTION
    Writes artifacts\sandbox\icon-cli-sandbox.wsb for this machine, maps the
    guest script in read-only and a results folder in read-write, and starts
    Windows Sandbox on it. The guest window stays open after the run, because the
    SmartScreen part of the verification is a human click.

    Rerunning clears and rebuilds the whole staging folder, so a stale result
    file can never be mistaken for a fresh one.

    The .wsb is generated rather than committed: a MappedFolder needs an absolute
    HostFolder path, which differs per machine.

.PARAMETER Ref
    The git ref whose install.ps1 the guest fetches from raw.githubusercontent.com.

.PARAMETER Version
    The release tag to install, for example '0.1.0'. The latest release is used
    when this is omitted.

.PARAMETER Wait
    Wait for the guest to write results\result.json, then print it.

.EXAMPLE
    .\sandbox\Invoke-SandboxTest.ps1

.EXAMPLE
    .\sandbox\Invoke-SandboxTest.ps1 -Version 0.1.0 -Wait
#>
[CmdletBinding()]
param(
    [string] $Ref = 'main',
    [string] $Version,
    [switch] $Wait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$GuestScriptName = 'Test-IconCliInstall.ps1'
$GuestPayloadFolder = 'C:\sandbox\payload'
$GuestResultFolder = 'C:\sandbox\results'
$WaitTimeoutMinutes = 30

function Exit-WithMessage {
    param(
        [string] $Message,
        [int] $Code
    )

    # An exit code, not a throw: a caller that checks $LASTEXITCODE gets nothing
    # from a terminating error.
    $Host.UI.WriteErrorLine($Message)
    exit $Code
}

function ConvertTo-XmlText {
    param([string] $Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-SandboxCommand {
    $commandParts = @(
        'powershell.exe',
        '-NoExit',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        """$GuestPayloadFolder\$GuestScriptName""",
        '-Ref',
        $Ref,
        '-ResultPath',
        """$GuestResultFolder"""
    )

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $commandParts += @('-Version', $Version)
    }

    return $commandParts -join ' '
}

$sandboxExe = Join-Path -Path $env:WINDIR -ChildPath 'System32\WindowsSandbox.exe'
if (-not (Test-Path -LiteralPath $sandboxExe)) {
    $lines = @(
        "Windows Sandbox is not installed: $sandboxExe is missing.",
        '',
        'Enable it from an elevated prompt:',
        '    Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -NoRestart',
        '',
        'Then reboot. It also needs hardware virtualization enabled in firmware',
        '(Intel VT-x or AMD-V), or the feature installs and the sandbox refuses to start.'
    )

    Exit-WithMessage -Code 2 -Message ($lines -join [System.Environment]::NewLine)
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$guestScript = Join-Path -Path $PSScriptRoot -ChildPath $GuestScriptName
if (-not (Test-Path -LiteralPath $guestScript)) {
    Exit-WithMessage -Code 2 -Message "$guestScript is missing."
}

$stagingRoot = Join-Path -Path $repositoryRoot -ChildPath 'artifacts\sandbox'
$payloadFolder = Join-Path -Path $stagingRoot -ChildPath 'payload'
$resultFolder = Join-Path -Path $stagingRoot -ChildPath 'results'
$wsbPath = Join-Path -Path $stagingRoot -ChildPath 'icon-cli-sandbox.wsb'

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $payloadFolder -Force | Out-Null
New-Item -ItemType Directory -Path $resultFolder -Force | Out-Null
Copy-Item -LiteralPath $guestScript -Destination (Join-Path -Path $payloadFolder -ChildPath $GuestScriptName) -Force

$configuration = @"
<Configuration>
  <Networking>Default</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$(ConvertTo-XmlText -Value $payloadFolder)</HostFolder>
      <SandboxFolder>$(ConvertTo-XmlText -Value $GuestPayloadFolder)</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$(ConvertTo-XmlText -Value $resultFolder)</HostFolder>
      <SandboxFolder>$(ConvertTo-XmlText -Value $GuestResultFolder)</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>$(ConvertTo-XmlText -Value (Get-SandboxCommand))</Command>
  </LogonCommand>
</Configuration>
"@

# Windows Sandbox rejects a .wsb that starts with a byte order mark, and 5.1's
# -Encoding UTF8 always writes one.
[System.IO.File]::WriteAllText($wsbPath, $configuration, (New-Object System.Text.UTF8Encoding $false))

Write-Host "Wrote $wsbPath"
Write-Host "Payload: $payloadFolder"
Write-Host "Results: $resultFolder"
Write-Host ''

Start-Process -FilePath $sandboxExe -ArgumentList """$wsbPath"""

Write-Host 'Windows Sandbox is starting. The guest script runs at logon.'
Write-Host ''
Write-Host 'In the sandbox window:'
Write-Host '  1. Let the checks finish and read the summary table.'
Write-Host '  2. Double-click icon-cli-downloaded.exe on the sandbox desktop. That copy carries'
Write-Host '     the Zone.Identifier a browser download attaches, which the installer path does not.'
Write-Host '  3. Note whether a SmartScreen dialog titled "Windows protected your PC" appeared.'
Write-Host ''
Write-Host "result.json and result.txt land in $resultFolder on this machine."
Write-Host 'Closing the sandbox window discards the guest, so read the results first.'

if (-not $Wait) {
    exit 0
}

$resultFile = Join-Path -Path $resultFolder -ChildPath 'result.json'
$deadline = (Get-Date).AddMinutes($WaitTimeoutMinutes)

Write-Host ''
Write-Host "Waiting for $resultFile."

while (-not (Test-Path -LiteralPath $resultFile)) {
    if ((Get-Date) -gt $deadline) {
        Exit-WithMessage -Code 3 -Message "No result after $WaitTimeoutMinutes minutes. The sandbox may still be running."
    }

    Start-Sleep -Seconds 5
}

# The guest writes result.json in one pass, but the host can still see the file
# between create and flush.
Start-Sleep -Seconds 2

Write-Host ''
Get-Content -LiteralPath $resultFile -Raw | Write-Host
exit 0
