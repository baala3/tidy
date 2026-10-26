<div align="center">

<img src="assets/tidy.svg" width="112" alt="Tidy icon">

# Tidy

**Free up your PC. Safely.**

A free Windows app that clears the cache and junk your PC piles up on its own.
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
- **Updates:** installed apps winget has a newer version of, with both version numbers, updated one at a time with progress.
  An update is offered only when winget matches the app exactly, by its Settings > Apps entry, its Store package, or because winget installed it.
  Apps that update themselves, like browsers and Steam, are labeled and not picked, and updates that install for everyone say they need administrator permission.
  If winget is missing, the view explains how to get App Installer from the Microsoft Store.

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
- Similar Photos groups pictures that look alike even when resized, recompressed, cropped a little or turned, with thumbnails side by side.
  A similarity setting (Strict, Normal or Loose) decides how alike, and Strict is the default.
  Tidy suggests keeping the one with the most pixels, then the sharpest, and never a messaging app's copy.
- Screenshots lists old screenshots, and Messaging Apps lists photos and videos WhatsApp, Telegram or Signal saved that also exist elsewhere on the PC.
- Pictures are read with Windows' own codecs, so HEIC and camera RAW files work where the PC has their extensions.
  What Tidy learns about each photo is kept, so later scans only read new ones.
- Nothing is selected for you, one photo of each group always stays, and a file that changed since the scan is left alone.

### OneDrive

- Files in OneDrive that also take space on this PC, from 1 MB up, with the same search and filters as Large Files.
- Free Up Space does what File Explorer's "Free up space" does: the file stays in OneDrive and in File Explorer, and downloads again when you open it. Nothing is deleted.
- Only files that are fully synced are offered. Files still uploading, and files you set to "Always keep on this device", are left alone.

### Installers

- Setup files in the same folders for apps you already have, like `SteamSetup.exe` once Steam is installed.
- A file counts only if it is an installer (a package, or a program that says it sets something up) and names an installed app exactly. Launchers that only share the app's name are left out.
- Installers newer than the installed app are marked Check First, since you may still mean to run them.

### Leftovers

- Folders that uninstalled apps left in AppData, ProgramData and Program Files, with their size, when they last changed and the app they came from.
- After you uninstall an app from Tidy, it shows what that app left behind.
- A folder is listed only on strong evidence: a folder the app registered with Windows, its publisher and name together, or its whole name. A folder that merely shares a word with an app is left out, and so is anything an installed or running app could be using.
- Everything is Check First and nothing is selected for you. Folders in ProgramData and Program Files need an administrator, so Tidy shows them for you to remove yourself.

### Startup

- Everything that starts when you sign in: Run keys, the Startup folders, Store apps' startup tasks, and Task Scheduler tasks that run at sign-in, which Task Manager leaves out.
- Each one shows the app it belongs to, when it was added or turned off where Windows records that, and what its programs use now: memory, and CPU measured over a few seconds.
- Check Sign-in reads what Windows recorded each one using in the first minutes after your last sign-in, CPU time and data read from disk. It asks for administrator permission and only reads.
- Turning one off flips the same switch Task Manager, Settings or Task Scheduler has, so nothing is deleted and you can turn it back on there. One that starts for everyone on the PC asks for administrator permission.
- Each is Safe to Turn Off or Check First with a note, and one whose program is gone is safe. Windows Security, antivirus, input methods and drivers' helpers are never listed.
- Tidy promises no speed-up. It shows only numbers it measured.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/apps-dark.png">
  <img src="docs/screenshots/apps-light.png" width="760" alt="Apps: installed apps with when each was last opened, and preinstalled extras tagged">
</picture>

## Space Map

- Scans any drive, including other internal and external drives, or any folder, and shows where the space went.
- A tree of the whole drive, which you can sort by size, name, number of files or last change, and beside it a treemap of the folder you pick.
  Click a folder in either one to look inside it, and the details below the treemap show what you picked and what you can do with it.
- Sizes are what files take on disk, so a file that lives only in OneDrive counts as nothing, and a file with several names (hard links) is counted once.
- Things another page of Tidy knows, like a cache, an old project, a WSL disk or an app, take that page's color and link to it.
  The Space Map itself only shows: you can open an item in File Explorer or copy its path, and nothing is removed from here.
- The normal scan needs no special rights, so folders Windows won't let you open are counted as other used space.
  Fast scan reads an NTFS drive's file table directly, the way WizTree does: it takes a second or two on a full drive and sees every folder, and Windows asks for administrator permission each time.

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
- The Space Map's fast scan also runs with administrator permission, and only reads: it opens the drive for reading and takes nothing but a drive letter.
- The Startup page deletes nothing: it turns entries off with Windows' own switch, and its administrator helper only accepts entries its own scan finds.
- An automatic clean takes only what is marked Safe to Clean, and nothing runs in the background unless you turn it on.
- Tidy collects no data.
  Its one network request asks GitHub for the latest release, when it opens and once a day, and sends nothing about your PC.
  Turn it off in Settings > Updates.
  Checking for and installing app updates is winget's own work: winget reaches its sources itself, and Tidy sends nothing.

The [safety page](https://baala3.github.io/tidy/safety.html) lists everything Tidy may clear and everything it refuses.
It is generated from the app's own rules.
Incidents are logged in [docs/safety-notes.md](docs/safety-notes.md), and [SECURITY.md](SECURITY.md) says how to report one.

## Report a problem

Open an [issue](https://github.com/baala3/tidy/issues) with your Tidy version (About shows it), your Windows version, and what you saw.
If Tidy removed or offered something it should not have, report it privately instead, as [SECURITY.md](SECURITY.md) explains.

## License

Tidy is proprietary software, licensed under the [Tidy End User License Agreement](LICENSE).
Versions 0.1.0 through 0.5.0 were released under the MIT License, and those releases remain under it.
Third-party licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
