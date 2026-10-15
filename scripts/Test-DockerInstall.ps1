[CmdletBinding()]
param()

# Runs Install-EntityTracker.ps1 against a fake docker command: the messages when Docker or a step
# fails, the images, volumes and mounts of each container, and the install it leaves. CI's Linux job
# runs the real build in the real container.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-EntityTracker.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-docker-install-' + [guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $testRoot 'bin'
$source = Join-Path $testRoot 'source clone'
$localData = Join-Path $testRoot 'local app data'
$startMenu = Join-Path $testRoot 'start menu'
$programs = Join-Path $localData 'Programs'
$log = Join-Path $testRoot 'docker.log'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
New-Item -ItemType Directory -Path $fakeBin, (Join-Path $source '.git'), $localData | Out-Null
Set-Content -LiteralPath (Join-Path $source '.git\config') -Value @(
    '[core]', "`tbare = false",
    '[remote "origin"]', "`turl = git@github.com:Example/EntityTracker.git", "`tfetch = +refs/heads/*:refs/remotes/origin/*")

$hashes = @{ 'app-v1.2.3' = ('a' * 40); 'app-v1.9.9' = ('b' * 40); 'app-v1.10.0' = ('c' * 40) }

# The fake docker logs each call and behaves as the FAKE_DOCKER_* variables say. Containers are
# told apart by their ET_STEP variable.
Set-Content -LiteralPath (Join-Path $fakeBin 'fake-docker.ps1') -Value @'
$line = ($args | ForEach-Object { "[$_]" }) -join ' '
Add-Content -LiteralPath $env:FAKE_DOCKER_LOG -Value $line
function Get-ContainerVariable([string]$Name) {
    $prefix = "$Name="
    foreach ($item in $script:arguments) { if ($item.StartsWith($prefix)) { return $item.Substring($prefix.Length) } }
    return $null
}
$script:arguments = $args
switch ($args[0]) {
    'info' {
        if ($env:FAKE_DOCKER_DOWN) { [Console]::Error.WriteLine('Cannot connect to the Docker daemon'); exit 1 }
        Write-Output $env:FAKE_DOCKER_OSTYPE; exit 0
    }
    'image' { exit 1 }
    'pull' { [Console]::Error.WriteLine('Pulling fs layer'); exit 0 }
    'volume' { exit 0 }
    'run' {
        switch (Get-ContainerVariable 'ET_STEP') {
            'tags' {
                if ($env:FAKE_DOCKER_TAGS_FAIL) { [Console]::Error.WriteLine('fatal: could not read Username'); exit 128 }
                Write-Output ("$('a' * 40)`trefs/tags/app-v1.2.3")
                Write-Output ("$('c' * 40)`trefs/tags/app-v1.10.0")
                Write-Output ("$('b' * 40)`trefs/tags/app-v1.9.9")
                Write-Output ("$('d' * 40)`trefs/tags/app-v2.0.0-rc1")
                exit 0
            }
            'fetch' {
                if ($env:FAKE_DOCKER_FETCH_EXIT) { exit [int]$env:FAKE_DOCKER_FETCH_EXIT }
                Write-Output '{ "sdk": { "version": "10.0.400" } }'
                exit 0
            }
            'build' { [Console]::Error.WriteLine('Restoring packages'); exit [int]$env:FAKE_DOCKER_BUILD_EXIT }
            'save' {
                if ($env:FAKE_DOCKER_NO_SHARE) { exit 42 }
                $mount = $args | Where-Object { "$_".EndsWith(':/programs') } | Select-Object -First 1
                $folder = Join-Path $mount.Substring(0, $mount.Length - ':/programs'.Length) (Get-ContainerVariable 'ET_NEW')
                New-Item -ItemType Directory -Path $folder | Out-Null
                Set-Content -LiteralPath (Join-Path $folder 'EntityTracker.Wpf.exe') -Value 'app'
                [IO.File]::WriteAllBytes((Join-Path $folder 'app-install.json'),
                    [Convert]::FromBase64String((Get-ContainerVariable 'ET_MANIFEST')))
                exit 0
            }
        }
        exit 99
    }
}
exit 0
'@
Set-Content -LiteralPath (Join-Path $fakeBin 'docker.cmd') -Value '@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-docker.ps1" %*'

$fakeNames = @('FAKE_DOCKER_DOWN', 'FAKE_DOCKER_OSTYPE', 'FAKE_DOCKER_TAGS_FAIL', 'FAKE_DOCKER_FETCH_EXIT',
    'FAKE_DOCKER_BUILD_EXIT', 'FAKE_DOCKER_NO_SHARE')

function Invoke-Install {
    param([hashtable]$Fake = @{}, [string[]]$Arguments = @())
    foreach ($name in $fakeNames) { Set-Item "env:$name" -Value $(if ($Fake.ContainsKey($name)) { $Fake[$name] } else { '' }) }
    if (-not $Fake.ContainsKey('FAKE_DOCKER_OSTYPE')) { $env:FAKE_DOCKER_OSTYPE = 'linux' }
    if (-not $Fake.ContainsKey('FAKE_DOCKER_BUILD_EXIT')) { $env:FAKE_DOCKER_BUILD_EXIT = '0' }
    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    # Windows PowerShell, as the updater runs the installer. Its error output is expected in the
    # failing cases, so it must not stop this test.
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $installer -SourcePath $source `
            -LocalAppDataPath $localData -StartMenuPath $startMenu -NoStart @Arguments 2>&1
    }
    finally { $ErrorActionPreference = 'Stop' }
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = ($output | ForEach-Object { "$_" }) -join "`n"
        Calls = @(if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log })
    }
}

function Get-Run {
    param($Result, [string]$Step)
    $runs = @($Result.Calls | Where-Object { $_.StartsWith('[run]') -and $_.Contains("[ET_STEP=$Step]") })
    if ($runs.Count -ne 1) { throw "Expected one '$Step' container: $($Result.Calls -join '; ')" }
    return $runs[0]
}

function Assert-Contains {
    param([string]$Call, [string[]]$Parts)
    foreach ($part in $Parts) { if (-not $Call.Contains($part)) { throw "The container is missing $part`: $Call" } }
}

function Assert-CleanedUp {
    param($Result)
    $volume = [regex]::Match(($Result.Calls -join "`n"), 'entitytracker-build-[0-9a-f]{32}').Value
    if ($volume -and $Result.Calls -notcontains "[volume] [rm] [--force] [$volume]") {
        throw "The work volume $volume was not removed: $($Result.Calls -join '; ')"
    }
    if (Test-Path -LiteralPath $programs) {
        $left = @(Get-ChildItem -LiteralPath $programs -Directory | Where-Object Name -match '^EntityTracker\.(new|old)\.')
        if ($left.Count -gt 0) { throw "The install left $($left[0].Name) behind." }
    }
}

function Assert-Fails {
    param($Result, [string]$Expected)
    if ($Result.ExitCode -eq 0) { throw "The install should have failed with: $Expected" }
    if ($Result.Text -notlike "*$Expected*") { throw "Expected '$Expected' but got: $($Result.Text)" }
    Assert-CleanedUp $Result
}

$originalPath = $env:PATH
$env:FAKE_DOCKER_LOG = $log
try {
    # The image that fetches must be the one the repository builds with, so an install downloads
    # one image.
    $pinned = [regex]::Match((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install-Common.ps1') -Raw),
        "FetchSdkVersion = '([^']+)'").Groups[1].Value
    $repositorySdk = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($pinned -ne $repositorySdk) {
        throw "Install-Common.ps1 fetches with .NET SDK $pinned but global.json pins $repositorySdk. Update FetchSdkVersion to match."
    }

    # No docker at all. CI's Windows runners have a real docker on the system PATH, so this case
    # leaves nothing on PATH.
    $env:PATH = Join-Path $testRoot 'empty'
    Assert-Fails (Invoke-Install) 'Docker was not found'
    $env:PATH = "$fakeBin;$originalPath"

    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_DOWN = '1' }) 'Docker is not running'
    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_OSTYPE = 'windows' }) 'Switch it to Linux containers'
    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_TAGS_FAIL = '1' }) 'allows anonymous read access'
    Assert-Fails (Invoke-Install -Arguments @('-Tag', 'app-v3.0.0')) 'app-v3.0.0 was not found on origin'
    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_FETCH_EXIT = '42' }) 'differs from origin. Refusing to install.'
    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_BUILD_EXIT = '3' }) 'failed with exit code 3'
    Assert-Fails (Invoke-Install @{ FAKE_DOCKER_NO_SHARE = '1' }) 'Docker could not write to'

    # A previous install is replaced.
    New-Item -ItemType Directory -Path (Join-Path $programs 'EntityTracker') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $programs 'EntityTracker\previous.txt') -Value 'old'

    $installed = Invoke-Install
    if ($installed.ExitCode -ne 0) { throw "The install failed: $($installed.Text)" }
    $volume = [regex]::Match(($installed.Calls -join "`n"), 'entitytracker-build-[0-9a-f]{32}').Value
    $sdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0.400'
    # The SSH origin is read anonymously over HTTPS, and the newest stable tag is chosen.
    Assert-Contains (Get-Run $installed 'tags') @("[$sdkImage]", '[ET_ORIGIN=https://github.com/Example/EntityTracker.git]')
    Assert-Contains (Get-Run $installed 'fetch') @("[$sdkImage]", "[-v] [${volume}:/work]",
        '[ET_TAG=app-v1.10.0]', "[ET_HASH=$($hashes['app-v1.10.0'])]")
    Assert-Contains (Get-Run $installed 'build') @("[-v] [${volume}:/work]", '[-v] [entitytracker-nuget:/root/.nuget/packages]',
        '[-w] [/work/source]', "[$sdkImage] [pwsh]", '[scripts/Publish-Windows.ps1] [-Version] [1.10.0]')
    Assert-Contains (Get-Run $installed 'save') @("[-v] [${volume}:/work]", "[-v] [${programs}:/programs]", "[$sdkImage]")
    if ($installed.Calls -notcontains "[pull] [$sdkImage]") { throw "The SDK image from global.json was not pulled: $($installed.Calls -join '; ')" }
    Assert-CleanedUp $installed
    foreach ($step in 1..5) { if ($installed.Text -notlike "*[$step/5]*") { throw "The install did not show step $step`: $($installed.Text)" } }

    $installPath = Join-Path $programs 'EntityTracker'
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'EntityTracker.Wpf.exe'))) { throw 'The app was not installed.' }
    if (Test-Path -LiteralPath (Join-Path $installPath 'previous.txt')) { throw 'The previous install was not replaced.' }
    $manifest = Get-Content -LiteralPath (Join-Path $installPath 'app-install.json') -Raw | ConvertFrom-Json
    if ($manifest.Version -ne 'app-v1.10.0' -or $manifest.SourcePath -ne $source) {
        throw "The install manifest is wrong: $($manifest | ConvertTo-Json -Compress)"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $startMenu 'EntityTracker.lnk'))) { throw 'The Start Menu shortcut was not created.' }

    # An exact release can be chosen.
    $exact = Invoke-Install -Arguments @('-Tag', 'app-v1.2.3')
    if ($exact.ExitCode -ne 0) { throw "The install of app-v1.2.3 failed: $($exact.Text)" }
    Assert-Contains (Get-Run $exact 'fetch') @('[ET_TAG=app-v1.2.3]', "[ET_HASH=$($hashes['app-v1.2.3'])]")
    if ((Get-Content -LiteralPath (Join-Path $installPath 'app-install.json') -Raw | ConvertFrom-Json).Version -ne 'app-v1.2.3') {
        throw 'The exact release was not installed.'
    }

    # Everything comes from mcr.microsoft.com: the fetch and the build use one SDK image.
    $otherImages = @($installed.Calls | Where-Object { $_.StartsWith('[run]') -and -not $_.Contains("[$sdkImage]") })
    if ($otherImages.Count -gt 0) { throw "A container used another image than $sdkImage`: $($otherImages[0])" }
    Write-Host 'Docker install tests passed.'
    # The failing cases above leave a non-zero $LASTEXITCODE that would fail the CI step.
    exit 0
}
finally {
    $env:PATH = $originalPath
    foreach ($name in $fakeNames + 'FAKE_DOCKER_LOG') { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
