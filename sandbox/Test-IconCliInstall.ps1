<#
.SYNOPSIS
    Verifies the published icon-cli install path on a clean Windows box with no .NET.

.DESCRIPTION
    Runs inside Windows Sandbox, started by sandbox\Invoke-SandboxTest.ps1 on the
    host. It walks an ordered table of named checks and records one result per
    check. A check is either an Assert, which fails the run when it throws, or an
    Observe, which only records what it found.

    Two things are proved here that cannot be proved on a developer machine: that
    icon-cli.exe really is self-contained, because the guest has no runtime to
    fall back on, and that the installer leaves no Mark of the Web on the exe it
    places.

    One thing is not proved here. SmartScreen's warning is a GUI gate that no
    script can observe, so the run stages a Mark of the Web copy on the desktop,
    records the machine-readable facts around it, and leaves the click to a human.

.PARAMETER Ref
    The git ref whose install.ps1 is fetched from raw.githubusercontent.com.

.PARAMETER Version
    The release tag to install, for example '0.1.0'. The latest release is used
    when this is omitted.

.PARAMETER ResultPath
    Folder the result files are written to. The default matches the read-write
    folder the host script maps into the sandbox.

.EXAMPLE
    powershell.exe -ExecutionPolicy Bypass -File C:\sandbox\payload\Test-IconCliInstall.ps1

.EXAMPLE
    powershell.exe -ExecutionPolicy Bypass -File C:\sandbox\payload\Test-IconCliInstall.ps1 -Ref main -Version 0.1.0
#>
[CmdletBinding()]
param(
    [string] $Ref = 'main',
    [string] $Version,
    [string] $ResultPath = 'C:\sandbox\results'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 streams the whole download through the progress bar,
# which costs more time than the download itself.
$ProgressPreference = 'SilentlyContinue'

# install.ps1 raises TLS 1.2 for itself, but the Invoke-RestMethod that fetches
# install.ps1 runs before any of its code does.
[System.Net.ServicePointManager]::SecurityProtocol =
    [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

$Repository = 'Dieses4lipp/icon-cli'
$ExeName = 'icon-cli.exe'
$InstallerName = 'icon-cli-installer.ps1'
$InstallDirectory = Join-Path -Path $env:LOCALAPPDATA -ChildPath 'Programs\icon-cli'
$ExePath = Join-Path -Path $InstallDirectory -ChildPath $ExeName
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\icon-cli'
$InstallScriptUrl = "https://raw.githubusercontent.com/$Repository/$Ref/install.ps1"

$IconRoot = Join-Path -Path $env:TEMP -ChildPath 'icon-cli-sandbox\icons'
$IconSetName = 'white'
$MotwCopyName = 'icon-cli-downloaded.exe'

# A 4x4 opaque PNG. The guest has no managed runtime other than the exe under
# test, so the source image for the convert check cannot be generated there.
$SamplePngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAySURBVBhXDcghAQAwDAPByomIKRmenMgpDq6r7sGRK53ZCyMoPQJGUDIBIyg1ASM9+wFmbysh8lzJFAAAAABJRU5ErkJggg=='

function Invoke-Executable {
    param(
        [string] $FilePath,
        [string[]] $Arguments
    )

    $outFile = [System.IO.Path]::GetTempFileName()
    $errFile = [System.IO.Path]::GetTempFileName()

    try {
        $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -NoNewWindow -Wait -PassThru `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile

        return [pscustomobject]@{
            ExitCode       = $process.ExitCode
            StandardOutput = (Get-Content -LiteralPath $outFile -Raw)
            StandardError  = (Get-Content -LiteralPath $errFile -Raw)
        }
    }
    finally {
        foreach ($file in @($outFile, $errFile)) {
            if (Test-Path -LiteralPath $file) {
                Remove-Item -LiteralPath $file -Force
            }
        }
    }
}

function Get-UserPathEntries {
    $value = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if ([string]::IsNullOrEmpty($value)) {
        return @()
    }

    return @($value -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-UserPathContains {
    param([string] $Directory)

    $wanted = $Directory.Trim().TrimEnd('\')
    foreach ($entry in Get-UserPathEntries) {
        if ([string]::Equals($entry.Trim().TrimEnd('\'), $wanted, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-RegistryReport {
    param([string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return "$Path does not exist"
    }

    $key = Get-Item -LiteralPath $Path
    if ($key.Property.Count -eq 0) {
        return "$Path exists with no values"
    }

    $lines = foreach ($name in $key.Property) {
        "  $name = $($key.GetValue($name))"
    }

    return @("$Path") + $lines
}

function Get-ZoneIdentifier {
    param([string] $Path)

    try {
        return (Get-Content -LiteralPath $Path -Stream 'Zone.Identifier' -ErrorAction Stop) -join '; '
    }
    catch {
        return "none ($($_.Exception.Message))"
    }
}

$checks = @(
    @{
        Name   = 'NoDotnetRuntime'
        Kind   = 'Assert'
        Action = {
            $command = Get-Command -Name 'dotnet' -ErrorAction SilentlyContinue
            $dotnetRoot = Join-Path -Path $env:ProgramFiles -ChildPath 'dotnet'
            $sharedRoots = @(
                (Join-Path -Path $env:ProgramFiles -ChildPath 'dotnet\shared\Microsoft.NETCore.App'),
                (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath 'dotnet\shared\Microsoft.NETCore.App')
            )
            $presentShared = @($sharedRoots | Where-Object { Test-Path -LiteralPath $_ })

            $found = @()
            if ($null -ne $command) { $found += "dotnet on PATH at $($command.Source)" }
            if (Test-Path -LiteralPath $dotnetRoot) { $found += "$dotnetRoot exists" }
            foreach ($shared in $presentShared) { $found += "$shared exists" }

            if ($found.Count -gt 0) {
                throw "The guest is not a clean box: $($found -join ', ')"
            }

            return @(
                'dotnet is not resolvable on PATH',
                "$dotnetRoot does not exist",
                'no Microsoft.NETCore.App shared framework directory'
            )
        }
    }
    @{
        Name   = 'WindowsBuild'
        Kind   = 'Observe'
        Action = {
            $os = Get-CimInstance -ClassName 'Win32_OperatingSystem'
            return @(
                "Caption: $($os.Caption)",
                "Version: $($os.Version)",
                "BuildNumber: $($os.BuildNumber)",
                "PowerShell: $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))",
                "ComputerName: $env:COMPUTERNAME",
                "UserName: $env:USERNAME"
            )
        }
    }
    @{
        Name   = 'InstallViaWebOneLiner'
        Kind   = 'Assert'
        Action = {
            $source = Invoke-RestMethod -Uri $InstallScriptUrl -Headers @{ 'User-Agent' = 'icon-cli-sandbox-test' }
            $installer = [scriptblock]::Create($source)

            # install.ps1 reports through Write-Host, which 5.1 routes to the
            # information stream, so 6>&1 is what captures the transcript.
            if ([string]::IsNullOrWhiteSpace($Version)) {
                $output = & $installer 6>&1 2>&1 | Out-String
            }
            else {
                $output = & $installer -Version $Version 6>&1 2>&1 | Out-String
            }

            return @("Fetched $InstallScriptUrl", 'Installer output:') + ($output.TrimEnd() -split "`r?`n")
        }
    }
    @{
        Name   = 'ExeInstalled'
        Kind   = 'Assert'
        Action = {
            if (-not (Test-Path -LiteralPath $ExePath)) {
                throw "$ExePath was not created."
            }

            $item = Get-Item -LiteralPath $ExePath
            $hash = (Get-FileHash -LiteralPath $ExePath -Algorithm SHA256).Hash.ToLowerInvariant()

            return @(
                "Path: $ExePath",
                "Length: $($item.Length) bytes",
                "SHA256: $hash"
            )
        }
    }
    @{
        Name   = 'UserPathEntry'
        Kind   = 'Assert'
        Action = {
            if (-not (Test-UserPathContains -Directory $InstallDirectory)) {
                throw "The User PATH does not contain $InstallDirectory. It holds: $((Get-UserPathEntries) -join ';')"
            }

            return "User PATH contains $InstallDirectory"
        }
    }
    @{
        Name   = 'UninstallEntry'
        Kind   = 'Assert'
        Action = {
            if (-not (Test-Path -LiteralPath $UninstallKey)) {
                throw "$UninstallKey does not exist."
            }

            $key = Get-Item -LiteralPath $UninstallKey
            $displayName = $key.GetValue('DisplayName')
            $uninstallString = $key.GetValue('UninstallString')

            if ($displayName -ne 'icon-cli') {
                throw "DisplayName is '$displayName', expected 'icon-cli'."
            }

            if ([string]::IsNullOrWhiteSpace($uninstallString)) {
                throw 'UninstallString is missing or empty.'
            }

            return @(
                "Key: $UninstallKey",
                "DisplayName: $displayName",
                "DisplayVersion: $($key.GetValue('DisplayVersion'))",
                "UninstallString: $uninstallString"
            )
        }
    }
    @{
        Name   = 'ExeRunsWithoutDotnet'
        Kind   = 'Assert'
        Action = {
            $run = Invoke-Executable -FilePath $ExePath -Arguments @('--help')

            $detail = @(
                "ExitCode: $($run.ExitCode)",
                'stdout:'
            ) + (("$($run.StandardOutput)".TrimEnd()) -split "`r?`n") + @('stderr:') + (("$($run.StandardError)".TrimEnd()) -split "`r?`n")

            if ($run.ExitCode -ne 0) {
                throw "icon-cli --help exited with $($run.ExitCode). stdout: $($run.StandardOutput) stderr: $($run.StandardError)"
            }

            if ("$($run.StandardOutput)" -notlike '*Usage: icon-cli*') {
                throw "icon-cli --help did not print its usage. stdout: $($run.StandardOutput)"
            }

            return $detail
        }
    }
    @{
        Name   = 'ExeDoesRealWork'
        Kind   = 'Assert'
        Action = {
            $setFolder = Join-Path -Path $IconRoot -ChildPath $IconSetName
            if (Test-Path -LiteralPath $IconRoot) {
                Remove-Item -LiteralPath $IconRoot -Recurse -Force
            }
            New-Item -ItemType Directory -Path $setFolder -Force | Out-Null

            $sourcePng = Join-Path -Path $setFolder -ChildPath 'sample.png'
            [System.IO.File]::WriteAllBytes($sourcePng, [System.Convert]::FromBase64String($SamplePngBase64))

            # The set name doubles as the tint, so 'white' needs no --color and the
            # run goes through the shading path rather than a raw copy.
            $env:ICON_CLI_ROOT = $IconRoot
            try {
                $run = Invoke-Executable -FilePath $ExePath -Arguments @('convert', $IconSetName)
            }
            finally {
                Remove-Item -Path 'Env:\ICON_CLI_ROOT' -Force
            }

            $icoPath = Join-Path -Path $setFolder -ChildPath 'sample.ico'

            # convert reports a per-image failure and still exits 0, so the .ico
            # itself is the assertion, not the exit code.
            if (-not (Test-Path -LiteralPath $icoPath)) {
                throw "convert produced no .ico. ExitCode $($run.ExitCode). stdout: $($run.StandardOutput) stderr: $($run.StandardError)"
            }

            $ico = Get-Item -LiteralPath $icoPath
            if ($ico.Length -le 0) {
                throw "$icoPath is empty."
            }

            return @(
                "ICON_CLI_ROOT: $IconRoot",
                "Source: $sourcePng ($((Get-Item -LiteralPath $sourcePng).Length) bytes)",
                "Output: $icoPath ($($ico.Length) bytes)",
                "ExitCode: $($run.ExitCode)",
                'stdout:'
            ) + (("$($run.StandardOutput)".TrimEnd()) -split "`r?`n")
        }
    }
    @{
        Name   = 'InstalledExeHasNoMotw'
        Kind   = 'Assert'
        Action = {
            # Invoke-WebRequest -OutFile writes the bytes itself rather than going
            # through the attachment service, so no Zone.Identifier is attached.
            try {
                $stream = Get-Item -LiteralPath $ExePath -Stream 'Zone.Identifier' -ErrorAction Stop
            }
            catch {
                return @(
                    "No Zone.Identifier stream on $ExePath",
                    "Error: $($_.Exception.Message)"
                )
            }

            throw "$ExePath carries a Mark of the Web: $((Get-Content -LiteralPath $ExePath -Stream $stream.Stream) -join '; ')"
        }
    }
    @{
        Name   = 'ExeIsUnsigned'
        Kind   = 'Observe'
        Action = {
            $signature = Get-AuthenticodeSignature -LiteralPath $ExePath

            return @(
                "Status: $($signature.Status)",
                "StatusMessage: $($signature.StatusMessage)",
                "SignerCertificate: $($signature.SignerCertificate)"
            )
        }
    }
    @{
        Name   = 'SmartScreenPolicy'
        Kind   = 'Observe'
        Action = {
            $machinePolicy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
            $explorer = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer'

            $smartScreenEnabled = 'SmartScreenEnabled is not set'
            if (Test-Path -LiteralPath $explorer) {
                $value = (Get-Item -LiteralPath $explorer).GetValue('SmartScreenEnabled')
                if ($null -ne $value) {
                    $smartScreenEnabled = "SmartScreenEnabled = $value"
                }
            }

            return @(Get-RegistryReport -Path $machinePolicy) +
                @("$explorer") +
                @("  $smartScreenEnabled")
        }
    }
    @{
        Name   = 'MotwCopyForSmartScreen'
        Kind   = 'Observe'
        Action = {
            $desktop = [System.Environment]::GetFolderPath('Desktop')
            $copyPath = Join-Path -Path $desktop -ChildPath $MotwCopyName
            Copy-Item -LiteralPath $ExePath -Destination $copyPath -Force

            $zone = @(
                '[ZoneTransfer]',
                'ZoneId=3',
                "HostUrl=https://github.com/$Repository/releases/latest/download/$ExeName"
            )
            Set-Content -LiteralPath $copyPath -Stream 'Zone.Identifier' -Value $zone -Encoding Ascii

            return @(
                "Staged: $copyPath",
                "Zone.Identifier: $(Get-ZoneIdentifier -Path $copyPath)",
                'Not launched. The SmartScreen prompt is a GUI gate, so the click is the human part of this run.'
            )
        }
    }
    @{
        Name   = 'Uninstall'
        Kind   = 'Assert'
        Action = {
            $uninstaller = Join-Path -Path $InstallDirectory -ChildPath $InstallerName
            if (-not (Test-Path -LiteralPath $uninstaller)) {
                throw "$uninstaller was not kept next to the exe."
            }

            $run = Invoke-Executable -FilePath 'powershell.exe' `
                -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$uninstaller`"", '-Uninstall')

            $leftovers = @()
            if (Test-Path -LiteralPath $InstallDirectory) { $leftovers += "$InstallDirectory still exists" }
            if (Test-UserPathContains -Directory $InstallDirectory) { $leftovers += 'the User PATH entry is still there' }
            if (Test-Path -LiteralPath $UninstallKey) { $leftovers += "$UninstallKey still exists" }

            if ($leftovers.Count -gt 0) {
                throw "The uninstall left things behind: $($leftovers -join ', '). stdout: $($run.StandardOutput) stderr: $($run.StandardError)"
            }

            return @(
                "Ran: $uninstaller -Uninstall",
                "ExitCode: $($run.ExitCode)",
                "$InstallDirectory is gone",
                'the User PATH entry is gone',
                "$UninstallKey is gone",
                'stdout:'
            ) + (("$($run.StandardOutput)".TrimEnd()) -split "`r?`n")
        }
    }
)

$results = New-Object System.Collections.Generic.List[object]

foreach ($check in $checks) {
    Write-Host "[$($check.Kind)] $($check.Name)"

    $outcome = 'Pass'
    $detail = ''

    try {
        $detail = @(& $check.Action) -join [System.Environment]::NewLine
        if ($check.Kind -eq 'Observe') {
            $outcome = 'Observe'
        }
    }
    catch {
        $detail = $_.Exception.Message
        if ($check.Kind -eq 'Observe') {
            $outcome = 'Observe'
        }
        else {
            $outcome = 'Fail'
        }
    }

    $results.Add([pscustomobject]@{
        Name    = $check.Name
        Kind    = $check.Kind
        Outcome = $outcome
        Detail  = $detail
    })

    foreach ($line in ($detail -split "`r?`n")) {
        Write-Host "    $line"
    }
}

if (-not (Test-Path -LiteralPath $ResultPath)) {
    New-Item -ItemType Directory -Path $ResultPath -Force | Out-Null
}

$failed = @($results | Where-Object { $_.Outcome -eq 'Fail' })

$report = [pscustomobject]@{
    Timestamp   = (Get-Date).ToString('o')
    Ref         = $Ref
    Version     = $Version
    Repository  = $Repository
    FailedCount = $failed.Count
    Results     = $results
}

$jsonPath = Join-Path -Path $ResultPath -ChildPath 'result.json'
$textPath = Join-Path -Path $ResultPath -ChildPath 'result.txt'

$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$textLines = New-Object System.Collections.Generic.List[string]
$textLines.Add('icon-cli Windows Sandbox verification')
$textLines.Add("Timestamp:   $($report.Timestamp)")
$textLines.Add("Ref:         $Ref")
$textLines.Add("Version:     $Version")
$textLines.Add("Failures:    $($failed.Count)")
$textLines.Add('')

foreach ($result in $results) {
    $textLines.Add("$($result.Outcome.ToUpperInvariant()) [$($result.Kind)] $($result.Name)")
    foreach ($line in ($result.Detail -split "`r?`n")) {
        $textLines.Add("    $line")
    }
    $textLines.Add('')
}

$textLines | Set-Content -LiteralPath $textPath -Encoding UTF8

Write-Host ''
Write-Host ($results | Select-Object -Property Name, Kind, Outcome | Format-Table -AutoSize | Out-String).TrimEnd()
Write-Host ''
Write-Host "Wrote $jsonPath"
Write-Host "Wrote $textPath"

Write-Host ''
Write-Host '--- the part no script can check ---'
Write-Host "A copy of the exe is on this sandbox's desktop as $MotwCopyName, carrying the"
Write-Host 'Zone.Identifier a browser download would attach (ZoneId=3).'
Write-Host ''
Write-Host '1. Double-click it on the desktop.'
Write-Host '2. Report whether a SmartScreen dialog titled "Windows protected your PC" appeared,'
Write-Host '   and if it did, whether running it needed "More info" and then "Run anyway".'
Write-Host '3. The installed exe has no Mark of the Web, so the installer path is expected to'
Write-Host '   show no such dialog. That is what the InstalledExeHasNoMotw check asserts.'
Write-Host ''

if ($failed.Count -gt 0) {
    $Host.UI.WriteErrorLine("$($failed.Count) of $($results.Count) checks failed: $(($failed | ForEach-Object { $_.Name }) -join ', ')")
    exit 1
}

Write-Host "All asserts passed. $(@($results | Where-Object { $_.Outcome -eq 'Observe' }).Count) observations recorded."
exit 0
