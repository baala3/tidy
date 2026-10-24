<div align="center">

<img src="assets/tidy.svg" width="112" alt="Tidy icon">

# Tidy

**Free up your PC. Safely.**

A free, open-source Windows app that clears the cache and junk your PC piles up on its own.
Files go to the Recycle Bin first, so you can put them back.

[Website](https://baala3.github.io/tidy) · [Download](https://github.com/baala3/tidy/releases/latest) · [Safety](https://baala3.github.io/tidy/safety.html)

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/overview-dark.png">
  <img src="docs/screenshots/overview-light.png" width="760" alt="Tidy's Overview: where the space on the drive went, by category">
</picture>

</div>

## Install

In PowerShell:

```powershell
irm https://baala3.github.io/tidy/install.ps1 | iex
```

The script downloads the latest installer, checks it against the published SHA-256, and installs Tidy for your account in `%LOCALAPPDATA%\Programs\Tidy`.
It needs no admin rights, adds a Start menu shortcut, and adds Tidy to Settings > Apps so you can uninstall it there.

Or download `Tidy-Setup-x64.exe` (or `Tidy-Setup-arm64.exe`) from [Releases](https://github.com/baala3/tidy/releases/latest) and run it.
For a portable copy, extract `Tidy-win-x64.zip` anywhere and run `Tidy.exe`.
Tidy tells you when a new version is out, and About > Install Update installs it.
A portable copy links you to the new zip instead.
Tidy is not code-signed yet, so Windows SmartScreen may say "Windows protected your PC" about a downloaded file.
Choose **More info**, then **Run anyway**.

Tidy runs on Windows 10 (version 2004) and Windows 11, on x64 and Arm64.

## What it clears

### App Caches

- Temp files untouched for a day, crash dumps, Windows error reports and DirectX, NVIDIA, AMD and Intel shader caches.
- Browser caches for every Chrome, Edge, Brave, Vivaldi, Opera and Firefox profile.
  Passwords, history, cookies and sign-ins sit next to these and are never touched.
  A browser that is open is skipped until you close it.
- Caches of Electron apps like Discord, Slack, Notion and Teams, recognized by the cache's own file layout.
- Telegram Desktop's media cache, and the iPhone and iPad software updates iTunes downloads.
- iPhone and iPad backups from iTunes and Apple Devices, one Check First row per device with its name and the date of the backup.

### Dev Tools

- Package caches for npm, pnpm, Yarn, Bun, Deno, NuGet, Gradle, Maven, Cargo, Go, pip, uv, Poetry, conda, Composer, vcpkg and Scoop.
- Editor caches for VS Code, Cursor, JetBrains IDEs and Visual Studio.
- Rebuildable folders in old projects: `node_modules`, Rust `target`, .NET `bin` and `obj`, Python environments, Gradle and Maven output, and more.
  A project shows up only when nothing in it changed for the stale period you choose (6 months by default), and never while a terminal, dev server or git command is working inside it.

### Apps

- **Preinstalled extras:** games, promos and trials that came with Windows or your PC, each marked Safe to Remove or Check First with a note on what it does.
- **Not used in 90 days:** from the launch history Windows keeps for Start, the taskbar and File Explorer.
- Runtimes, codecs, drivers, the Store, Windows Security and Xbox sign-in parts are never offered.
- Store apps are removed for your account and can be reinstalled from the Microsoft Store.
  Desktop apps run their own uninstaller, the same one Settings > Apps opens.

### Windows

- Windows Update leftovers, Delivery Optimization files, your previous version of Windows, old driver versions, upgrade logs and crash dumps.
- Tidy has Windows' own Disk Cleanup handlers measure and remove them, the same ones Disk Cleanup and Storage Sense run, and passes them no paths.
- Checking and cleaning each ask for administrator permission once. What's cleaned can't go to the Recycle Bin, so nothing is selected for you.

### WSL & Docker

- The disk images of your WSL distros and Docker Desktop, which grow as Linux writes and never shrink by themselves.
- Tidy reads each disk's block table to show how much compacting gives back, without starting or stopping anything.
- Compact stops Docker Desktop and shuts WSL down (you're told which distros are running first), then compacts with the same Windows call `Optimize-VHD` uses. Nothing inside the disks changes, and it needs no admin rights.

### Large Files

- Big files in Documents, Desktop, Downloads, Videos, Music and Pictures, with search, size and age filters, and type chips.
- Nothing is selected for you, and files that live only in OneDrive are left alone.

### Duplicates

- Files over 1 MB in the same folders whose contents match byte for byte, grouped so you can see every copy.
- One copy of each file always stays: Tidy suggests which, and won't let you pick the last one.
- Right before a copy moves, Tidy compares it again with the copy that stays, and leaves it alone if anything changed.

### OneDrive

- Files in OneDrive that also take space on this PC, from 1 MB up, with the same search and filters as Large Files.
- Free Up Space does what File Explorer's "Free up space" does: the file stays in OneDrive and in File Explorer, and downloads again when you open it. Nothing is deleted.
- Only files that are fully synced are offered. Files still uploading, and files you set to "Always keep on this device", are left alone.

### Installers

- Setup files in the same folders for apps you already have, like `SteamSetup.exe` once Steam is installed.
- A file counts only if it is an installer (a package, or a program that says it sets something up) and names an installed app exactly. Launchers that only share the app's name are left out.
- Installers newer than the installed app are marked Check First, since you may still mean to run them.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/apps-dark.png">
  <img src="docs/screenshots/apps-light.png" width="760" alt="Apps: installed apps with when each was last opened, and preinstalled extras tagged">
</picture>

## Automatic cleaning

- In Settings, Tidy can clean caches by itself every week or every month. It is off until you turn it on.
- It cleans only rows App Caches and Dev Tools mark Safe to Clean, never project folders or iPhone backups, and everything goes to the Recycle Bin.
- A browser or app that is open is skipped. Tidy waits up to a day for it to close, then cleans the rest without it.
- Tidy can also tell you when the drive Windows is on has less than 10% free, or less than 20 GB on a big drive, at most once a day.
- Both run from one Task Scheduler task that starts `Tidy.exe --background` once an hour, as you, without administrator rights. Turning both off removes the task, and so does uninstalling Tidy.
- History lists every clean, the ones you run and the automatic ones, with what was removed.

## Safety

Tidy deletes files, so it is built to be sure rather than thorough.

- Caches are offered only from a fixed allowlist, and anything Tidy does not recognize never shows up.
- Every path is checked again at the moment it is removed, not just when it was scanned.
- Anything behind a link or junction is refused, and so are credential stores and password managers.
- Files go to the Recycle Bin. If the Recycle Bin is off for a drive, or an item is too big for it, Tidy leaves the item alone rather than delete it for good.
- The Windows page is the exception, and says so: Windows' own Disk Cleanup removes those files, they can't go to the Recycle Bin, and Windows asks for administrator permission each time.
- An automatic clean takes only what is marked Safe to Clean, and nothing runs in the background unless you turn it on.
- Tidy collects no data.
  Its one network request asks GitHub for the latest release, when it opens and once a day, and sends nothing about your PC.
  Turn it off in Settings > Updates.

The [safety page](https://baala3.github.io/tidy/safety.html) lists everything Tidy may clear and everything it refuses.
It is generated from the app's own rules.
Incidents are logged in [docs/safety-notes.md](docs/safety-notes.md), and [SECURITY.md](SECURITY.md) says how to report one.

## Build from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/baala3/tidy
cd tidy
.\build.ps1 -Target test
.\build.ps1 -Target run
```

[CONTRIBUTING.md](CONTRIBUTING.md) explains how the code is laid out and the rules for changes.

## Credits

Tidy is based on [Purge for Mac](https://github.com/jithin-sabu/purge-app) by Jithin Sabu, used under the MIT License.
Its deletion-safety approach, safety tiers and many explanations come from Purge.
Tidy is an independent project and is not affiliated with Purge.
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## License

[MIT](LICENSE)
