Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

BeforeAll {
    . $PSScriptRoot/../install.ps1

    $installerSource = (Resolve-Path -LiteralPath (Join-Path -Path $PSScriptRoot -ChildPath '../install.ps1')).Path
}

Describe 'Get-Release' {
    It 'reads the latest release when no tag is given' {
        Mock Invoke-RestMethod { return @{ tag_name = 'v0.1.0' } }

        Get-Release | Out-Null

        Should -Invoke Invoke-RestMethod -Times 1 -Exactly -ParameterFilter {
            $Uri -eq 'https://api.github.com/repos/Dieses4lipp/icon-cli/releases/latest'
        }
    }

    It 'reads the tagged release for <Tag>' -ForEach @(
        @{ Tag = '0.1.0' }
        @{ Tag = 'v0.1.0' }
    ) {
        Mock Invoke-RestMethod { return @{ tag_name = 'v0.1.0' } }

        Get-Release -Tag $Tag | Out-Null

        Should -Invoke Invoke-RestMethod -Times 1 -Exactly -ParameterFilter {
            $Uri -eq 'https://api.github.com/repos/Dieses4lipp/icon-cli/releases/tags/v0.1.0'
        }
    }

    It 'names the URI when the request fails' {
        Mock Invoke-RestMethod { throw 'the remote server refused the connection' }

        { Get-Release } | Should -Throw -ExpectedMessage '*/releases/latest*'
    }
}

Describe 'Get-AssetUrl' {
    BeforeEach {
        $release = @{
            tag_name = 'v0.1.0'
            assets   = @(
                @{ name = 'icon-cli.exe'; browser_download_url = 'https://example.invalid/icon-cli.exe' }
                @{ name = 'icon-cli.exe.sha256'; browser_download_url = 'https://example.invalid/icon-cli.exe.sha256' }
            )
        }
    }

    It 'returns the download url of the matching asset' {
        Get-AssetUrl -Release $release -Name 'icon-cli.exe.sha256' |
            Should -Be 'https://example.invalid/icon-cli.exe.sha256'
    }

    It 'names the tag when no asset matches' {
        { Get-AssetUrl -Release $release -Name 'icon-cli.msi' } | Should -Throw -ExpectedMessage '*v0.1.0*'
    }
}

Describe 'Get-ExpectedHash' {
    It 'returns the bare lower-case digest' {
        Mock Invoke-RestMethod { return "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789  icon-cli.exe`n" }

        Get-ExpectedHash -Url 'https://example.invalid/icon-cli.exe.sha256' |
            Should -Be 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789'
    }

    It 'throws on checksum content <Content>' -ForEach @(
        @{ Content = 'not-a-hash  icon-cli.exe' }
        @{ Content = '' }
        @{ Content = 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef012345678  icon-cli.exe' }
    ) {
        Mock Invoke-RestMethod { return $Content }

        { Get-ExpectedHash -Url 'https://example.invalid/icon-cli.exe.sha256' } |
            Should -Throw -ExpectedMessage '*does not contain a SHA256 hash*'
    }
}

Describe 'Test-SamePath' {
    It 'treats a trailing backslash as the same path' {
        Test-SamePath -Left 'C:\tools\icon-cli\' -Right 'C:\tools\icon-cli' | Should -BeTrue
    }

    It 'ignores case' {
        Test-SamePath -Left 'C:\TOOLS\Icon-Cli' -Right 'c:\tools\icon-cli' | Should -BeTrue
    }

    It 'ignores surrounding whitespace' {
        Test-SamePath -Left '  C:\tools\icon-cli  ' -Right 'C:\tools\icon-cli' | Should -BeTrue
    }

    It 'separates sibling directories' {
        Test-SamePath -Left 'C:\tools\icon-cli' -Right 'C:\tools\icon-cli-old' | Should -BeFalse
    }
}

Describe 'Add-PathEntry' {
    It 'returns the directory alone for an empty list' {
        $result = Add-PathEntry -Entries @() -Directory 'C:\tools\icon-cli'

        $result.Count | Should -Be 1
        $result[0] | Should -Be 'C:\tools\icon-cli'
    }

    It 'returns null when the directory is present with a trailing backslash' {
        Add-PathEntry -Entries @('C:\a', 'C:\tools\icon-cli\') -Directory 'C:\tools\icon-cli' |
            Should -BeNullOrEmpty
    }

    It 'returns null when the directory is present in another case' {
        Add-PathEntry -Entries @('C:\TOOLS\ICON-CLI') -Directory 'C:\tools\icon-cli' |
            Should -BeNullOrEmpty
    }

    It 'appends the directory last' {
        $result = Add-PathEntry -Entries @('C:\a', 'C:\b') -Directory 'C:\tools\icon-cli'

        $result -join ';' | Should -Be 'C:\a;C:\b;C:\tools\icon-cli'
    }
}

Describe 'Remove-PathEntry' {
    It 'keeps the remaining entries in order' {
        $result = Remove-PathEntry -Entries @('C:\a', 'C:\TOOLS\icon-cli', 'C:\b') -Directory 'C:\tools\icon-cli\'

        $result -join ';' | Should -Be 'C:\a;C:\b'
    }

    It 'returns an empty list when the only entry is removed' {
        $result = Remove-PathEntry -Entries @('C:\tools\icon-cli') -Directory 'C:\tools\icon-cli'

        $null -eq $result | Should -BeFalse
        $result.Count | Should -Be 0
    }

    It 'returns null when the directory is absent' {
        $null -eq (Remove-PathEntry -Entries @('C:\a', 'C:\b') -Directory 'C:\tools\icon-cli') |
            Should -BeTrue
    }

    It 'returns null for an empty list' {
        $null -eq (Remove-PathEntry -Entries @() -Directory 'C:\tools\icon-cli') | Should -BeTrue
    }
}

Describe 'The Installed apps entry' {
    BeforeEach {
        $UninstallKey = 'HKCU:\Software\icon-cli-pester-' + [System.Guid]::NewGuid().ToString('n')
        $InstallDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('icon-cli-pester-' + [System.Guid]::NewGuid().ToString('n'))
        $exeByteCount = 3000

        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path -Path $InstallDirectory -ChildPath 'icon-cli.exe'), [byte[]]::new($exeByteCount))

        $uninstallerPath = Join-Path -Path $InstallDirectory -ChildPath 'icon-cli-installer.ps1'
    }

    AfterEach {
        if (Test-Path -LiteralPath $UninstallKey) {
            Remove-Item -LiteralPath $UninstallKey -Recurse -Force
        }

        if (Test-Path -LiteralPath $InstallDirectory) {
            Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        }
    }

    AfterAll {
        Get-ChildItem -Path 'HKCU:\Software' -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like 'icon-cli-pester-*' } |
            Remove-Item -Recurse -Force
    }

    It 'writes the values Add/Remove Programs reads' {
        Register-UninstallEntry -DisplayVersion '0.1.0' -UninstallerPath $uninstallerPath

        $values = Get-ItemProperty -LiteralPath $UninstallKey

        $values.DisplayName | Should -Be 'icon-cli'
        $values.DisplayVersion | Should -Be '0.1.0'
        $values.InstallLocation | Should -Be $InstallDirectory
        $values.DisplayIcon | Should -Be (Join-Path -Path $InstallDirectory -ChildPath 'icon-cli.exe')
        $values.UninstallString | Should -BeLike "*$uninstallerPath*"
        $values.UninstallString | Should -BeLike '*-Uninstall*'
        $values.EstimatedSize | Should -Be ([int][math]::Ceiling($exeByteCount / 1024))
        $values.NoModify | Should -Be 1
        $values.NoRepair | Should -Be 1
    }

    It 'removes the key again' {
        Register-UninstallEntry -DisplayVersion '0.1.0' -UninstallerPath $uninstallerPath

        Unregister-UninstallEntry

        Test-Path -LiteralPath $UninstallKey | Should -BeFalse
    }

    It 'succeeds when there is no key to remove' {
        { Unregister-UninstallEntry } | Should -Not -Throw
    }
}

Describe 'Invoke-Uninstall' {
    BeforeEach {
        Mock Remove-UserPathEntry { }

        $UninstallKey = 'HKCU:\Software\icon-cli-pester-' + [System.Guid]::NewGuid().ToString('n')
        $InstallDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('icon-cli-pester-' + [System.Guid]::NewGuid().ToString('n'))
    }

    AfterEach {
        if (Test-Path -LiteralPath $UninstallKey) {
            Remove-Item -LiteralPath $UninstallKey -Recurse -Force
        }

        if (Test-Path -LiteralPath $InstallDirectory) {
            Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        }
    }

    AfterAll {
        Get-ChildItem -Path 'HKCU:\Software' -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like 'icon-cli-pester-*' } |
            Remove-Item -Recurse -Force
    }

    It 'removes the folder, the key and the PATH entry' {
        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path -Path $InstallDirectory -ChildPath 'icon-cli.exe'), [byte[]]::new(16))
        Register-UninstallEntry -DisplayVersion '0.1.0' -UninstallerPath (Join-Path -Path $InstallDirectory -ChildPath 'icon-cli-installer.ps1')

        Invoke-Uninstall

        Test-Path -LiteralPath $InstallDirectory | Should -BeFalse
        Test-Path -LiteralPath $UninstallKey | Should -BeFalse
        Should -Invoke Remove-UserPathEntry -Times 1 -Exactly
    }

    # Add/Remove Programs can run the uninstaller twice.
    It 'succeeds when the install folder is already gone' {
        { Invoke-Uninstall } | Should -Not -Throw

        Should -Invoke Remove-UserPathEntry -Times 1 -Exactly
    }
}

Describe 'Save-LocalUninstaller' {
    BeforeEach {
        $InstallDirectory = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ('icon-cli-pester-' + [System.Guid]::NewGuid().ToString('n'))
        New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    }

    AfterEach {
        if (Test-Path -LiteralPath $InstallDirectory) {
            Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
        }
    }

    It 'keeps a byte-identical copy of the installer next to the exe' {
        $written = Save-LocalUninstaller

        $written | Should -Be (Join-Path -Path $InstallDirectory -ChildPath 'icon-cli-installer.ps1')
        (Get-FileHash -LiteralPath $written -Algorithm SHA256).Hash |
            Should -Be (Get-FileHash -LiteralPath $installerSource -Algorithm SHA256).Hash
    }
}
