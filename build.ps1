# Usage: .\build.ps1 [-Target build|test|run|publish] [-Configuration Debug|Release] [-Version 1.2.3]
#
# publish builds self-contained x64 and Arm64 apps into artifacts\, zips them as
# Tidy-win-<arch>.zip, and writes SHA256SUMS.txt. The release workflow runs exactly this.
param(
    [ValidateSet("build", "test", "run", "publish")] [string] $Target = "build",
    [string] $Configuration = "Debug",
    [string] $Version = "0.1.0"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }

function Invoke-Checked([scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

switch ($Target) {
    "build" { Invoke-Checked { dotnet build Tidy.slnx -c $Configuration -p:Platform=$hostArch } }
    "test" { Invoke-Checked { dotnet test --project tests/Tidy.Core.Tests -c $Configuration } }
    "run" {
        Invoke-Checked { dotnet build src/Tidy.App -c $Configuration -p:Platform=$hostArch }
        & "src/Tidy.App/bin/$hostArch/$Configuration/net10.0-windows10.0.19041.0/win-$hostArch/Tidy.exe"
    }
    "publish" {
        if (Test-Path artifacts) { Remove-Item artifacts -Recurse -Force }
        New-Item -ItemType Directory artifacts | Out-Null
        foreach ($arch in "x64", "arm64") {
            $platform = if ($arch -eq "arm64") { "ARM64" } else { "x64" }
            $out = "artifacts/Tidy-win-$arch"
            Invoke-Checked { dotnet publish src/Tidy.App -c Release -p:Platform=$platform -r "win-$arch" -p:Version=$Version -o $out }
            Copy-Item scripts/uninstall.ps1 $out
            Compress-Archive -Path "$out/*" -DestinationPath "artifacts/Tidy-win-$arch.zip" -CompressionLevel Optimal
        }
        Get-ChildItem artifacts -Filter *.zip | ForEach-Object {
            "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
        } | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
        Get-Content artifacts/SHA256SUMS.txt
    }
}
