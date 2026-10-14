# Security

Tidy deletes files, so a bug that removes the wrong thing is a security issue.

## Reporting

Please report privately through [GitHub security advisories](https://github.com/baala3/tidy/security/advisories/new).
Do not open a public issue for something that could make Tidy remove data it should not.

Include the Tidy version, your Windows version, the path involved, and what Tidy showed before you confirmed.
You should hear back within a few days.

## What counts

- Tidy removing, or offering, anything outside its allowlist.
- A link, junction or path trick that makes a removal land somewhere else.
- Anything deleted for good when Tidy said it would go to the Recycle Bin.
- An app removal that breaks Windows or another app, when Tidy did not warn about it.

Fixed incidents are logged in [docs/safety-notes.md](docs/safety-notes.md).
