<#
.SYNOPSIS
    Installs icon-cli for the current user, with no administrator rights.

.DESCRIPTION
    Downloads the self-contained icon-cli.exe from a GitHub release, verifies it
    against the SHA256 checksum published alongside it, places it in
    %LOCALAPPDATA%\Programs\icon-cli and adds that folder to the User PATH.

    It also keeps a copy of itself next to the exe as icon-cli-installer.ps1 and
    registers icon-cli under Settings, Apps, Installed apps, so it can be removed
    from there or offline.

    Nothing outside the current user's profile and HKCU is touched: no Machine
    PATH, no Program Files, no writes outside HKCU\Environment and the per-user
    Uninstall key.

.PARAMETER Version
    Install a specific release tag, for example '0.1.0' or 'v0.1.0'.
    The latest release is used when this is omitted.

.PARAMETER Uninstall
    Remove the install folder and its User PATH entry.

.EXAMPLE
    irm https://raw.githubusercontent.com/Dieses4lipp/icon-cli/main/install.ps1 | iex

.EXAMPLE
    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/Dieses4lipp/icon-cli/main/install.ps1))) -Uninstall
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 streams the whole download through the progress bar,
# which costs more time than the download itself.
$ProgressPreference = 'SilentlyContinue'

$Repository = 'Dieses4lipp/icon-cli'
$ExeName = 'icon-cli.exe'
$InstallDirectory = Join-Path -Path $env:LOCALAPPDATA -ChildPath 'Programs\icon-cli'
$InstallerName = 'icon-cli-installer.ps1'
$SourceUrl = "https://raw.githubusercontent.com/$Repository/main/install.ps1"

# Per-user Add/Remove Programs. HKCU only, so no administrator rights and no
# effect on other accounts.
$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\icon-cli'

function Initialize-Tls {
    # Windows PowerShell 5.1 on older builds still defaults to TLS 1.0, which
    # api.github.com refuses. PowerShell 7 negotiates on its own.
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        [System.Net.ServicePointManager]::SecurityProtocol =
            [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
    }
}

function Get-Release {
    param([string] $Tag)

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        $uri = "https://api.github.com/repos/$Repository/releases/latest"
    }
    else {
        if ($Tag.StartsWith('v')) {
            $normalized = $Tag
        }
        else {
            $normalized = "v$Tag"
        }

        $uri = "https://api.github.com/repos/$Repository/releases/tags/$normalized"
    }

    $headers = @{
        'Accept'     = 'application/vnd.github+json'
        'User-Agent' = 'icon-cli-installer'
    }

    try {
        return Invoke-RestMethod -Uri $uri -Headers $headers
    }
    catch {
        throw "Could not read the release from $uri - $($_.Exception.Message)"
    }
}

function Get-AssetUrl {
    param(
        [object] $Release,
        [string] $Name
    )

    $asset = @($Release.assets | Where-Object { $_.name -eq $Name }) | Select-Object -First 1
    if ($null -eq $asset) {
        throw "Release $($Release.tag_name) has no asset named '$Name'."
    }

    return $asset.browser_download_url
}

function Get-ExpectedHash {
    param([string] $Url)

    $headers = @{ 'User-Agent' = 'icon-cli-installer' }
    $content = Invoke-RestMethod -Uri $Url -Headers $headers

    # The checksum file holds '<sha256>  icon-cli.exe', sha256sum style.
    $token = ([string]$content).Trim() -split '\s+' | Select-Object -First 1
    if ($token -notmatch '^[0-9a-fA-F]{64}$') {
        throw "The checksum file at $Url does not contain a SHA256 hash."
    }

    return $token.ToLowerInvariant()
}

function Get-UserPathEntries {
    $value = [System.Environment]::GetEnvironmentVariable('Path', 'User')
    if ([string]::IsNullOrEmpty($value)) {
        return @()
    }

    return @($value -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-SamePath {
    param(
        [string] $Left,
        [string] $Right
    )

    $a = $Left.Trim().TrimEnd('\')
    $b = $Right.Trim().TrimEnd('\')

    return [string]::Equals($a, $b, [System.StringComparison]::OrdinalIgnoreCase)
}

function Add-PathEntry {
    param(
        [string[]] $Entries,
        [string] $Directory
    )

    foreach ($entry in $Entries) {
        if (Test-SamePath -Left $entry -Right $Directory) {
            return $null
        }
    }

    # The unary comma keeps the array intact. A bare array return unrolls, and a
    # single entry would come back as a string.
    return , @(@($Entries) + $Directory)
}

function Remove-PathEntry {
    param(
        [string[]] $Entries,
        [string] $Directory
    )

    $remaining = @($Entries | Where-Object { -not (Test-SamePath -Left $_ -Right $Directory) })
    if ($remaining.Count -eq @($Entries).Count) {
        return $null
    }

    # Without the unary comma an emptied list would unroll to $null, which is the
    # signal for 'the directory was not there'.
    return , $remaining
}

function Add-UserPathEntry {
    param([string] $Directory)

    $updated = Add-PathEntry -Entries (Get-UserPathEntries) -Directory $Directory
    if ($null -eq $updated) {
        Write-Host "PATH already contains $Directory."
        return
    }

    # 'User' writes HKCU\Environment only, and broadcasts WM_SETTINGCHANGE so
    # newly started processes pick the value up.
    [System.Environment]::SetEnvironmentVariable('Path', ($updated -join ';'), 'User')
    Write-Host "Added $Directory to the User PATH."
}

function Remove-UserPathEntry {
    param([string] $Directory)

    $remaining = Remove-PathEntry -Entries (Get-UserPathEntries) -Directory $Directory
    if ($null -eq $remaining) {
        Write-Host "PATH does not contain $Directory."
        return
    }

    [System.Environment]::SetEnvironmentVariable('Path', ($remaining -join ';'), 'User')
    Write-Host "Removed $Directory from the User PATH."
}

function Save-LocalUninstaller {
    $target = Join-Path -Path $InstallDirectory -ChildPath $InstallerName

    if (-not [string]::IsNullOrEmpty($PSCommandPath)) {
        # Run from a file: keep exactly the script that is running.
        Copy-Item -LiteralPath $PSCommandPath -Destination $target -Force
    }
    else {
        # Run through 'irm | iex' there is no file to copy, and $MyInvocation
        # reports the caller rather than the piped text, so fetch the source.
        Invoke-WebRequest -Uri $SourceUrl -OutFile $target -UseBasicParsing
    }

    Write-Host "Wrote $target."
    return $target
}

function Register-UninstallEntry {
    param(
        [string] $DisplayVersion,
        [string] $UninstallerPath
    )

    $exePath = Join-Path -Path $InstallDirectory -ChildPath $ExeName
    $sizeInKb = [int]([math]::Ceiling((Get-Item -LiteralPath $exePath).Length / 1024))
    $command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -Uninstall' -f $UninstallerPath

    if (-not (Test-Path -LiteralPath $UninstallKey)) {
        New-Item -Path $UninstallKey -Force | Out-Null
    }

    $values = [ordered]@{
        DisplayName          = 'icon-cli'
        DisplayVersion       = $DisplayVersion
        Publisher            = 'Dieses4lipp'
        DisplayIcon          = $exePath
        InstallLocation      = $InstallDirectory
        URLInfoAbout         = "https://github.com/$Repository"
        UninstallString      = $command
        QuietUninstallString = $command
    }

    foreach ($name in $values.Keys) {
        New-ItemProperty -Path $UninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null
    }

    foreach ($name in @('NoModify', 'NoRepair')) {
        New-ItemProperty -Path $UninstallKey -Name $name -Value 1 -PropertyType DWord -Force | Out-Null
    }

    New-ItemProperty -Path $UninstallKey -Name 'EstimatedSize' -Value $sizeInKb -PropertyType DWord -Force | Out-Null

    Write-Host 'Registered icon-cli under Settings, Apps, Installed apps.'
}

function Unregister-UninstallEntry {
    if (Test-Path -LiteralPath $UninstallKey) {
        Remove-Item -LiteralPath $UninstallKey -Recurse -Force
        Write-Host 'Removed the Installed apps entry.'
    }
    else {
        Write-Host 'No Installed apps entry to remove.'
    }
}

function Invoke-Uninstall {
    Unregister-UninstallEntry

    # Add/Remove Programs runs the copy inside the folder about to be deleted.
    # Nothing holds the script open, but the folder cannot be removed while it
    # is the working directory.
    $current = (Get-Location).Path
    if ($current.TrimEnd('\').StartsWith($InstallDirectory.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        Set-Location -LiteralPath ([System.IO.Path]::GetTempPath())
    }

    if (Test-Path -LiteralPath $InstallDirectory) {
        Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        Write-Host "Removed $InstallDirectory."
    }
    else {
        Write-Host "$InstallDirectory does not exist."
    }

    Remove-UserPathEntry -Directory $InstallDirectory
    Write-Host 'icon-cli uninstalled.'
}

function Invoke-Install {
    Initialize-Tls

    $release = Get-Release -Tag $Version
    Write-Host "Installing icon-cli $($release.tag_name)."

    $exeUrl = Get-AssetUrl -Release $release -Name $ExeName
    $hashUrl = Get-AssetUrl -Release $release -Name "$ExeName.sha256"
    $expectedHash = Get-ExpectedHash -Url $hashUrl

    # Download to a temporary file so a failed checksum never leaves a
    # half-trusted binary in the install folder.
    $temporaryName = 'icon-cli-' + [System.Guid]::NewGuid().ToString('n') + '.exe'
    $temporaryFile = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath $temporaryName

    try {
        Invoke-WebRequest -Uri $exeUrl -OutFile $temporaryFile -UseBasicParsing

        $actualHash = (Get-FileHash -LiteralPath $temporaryFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "Checksum mismatch for $ExeName. Expected $expectedHash, got $actualHash. The download was discarded."
        }

        Write-Host "SHA256 verified: $actualHash"

        if (-not (Test-Path -LiteralPath $InstallDirectory)) {
            New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        }

        $target = Join-Path -Path $InstallDirectory -ChildPath $ExeName
        Move-Item -LiteralPath $temporaryFile -Destination $target -Force
        Write-Host "Installed $target."
    }
    finally {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile -Force
        }
    }

    Add-UserPathEntry -Directory $InstallDirectory

    $uninstallerPath = Save-LocalUninstaller
    Register-UninstallEntry -DisplayVersion ($release.tag_name -replace '^v', '') -UninstallerPath $uninstallerPath

    # Make the command usable in this session too, without a restart.
    $onSessionPath = @($env:Path -split ';' | Where-Object { Test-SamePath -Left $_ -Right $InstallDirectory })
    if ($onSessionPath.Count -eq 0) {
        $env:Path = "$env:Path;$InstallDirectory"
    }

    Write-Host ''
    Write-Host 'Done. Run: icon-cli --help'
    Write-Host 'Open a new terminal if the command is not found in existing ones.'
    Write-Host "Remove it from Settings, Apps, Installed apps, or run: $uninstallerPath -Uninstall"
}

# Dot-sourcing loads the functions for the Pester suite, so it must not install.
# Every other route, including 'irm | iex', reports something other than '.'.
if ($MyInvocation.InvocationName -ne '.') {
    if ($Uninstall) {
        Invoke-Uninstall
    }
    else {
        Invoke-Install
    }
}
