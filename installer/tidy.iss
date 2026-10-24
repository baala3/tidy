; Per-user installer for Tidy. No admin rights: installs to %LOCALAPPDATA%\Programs\Tidy,
; adds a Start menu shortcut and an entry in Settings > Apps.
;
; Built by build.ps1 -Target publish:  iscc /DAppVersion=1.2.3 /DArch=x64 installer\tidy.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{6F3B1D2E-8C4A-4E7B-9A1F-5D2C7E9B3A10}
AppName=Tidy
AppVersion={#AppVersion}
AppVerName=Tidy {#AppVersion}
AppPublisher=baala3
AppPublisherURL=https://baala3.github.io/tidy
AppSupportURL=https://github.com/baala3/tidy/issues
AppUpdatesURL=https://github.com/baala3/tidy/releases
DefaultDirName={localappdata}\Programs\Tidy
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=Tidy-Setup-{#Arch}
SetupIconFile=..\src\Tidy.App\Assets\Tidy.ico
UninstallDisplayIcon={app}\Tidy.exe
UninstallDisplayName=Tidy
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
MinVersion=10.0.19041

[Files]
Source: "..\artifacts\Tidy-win-{#Arch}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\Tidy"; Filename: "{app}\Tidy.exe"; Comment: "Free up your PC. Safely."

[Run]
Filename: "{app}\Tidy.exe"; Description: "Open Tidy"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes Tidy's scheduled task and its notification registration, before the files go.
Filename: "{app}\Tidy.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSchedule"

[UninstallDelete]
; Tidy's settings.
Type: filesandordirs; Name: "{localappdata}\Tidy"
