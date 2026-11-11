# Installs the Panopticon CLI @@VERSION@@ from @@SERVER_DISPLAY@@ on Windows (spec cli-distribution, design D3).
#
#   irm @@SERVER_DISPLAY@@/install.ps1 | iex
#
# It installs to %LOCALAPPDATA%\Programs\panopticon and adds that directory to your user PATH. The server fills in this
# script for its own release: every download comes from that server.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $server = @@SERVER@@
    $url = @@WIN_URL@@
    $sha256 = @@WIN_SHA256@@

    function Fail([string] $message) {
        Write-Host "panopticon install: $message" -ForegroundColor Red
        throw "panopticon install: $message"
    }

    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') {
        Fail "unsupported platform: Windows $($env:PROCESSOR_ARCHITECTURE). The CLI runs on Windows x64, Linux (x64, arm64) and macOS (Apple Silicon)."
    }
    if (-not $url) {
        Fail "this server has no Windows build. It has CLI builds for: @@PLATFORM_LIST@@."
    }

    $dir = Join-Path $env:LOCALAPPDATA 'Programs\panopticon'
    $target = Join-Path $dir 'panopticon.exe'
    $tmp = Join-Path $dir ".panopticon-install-$PID.exe"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    try {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $tmp
        } catch {
            Fail "couldn't download ${url}: $($_.Exception.Message)"
        }

        $actual = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash.ToLowerInvariant()
        if ($actual -ne $sha256) {
            Fail "checksum mismatch for ${url}: expected $sha256, got $actual. Nothing was installed."
        }

        $installed = (& $tmp --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            Fail "the downloaded CLI doesn't run on this machine. Nothing was installed. It said: $installed"
        }

        # A running panopticon.exe can't be overwritten, but it can be renamed aside (design D6).
        if (Test-Path $target) {
            Move-Item -Force -Path $target -Destination "$target.old"
        }
        Move-Item -Force -Path $tmp -Destination $target
    } finally {
        if (Test-Path $tmp) { Remove-Item -Force $tmp }
    }

    Write-Host "Installed panopticon $installed to $target"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($userPath -split ';' | Where-Object { $_ })
    if ($entries -notcontains $dir) {
        [Environment]::SetEnvironmentVariable('Path', (($entries + $dir) -join ';'), 'User')
        $env:Path = "$env:Path;$dir"
        Write-Host "Added $dir to your user PATH. Open a new terminal for other windows to see it."
    }

    Write-Host ''
    Write-Host 'Next, log this machine in:'
    Write-Host "  panopticon login --server $server"
}
