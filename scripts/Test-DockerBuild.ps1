[CmdletBinding()]
param()

# Checks Invoke-DockerBuild.ps1 against a fake docker command: the messages when Docker can't build,
# and the image and command it runs. CI's Linux job runs the real build in the real container.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Invoke-DockerBuild.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-docker-build-' + [guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $testRoot 'bin'
$stage = Join-Path $testRoot 'stage with spaces'
$log = Join-Path $testRoot 'docker.log'
New-Item -ItemType Directory -Path $fakeBin, $stage | Out-Null
Set-Content -LiteralPath (Join-Path $stage 'global.json') -Value '{ "sdk": { "version": "10.0.400" } }'

# The fake docker logs each call and behaves as the FAKE_DOCKER_* variables say.
Set-Content -LiteralPath (Join-Path $fakeBin 'fake-docker.ps1') -Value @'
$line = ($args | ForEach-Object { "[$_]" }) -join ' '
Add-Content -LiteralPath $env:FAKE_DOCKER_LOG -Value $line
switch ($args[0]) {
    'info' {
        if ($env:FAKE_DOCKER_DOWN) { [Console]::Error.WriteLine('Cannot connect to the Docker daemon'); exit 1 }
        Write-Output $env:FAKE_DOCKER_OSTYPE; exit 0
    }
    'image' { exit 1 }
    'pull' { [Console]::Error.WriteLine('Pulling fs layer'); exit 0 }
    'run' {
        $mount = $args[[array]::IndexOf($args, '-v') + 1]
        $folder = $mount.Substring(0, $mount.LastIndexOf(':/src'))
        if ($args -contains 'sh') {
            if (-not $env:FAKE_DOCKER_NO_SHARE) { Set-Content -LiteralPath (Join-Path $folder '.docker-share-check') -Value ok }
            exit 0
        }
        exit [int]$env:FAKE_DOCKER_BUILD_EXIT
    }
}
exit 0
'@
Set-Content -LiteralPath (Join-Path $fakeBin 'docker.cmd') -Value '@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-docker.ps1" %*'

function Invoke-Build {
    param([hashtable]$Fake)
    $names = @('FAKE_DOCKER_DOWN', 'FAKE_DOCKER_OSTYPE', 'FAKE_DOCKER_NO_SHARE', 'FAKE_DOCKER_BUILD_EXIT')
    foreach ($name in $names) { Set-Item "env:$name" -Value $(if ($Fake.ContainsKey($name)) { $Fake[$name] } else { '' }) }
    if (-not $Fake.ContainsKey('FAKE_DOCKER_OSTYPE')) { $env:FAKE_DOCKER_OSTYPE = 'linux' }
    if (-not $Fake.ContainsKey('FAKE_DOCKER_BUILD_EXIT')) { $env:FAKE_DOCKER_BUILD_EXIT = '0' }
    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
    # Windows PowerShell, as the updater runs the installer. Its error output is expected in the
    # failing cases, so it must not stop this test.
    $ErrorActionPreference = 'Continue'
    try { $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -SourcePath $stage -Version 1.2.3 2>&1 }
    finally { $ErrorActionPreference = 'Stop' }
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = ($output | ForEach-Object { "$_" }) -join "`n"
        Calls = @(if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log })
    }
}

function Assert-Fails {
    param($Result, [string]$Expected)
    if ($Result.ExitCode -eq 0) { throw "The Docker build should have failed with: $Expected" }
    if ($Result.Text -notlike "*$Expected*") { throw "Expected '$Expected' but got: $($Result.Text)" }
}

$originalPath = $env:PATH
$env:PATH = "$fakeBin;$env:PATH"
$env:FAKE_DOCKER_LOG = $log
try {
    Assert-Fails (Invoke-Build @{ FAKE_DOCKER_DOWN = '1' }) 'Docker is not running'
    Assert-Fails (Invoke-Build @{ FAKE_DOCKER_OSTYPE = 'windows' }) 'Switch it to Linux containers'
    Assert-Fails (Invoke-Build @{ FAKE_DOCKER_NO_SHARE = '1' }) 'Docker could not write to'
    Assert-Fails (Invoke-Build @{ FAKE_DOCKER_BUILD_EXIT = '3' }) 'failed with exit code 3'

    $built = Invoke-Build @{}
    if ($built.ExitCode -ne 0) { throw "The Docker build failed: $($built.Text)" }
    $image = 'mcr.microsoft.com/dotnet/sdk:10.0.400'
    if ($built.Calls -notcontains "[pull] [$image]") { throw "The SDK image from global.json was not pulled: $($built.Calls -join '; ')" }
    $build = @($built.Calls | Where-Object { $_ -like '*`[pwsh`]*' })
    if ($build.Count -ne 1) { throw "Expected one build run: $($built.Calls -join '; ')" }
    foreach ($part in @("[-v] [$([IO.Path]::GetFullPath($stage)):/src]", '[-v] [entitytracker-nuget:/root/.nuget/packages]',
                        '[-w] [/src]', "[$image] [pwsh]", '[scripts/Publish-Windows.ps1] [-Version] [1.2.3]')) {
        if (-not $build[0].Contains($part)) { throw "The build run is missing $part`: $($build[0])" }
    }
    if (Test-Path -LiteralPath (Join-Path $stage '.docker-share-check')) { throw 'The share check file was left behind.' }

    Write-Host 'Docker build tests passed.'
    # The failing cases above leave a non-zero $LASTEXITCODE that would fail the CI step.
    exit 0
}
finally {
    $env:PATH = $originalPath
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
