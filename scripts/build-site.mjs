// Builds the website into _site/.
//
//   node scripts/build-site.mjs [version]
//
// The safety page is generated from the app's own rules (catalog.json, apps.json and the
// protected fragments in DeletionSafetyPolicy.cs), so the published list cannot drift from
// what Tidy actually does.

import { readFileSync, writeFileSync, mkdirSync, cpSync, rmSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "_site");
const version = (process.argv[2] ?? process.env.TIDY_VERSION ?? "0.1.0").replace(/^v/, "");
const siteUrl = process.env.SITE_URL ?? "https://baala3.github.io/tidy";
const repo = "https://github.com/baala3/tidy";

const read = (path) => readFileSync(join(root, path), "utf8");
const json = (path) => JSON.parse(read(path));
const escape = (text) =>
  String(text).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

const placeholders = {
  "{{VERSION}}": version,
  "{{SITE_URL}}": siteUrl,
  "{{DOWNLOAD_X64}}": `${repo}/releases/latest/download/Tidy-Setup-x64.exe`,
  "{{DOWNLOAD_ARM64}}": `${repo}/releases/latest/download/Tidy-Setup-arm64.exe`,
  "{{DOWNLOAD_ZIP}}": `${repo}/releases/latest/download/Tidy-win-x64.zip`,
};
const fill = (html) => Object.entries(placeholders).reduce((text, [key, value]) => text.replaceAll(key, value), html);

const catalog = json("src/Tidy.Core/Catalog/catalog.json");
const apps = json("src/Tidy.Core/Apps/apps.json");
const policy = read("src/Tidy.Core/Safety/DeletionSafetyPolicy.cs");

// The fragments list is a C# collection expression of string literals.
const fragmentsBlock = policy.match(/ProtectedFragments\s*=\s*\[([\s\S]*?)\];/);
if (!fragmentsBlock) throw new Error("ProtectedFragments not found in DeletionSafetyPolicy.cs");
const fragments = [...fragmentsBlock[1].matchAll(/"([^"]+)"/g)].map((m) => m[1]);

const tag = (tier) =>
  tier === "safe" ? '<span class="tag safe">Safe to Clean</span>' : '<span class="tag check">Check First</span>';
const appTag = (tier) =>
  tier === "safe" ? '<span class="tag safe">Safe to Remove</span>' : '<span class="tag check">Check First</span>';
const prettyPath = (path) =>
  path
    .replace("{Local}", "%LOCALAPPDATA%")
    .replace("{Roaming}", "%APPDATA%")
    .replace("{LocalLow}", "%USERPROFILE%\\AppData\\LocalLow")
    .replace("{Home}", "%USERPROFILE%")
    .replace("{Temp}", "%TEMP%");

const cacheRows = (scope) =>
  catalog.entries
    .filter((e) => e.scope === scope)
    .map(
      (e) => `<tr><td><strong>${escape(e.name)}</strong><br>${tag(e.tier)}</td><td>${escape(e.explanation)}${
        e.minAgeHours ? ` Only items untouched for ${e.minAgeHours} hours.` : ""
      }${e.closeFirst ? ` Skipped while ${escape(e.closeFirst.app)} is open.` : ""}</td><td>${e.paths
        .map((p) => `<code>${escape(prettyPath(p))}${e.contentsOnly ? "\\*" : ""}</code>`)
        .join("<br>")}</td></tr>`
    )
    .join("\n");

const artifactRows = Object.values(catalog.artifacts)
  .map((a) => `<tr><td><strong>${escape(a.name)}</strong></td><td colspan="2">${escape(a.explanation)}</td></tr>`)
  .join("\n");

const preinstalledRows = apps.preinstalled
  .map(
    (e) =>
      `<tr><td><strong>${escape(e.name ?? e.match.replace(/\.$/, "") + " apps")}</strong><br>${appTag(e.tier)}</td><td>${escape(
        e.explanation
      )}</td><td><code>${escape(e.match)}${e.match.endsWith(".") ? "*" : ""}</code></td></tr>`
    )
    .join("\n");

const chips = (items) => `<ul class="chips">${items.map((i) => `<li>${escape(i)}</li>`).join("")}</ul>`;

const safety = `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Tidy Safety: what it clears and what it never touches</title>
  <meta name="description" content="Every location Tidy may clear, every app it may offer, and everything it refuses. Generated from the app's own rules.">
  <link rel="icon" href="assets/tidy.svg" type="image/svg+xml">
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <header class="top">
    <a class="brand" href="./"><img src="assets/tidy.svg" alt="" width="28" height="28"><span>Tidy</span></a>
    <nav>
      <a href="safety.html">Safety</a>
      <a href="${repo}">GitHub</a>
      <a class="button small primary" href="${placeholders["{{DOWNLOAD_X64}}"]}">Download</a>
    </nav>
  </header>
  <main class="doc">
    <h1>Safety</h1>
    <p>Tidy only clears what is listed on this page. This page is generated from Tidy ${escape(version)}'s own rules, so it always matches what the app does.</p>
    <p>Every path is checked again at the moment it is moved, and goes to the Recycle Bin. Anything behind a link or junction is skipped. If the Recycle Bin is off for a drive, or an item is too big for it, Tidy leaves it alone rather than delete it for good.</p>

    <h2>App caches</h2>
    <p class="note">Safe to Clean items take part in Select All Safe. Check First items are never selected for you.</p>
    <table class="table"><thead><tr><th>Item</th><th>What it is</th><th>Where</th></tr></thead><tbody>
${cacheRows("app_caches")}
<tr><td><strong>Electron and Chromium app caches</strong><br>${tag("safe")}</td><td>${escape(
  catalog.chromiumApps.explanation.replaceAll("{app}", "the app")
)} Only folders with a browser cache's own file layout qualify.</td><td>${catalog.chromiumApps.folders
  .map((f) => `<code>%APPDATA%\\&lt;app&gt;\\${escape(f)}</code>`)
  .join("<br>")}</td></tr>
    </tbody></table>

    <h2>Developer tools</h2>
    <table class="table"><thead><tr><th>Item</th><th>What it is</th><th>Where</th></tr></thead><tbody>
${cacheRows("dev_tools")}
    </tbody></table>

    <h2>Project folders</h2>
    <p class="note">Offered only inside a real project (its <code>package.json</code>, <code>Cargo.toml</code>, <code>.csproj</code> and so on must be there), only when nothing in the project changed for the stale period you choose, and never while a terminal, dev server or git command is working inside it. A project with no lockfile drops to Check First.</p>
    <table class="table"><thead><tr><th>Folder</th><th colspan="2">What it is</th></tr></thead><tbody>
${artifactRows}
    </tbody></table>

    <h2>Preinstalled extras</h2>
    <p class="note">Apps are never selected for you. Store apps are removed for your account and can be reinstalled from the Microsoft Store. Desktop apps run their own uninstaller.</p>
    <table class="table"><thead><tr><th>App</th><th>Why</th><th>Package</th></tr></thead><tbody>
${preinstalledRows}
    </tbody></table>

    <h2>Never touched</h2>
    <p class="note">Your profile, AppData, Documents, Desktop, Downloads, Pictures, Music, Videos and OneDrive folders themselves; Windows, Program Files and ProgramData; credential, certificate and key stores; Store app data; and Tidy's own settings.</p>
    <p class="note">Any path containing one of these words is refused, so password managers and sign-in state are never offered:</p>
    ${chips(fragments)}

    <h2>Apps never offered</h2>
    <p class="note">Store packages Windows or other apps depend on, by package name:</p>
    ${chips([...apps.neverStore.map((p) => p + "*"), ...apps.neverStoreSuffixes.map((s) => "*" + s)])}
    <p class="note" style="margin-top:16px">Desktop programs whose name contains:</p>
    ${chips(apps.neverDesktop)}
  </main>
  <footer>
    <div class="links">
      <a href="./">Home</a>
      <a href="${repo}/blob/main/docs/safety-notes.md">Safety notes</a>
      <a href="${repo}/blob/main/SECURITY.md">Report a problem</a>
      <a href="${repo}">GitHub</a>
    </div>
    <p class="credit">Generated from Tidy ${escape(version)}. Based on <a href="https://github.com/jithin-sabu/purge-app">Purge for Mac</a> by Jithin Sabu (MIT).</p>
  </footer>
</body>
</html>
`;

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
cpSync(join(root, "site"), out, { recursive: true });
writeFileSync(join(out, "index.html"), fill(read("site/index.html")));
writeFileSync(join(out, "safety.html"), safety);
cpSync(join(root, "scripts", "install.ps1"), join(out, "install.ps1"));
writeFileSync(join(out, ".nojekyll"), "");
console.log(`Built _site for Tidy ${version}: ${catalog.entries.length} cache entries, ${apps.preinstalled.length} preinstalled apps, ${fragments.length} protected fragments.`);
