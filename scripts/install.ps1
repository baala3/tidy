# Installs or updates Tidy for the current user. No admin rights needed.
#
#   irm https://baala3.github.io/tidy/install.ps1 | iex
#
# Downloads the latest release from GitHub, checks it against the published SHA-256,
# and installs to %LOCALAPPDATA%\Programs\Tidy with a Start menu shortcut and an entry
# in Settings > Apps, so it uninstalls like any other app.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'baala3/tidy'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Tidy'
$arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'arm64' } else { 'x64' }
$assetName = "Tidy-win-$arch.zip"

Write-Host "Finding the latest Tidy release..."
$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ 'User-Agent' = 'tidy-installer' }
$asset = $release.assets | Where-Object name -eq $assetName
$sums = $release.assets | Where-Object name -eq 'SHA256SUMS.txt'
if (-not $asset -or -not $sums) { throw "Release $($release.tag_name) has no $assetName. Download Tidy from https://github.com/$repo/releases instead." }

$temp = Join-Path ([IO.Path]::GetTempPath()) ("tidy-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = Join-Path $temp $assetName
    Write-Host "Downloading Tidy $($release.tag_name) for $arch..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing

    $expected = ((Invoke-RestMethod $sums.browser_download_url) -split "`n" |
        Where-Object { $_ -match "\s\*?$([regex]::Escape($assetName))\s*$" } |
        Select-Object -First 1) -replace '\s.*$', ''
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if (-not $expected -or $expected.Trim().ToUpperInvariant() -ne $actual) {
        throw "The download does not match the published SHA-256. Nothing was installed."
    }

    Get-Process Tidy -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir\*" } | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
    New-Item -ItemType Directory -Path $installDir | Out-Null
    Expand-Archive $zip -DestinationPath $installDir

    $exe = Join-Path $installDir 'Tidy.exe'
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Tidy.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = 'Tidy your PC. Safely.'
    $shortcut.Save()

    # Settings > Apps entry, so Tidy uninstalls like any other app.
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tidy'
    New-Item -Path $key -Force | Out-Null
    $version = $release.tag_name.TrimStart('v')
    $sizeKb = [int]((Get-ChildItem $installDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $values = @{
        DisplayName = 'Tidy'
        DisplayVersion = $version
        Publisher = 'baala3'
        DisplayIcon = $exe
        InstallLocation = $installDir
        URLInfoAbout = "https://github.com/$repo"
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$installDir\uninstall.ps1`""
    }
    foreach ($name in $values.Keys) { Set-ItemProperty -Path $key -Name $name -Value $values[$name] }
    Set-ItemProperty -Path $key -Name EstimatedSize -Value $sizeKb -Type DWord
    Set-ItemProperty -Path $key -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name NoRepair -Value 1 -Type DWord

    Write-Host "Tidy $version is installed. Find it in the Start menu." -ForegroundColor Green
    Start-Process $exe
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
