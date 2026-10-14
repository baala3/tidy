# Contributing

Thanks for helping.
Tidy deletes files, so deletion safety comes before every other goal.

## Build and test

You need Windows 10 (19041) or later and the [.NET 10 SDK](https://dotnet.microsoft.com/download).
If PowerShell blocks scripts, run them as `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Target test`.

```powershell
.\build.ps1 -Target test    # unit tests
.\build.ps1 -Target run     # build and start the app
.\build.ps1 -Target publish # installers and zips in artifacts\ (needs Inno Setup 6)
```

## Where things live

- `src/Tidy.Core` holds scanning, the safety policy and the deleter. It has no UI.
- `src/Tidy.Core/Catalog/catalog.json` names every cache location Tidy may offer, with its tier and explanation.
- `src/Tidy.Core/Apps/apps.json` lists preinstalled extras and the apps that must never be offered.
- `src/Tidy.Core/Safety/DeletionSafetyPolicy.cs` is the allowlist every removal goes through.
- `src/Tidy.App` is the WinUI 3 app.
- `tests/Tidy.Core.Tests` holds the tests. They run against a throwaway profile, never your real folders.
- `site/` is the website, published to GitHub Pages.
  Preview it with `node scripts/build-site.mjs --serve` and open http://localhost:8080.
  Opening `site/index.html` from disk won't have the Safety page, which is generated into `_site/`.

## Rules for changes

- Mark a location Safe to Clean only when it holds nothing but data its app rebuilds by itself.
  If it can hold settings, sign-ins, downloads you chose, projects or history, it is Check First or not offered at all.
- A new cache location needs an entry in `catalog.json` and a matching pattern in `DeletionSafetyPolicy.AllowedPatterns`.
  The catalog only proposes; the policy decides. A test fails if the two disagree.
- Every removal goes through `FileDeleter`, which re-checks the policy at the moment of removal.
  Never add another way to delete files.
- When you switch over one of Tidy's enums, list every case and leave out `default`.
- Make one fix or one feature per pull request, and open an issue first for anything bigger than a small fix.
- For UI changes, add before and after screenshots in light and dark mode.
