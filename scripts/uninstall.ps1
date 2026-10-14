# Removes Tidy installed by install.ps1. Ships inside the release zip.
# Tidy keeps its settings in %LOCALAPPDATA%\Tidy; they are removed too.

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\Tidy'

Get-Process Tidy -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir\*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500

Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Tidy.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tidy' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'Tidy') -Recurse -Force -ErrorAction SilentlyContinue

# This script runs from inside the folder it removes, so the folder goes after it exits.
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$installDir`"" -WindowStyle Hidden
Write-Host 'Tidy is uninstalled.'
