<#
.SYNOPSIS
    Runs the Pester suite for install.ps1 under Windows PowerShell 5.1 or PowerShell 7.

.DESCRIPTION
    Requires Pester 5. Exits with a non-zero code when a test fails or when no
    Pester 5 is available, so CI and local runs fail the same way.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Exit-WithMessage {
    param(
        [string] $Message,
        [int] $Code
    )

    # An exit code, not a throw: the GitHub 'powershell' shell dot-sources the
    # step and reports $LASTEXITCODE, which a terminating error leaves unset.
    $Host.UI.WriteErrorLine($Message)
    exit $Code
}

$pester = Get-Module -ListAvailable -Name 'Pester' |
    Sort-Object -Property Version -Descending |
    Select-Object -First 1

if ($null -eq $pester) {
    Exit-WithMessage -Code 2 -Message 'Pester is not installed. Run: Install-Module Pester -MinimumVersion 5.5.0 -Scope CurrentUser -SkipPublisherCheck'
}

if ($pester.Version.Major -lt 5) {
    Exit-WithMessage -Code 2 -Message "Pester $($pester.Version) is too old; this suite needs Pester 5. Run: Install-Module Pester -MinimumVersion 5.5.0 -Scope CurrentUser -SkipPublisherCheck"
}

# Windows ships Pester 3.4.0 under %WINDIR%, so import the resolved path rather
# than the module name.
Import-Module -Name $pester.Path -Force

Write-Host "Using Pester $($pester.Version) on PowerShell $($PSVersionTable.PSVersion)."

$configuration = New-PesterConfiguration
$configuration.Run.Path = Join-Path -Path $PSScriptRoot -ChildPath 'install.Tests.ps1'
$configuration.Run.PassThru = $true
$configuration.Output.Verbosity = 'Detailed'

$result = Invoke-Pester -Configuration $configuration

if ($result.FailedCount -gt 0) {
    Exit-WithMessage -Code 1 -Message "$($result.FailedCount) of $($result.TotalCount) tests failed."
}

Write-Host "$($result.PassedCount) tests passed."
exit 0
