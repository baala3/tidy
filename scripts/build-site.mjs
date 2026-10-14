// Builds the website into _site/.
//
//   node scripts/build-site.mjs [version]
//   node scripts/build-site.mjs --serve [version]   build, then preview at http://localhost:8080
//
// Preview through --serve rather than opening site/index.html from disk: the Safety page only
// exists in _site/, and the download links and version are filled in at build time. The preview
// rebuilds on every page load, so edits show up on reload.
//
// The safety page is generated from the app's own rules (catalog.json, apps.json and the
// protected fragments in DeletionSafetyPolicy.cs), so the published list cannot drift from
// what Tidy actually does.

import { readFileSync, writeFileSync, mkdirSync, cpSync, rmSync } from "node:fs";
import { join, dirname, extname, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync } from "node:child_process";
import { createServer } from "node:http";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "_site");
const args = process.argv.slice(2);
const serve = args.includes("--serve");
const version = (args.find((a) => !a.startsWith("--")) ?? process.env.TIDY_VERSION ?? "0.1.0").replace(/^v/, "");
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

// The Safety page's nav. It matches the one in site/index.html (keep the two in step), but is glass
// from the start and its section links point back at the home page.
const nav = ({ home, solid }) => `<nav class="nav${solid ? " solid" : ""}" id="nav" aria-label="Primary">
    <a class="brand" href="./" aria-label="Tidy home">
      <svg viewBox="0 0 32 32" aria-hidden="true">
        <linearGradient id="brand-sun-fill" x1="0" y1="5" x2="0" y2="27" gradientUnits="userSpaceOnUse"><stop offset="0" stop-color="#f5c54e"/><stop offset=".55" stop-color="#ff9171"/><stop offset="1" stop-color="#fd75a8"/></linearGradient>
        <clipPath id="brand-sun"><circle cx="16" cy="16" r="11"/></clipPath>
        <g clip-path="url(#brand-sun)" fill="currentColor">
          <rect x="5" y="5" width="22" height="11.4"/>
          <rect x="5" y="18" width="22" height="3.2"/>
          <rect x="5" y="22.7" width="22" height="2"/>
          <rect x="5" y="26" width="22" height="1.2"/>
        </g>
      </svg>
      Tidy
    </a>
    <ul class="nav-links" id="navLinks">
      <li><a href="${home}#clears">What it clears</a></li>
      <li><a href="${home}#how">How it works</a></li>
      <li><a href="safety.html">Safety</a></li>
      <li><a href="${home}#faq">Questions</a></li>
      <li><a href="${repo}">GitHub</a></li>
    </ul>
    <a class="nav-cta" href="${placeholders["{{DOWNLOAD_X64}}"]}">Download</a>
    <button class="nav-toggle" id="navToggle" type="button" aria-label="Open menu" aria-expanded="false" aria-controls="navLinks">
      <span class="burger" aria-hidden="true"><i></i><i></i><i></i></span>
    </button>
    <div class="nav-meta" aria-hidden="true">
      <span>Tidy ${escape(version)} · free and open source</span>
      <span>Windows 10 and 11</span>
    </div>
  </nav>`;

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
  <meta name="theme-color" content="#08070f">
  <link rel="icon" href="assets/tidy.svg" type="image/svg+xml">
  <link rel="apple-touch-icon" href="assets/apple-touch-icon.png">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@500;600;700&family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap">
  <link rel="stylesheet" href="style.css">
</head>
<body>
  ${nav({ home: "./", solid: true })}
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
  <footer class="site-footer">
    <span>Generated from Tidy ${escape(version)}. Based on <a href="https://github.com/jithin-sabu/purge-app">Purge for Mac</a> by Jithin Sabu (MIT).</span>
    <nav>
      <a href="./">Home</a>
      <a href="${repo}/blob/main/docs/safety-notes.md">Safety notes</a>
      <a href="${repo}/blob/main/SECURITY.md">Report a problem</a>
      <a href="${repo}">GitHub</a>
    </nav>
  </footer>
  <script src="site.js"></script>
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

if (serve) {
  const port = Number(process.env.PORT ?? 8080);
  const types = { ".html": "text/html", ".css": "text/css", ".js": "text/javascript", ".svg": "image/svg+xml", ".png": "image/png", ".webp": "image/webp", ".ps1": "text/plain" };
  const rebuild = () => execFileSync(process.execPath, [fileURLToPath(import.meta.url), version], { stdio: "ignore" });
  createServer((req, res) => {
    let path = decodeURIComponent(new URL(req.url, "http://localhost").pathname);
    if (path.endsWith("/")) path += "index.html";
    const file = normalize(join(out, path));
    if (!file.startsWith(out)) { res.writeHead(403).end(); return; }
    if (extname(file) === ".html") rebuild();
    let body;
    try {
      body = readFileSync(file);
    } catch {
      res.writeHead(404, { "content-type": "text/plain" }).end("Not found");
      return;
    }
    res.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream", "cache-control": "no-store" });
    res.end(body);
  }).listen(port, () => console.log(`Previewing at http://localhost:${port}/ (Ctrl+C to stop)`));
}
