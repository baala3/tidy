# Usage: .\build.ps1 [-Target build|test|run|publish] [-Configuration Debug|Release] [-Version 1.2.3]
#
# publish builds self-contained x64 and Arm64 apps into artifacts\, packs each as a
# portable Tidy-win-<arch>.zip and a per-user Tidy-Setup-<arch>.exe (Inno Setup), and
# writes SHA256SUMS.txt. The release workflow runs exactly this.
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
        $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
        if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
        if (-not (Test-Path $iscc)) { throw "Inno Setup 6 is needed to build the installer: winget install JRSoftware.InnoSetup" }
        if (Test-Path artifacts) { Remove-Item artifacts -Recurse -Force }
        New-Item -ItemType Directory artifacts | Out-Null
        foreach ($arch in "x64", "arm64") {
            $platform = if ($arch -eq "arm64") { "ARM64" } else { "x64" }
            $out = "artifacts/Tidy-win-$arch"
            Invoke-Checked { dotnet publish src/Tidy.App -c Release -p:Platform=$platform -r "win-$arch" -p:Version=$Version -o $out }
            Compress-Archive -Path "$out/*" -DestinationPath "artifacts/Tidy-win-$arch.zip" -CompressionLevel Optimal
            Invoke-Checked { & $iscc /Q "/DAppVersion=$Version" "/DArch=$arch" installer/tidy.iss }
        }
        Get-ChildItem artifacts -File | Where-Object Extension -in ".zip", ".exe" | ForEach-Object {
            "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
        } | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
        Get-Content artifacts/SHA256SUMS.txt
    }
}
