# Installs or updates Tidy for the current user. No admin rights needed.
#
#   irm https://baala3.github.io/tidy/install.ps1 | iex
#
# Downloads the latest installer from GitHub Releases, checks it against the published
# SHA-256, and runs it silently. Tidy lands in %LOCALAPPDATA%\Programs\Tidy with a Start
# menu shortcut, and uninstalls from Settings > Apps like any other app.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'baala3/tidy'
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$assetName = "Tidy-Setup-$arch.exe"

Write-Host 'Finding the latest Tidy release...'
$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'tidy-installer' }
$asset = $release.assets | Where-Object name -eq $assetName
$sums = $release.assets | Where-Object name -eq 'SHA256SUMS.txt'
if (-not $asset -or -not $sums) { throw "Release $($release.tag_name) has no $assetName. Download Tidy from https://github.com/$repo/releases instead." }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('tidy-install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $setup = Join-Path $temp $assetName
    Write-Host "Downloading Tidy $($release.tag_name) for $arch..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $setup -UseBasicParsing

    $expected = ((Invoke-RestMethod $sums.browser_download_url) -split "`n" |
        Where-Object { $_ -match "\s\*?$([regex]::Escape($assetName))\s*$" } |
        Select-Object -First 1) -replace '\s.*$', ''
    $actual = (Get-FileHash $setup -Algorithm SHA256).Hash
    if (-not $expected -or $expected.Trim().ToUpperInvariant() -ne $actual) {
        throw 'The download does not match the published SHA-256. Nothing was installed.'
    }

    Write-Host 'Installing...'
    $process = Start-Process $setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "The installer stopped with code $($process.ExitCode)." }

    $exe = Join-Path $env:LOCALAPPDATA 'Programs\Tidy\Tidy.exe'
    Write-Host "Tidy $($release.tag_name.TrimStart('v')) is installed. Find it in the Start menu." -ForegroundColor Green
    if (Test-Path $exe) { Start-Process $exe }
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
