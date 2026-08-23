# Rider's Vencord custom plugin manager (RVCPM) v0.1.1

A Windows manager for Vencord custom userplugins. The host is C# WinForms targeting .NET Framework 4.7.2; the Discord-style UI runs in Microsoft Edge WebView2.

## Highlights

- Multi-plugin install from `.ts`, `.tsx`, plugin folders, ZIP archives and GitHub repository/tree/blob URLs.
- Drag & Drop, including folders (browser drop imports a snapshot; use the native Files/Folder buttons for packages larger than 16 MB).
- Per-machine state under `%LOCALAPPDATA%\RVCPM`; every installed plugin (including GitHub) is kept as its own build snapshot.
- Dedicated managed Vencord checkout; every install pulls the latest Vencord main branch, restores dependencies, builds once and injects the fresh custom build.
- Automatic Git/Node checks and winget bootstrap; exact pnpm version is read from Vencord's current `package.json`.
- Enable/disable plugins with safe staged Vencord settings writes and a Discord restart banner/button.
- Generic editing for statically detectable Vencord settings (`STRING`, `NUMBER`, `BOOLEAN`, `SELECT`, `SLIDER`, simple `CUSTOM`). Arbitrary React `COMPONENT`, complex `CUSTOM`, and `BIGINT` remain Discord-only by design.
- Remove plugins, optionally remove their Vencord settings.
- GitHub repository description + README display.
- Update checks compare each plugin’s installed GitHub commit or local-source SHA-256; one-plugin/update-all flows keep rollback copies until build/injection succeeds.
- Stable/PTB/Canary/Auto Discord targeting, plus custom location.
- English, Russian and Ukrainian UI (English default).
- `Plugins+` integration: when at least one RVCPM-managed plugin exists, an internal hidden userplugin dynamically renames Vencord Settings' standard `Plugins` entry to `Plugins+`.
- Safe ZIP extraction (path traversal protection, entry/expanded-size limits), atomic app config writes, Vencord settings rollback and stale-temp cleanup.

See **README_RU.md** for detailed architecture, **SUPPORTED_PLUGINS.md** for accepted source layouts, and **QA_CHECKS.md** for release validation notes.

## Build

On Windows with Visual Studio 2022 / Build Tools and the .NET desktop development workload:

```bat
Build_Release.bat
```

Output is copied to `dist\RVCPM`.

This project pins:

- Microsoft.Web.WebView2 `1.0.4129.50`
- Newtonsoft.Json `13.0.3`

## Security

Custom Vencord plugins execute code inside Discord. RVCPM validates and packages source without executing it during analysis, but it cannot make untrusted plugin code safe. Install only plugins you trust.
