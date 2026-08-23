<div align="center">
  <img src="RVCPM/Web/rvcpm-icon.png" width="112" height="112" alt="RVCPM icon" />

  # Rider's Vencord Custom Plugin Manager

  **A modern Windows manager for Vencord custom plugins.**  
  Install, update, configure and control userplugins without manually touching `src/userplugins`, `pnpm` or the Vencord build pipeline.

  [![Version](https://img.shields.io/badge/version-0.1.3-5865F2?style=flat-square)](#)
  [![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-5865F2?style=flat-square&logo=windows11&logoColor=white)](#)
  [![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-512BD4?style=flat-square&logo=dotnet&logoColor=white)](#)
  [![WebView2](https://img.shields.io/badge/UI-WebView2-5C2D91?style=flat-square&logo=microsoftedge&logoColor=white)](#)
  [![Vencord](https://img.shields.io/badge/Vencord-userplugins-5865F2?style=flat-square)](https://github.com/Vendicated/Vencord)

  **English** · [Русский](README_RU.md)
</div>

---

## What is RVCPM?

**RVCPM** turns Vencord custom-plugin management into a normal desktop workflow.

Instead of cloning Vencord, copying files into `src/userplugins`, installing the right Node/pnpm version, rebuilding, injecting and repeating the same process after every update, RVCPM handles the pipeline for you.

Drop a plugin into the app, paste a GitHub link, select what you want to install, and RVCPM takes care of the rest.

> [!NOTE]
> RVCPM is an independent project and is **not affiliated with or endorsed by Vencord or Discord**.

## Highlights

| | Feature | What it does |
|---|---|---|
| 📦 | **Multi-plugin installation** | Install several plugins in one operation from files, folders, ZIP archives or GitHub. |
| 🖱️ | **Drag & Drop** | Drop `.ts`, `.tsx`, plugin folders, ZIPs, multiple packages, or a mixture of them directly into the window. |
| 🐙 | **GitHub integration** | Install from a public repository, `/tree/...` subfolder or `/blob/...` plugin file. |
| 🔄 | **Plugin updates** | Detect updates by Git commit for GitHub sources and SHA-256 for local sources. |
| ⚙️ | **Plugin settings** | Edit supported Vencord plugin settings directly from RVCPM. |
| ⏻ | **Enable / disable** | Stage plugin state changes safely and restart Discord with one click when required. |
| 🧹 | **Removal** | Remove managed plugins and optionally clean their Vencord settings. |
| 🛠️ | **Automatic Vencord build** | Pull the latest Vencord source, install dependencies, build once and inject the result automatically. |
| ↩️ | **Rollback protection** | Keep rollback copies until the new build and injection complete successfully. |
| 🌐 | **3 interface languages** | English, Russian and Ukrainian. English is the default. |
| ✨ | **Plugins+ integration** | When RVCPM manages at least one plugin, the standard Vencord `Plugins` entry becomes `Plugins+`. |
| 💾 | **Per-PC state** | Sources, snapshots, repositories, configuration and logs are stored locally for each machine. |

## Quick start

### 1. Launch RVCPM

Build the project using `Build_Release.bat` or Visual Studio, then run `RVCPM.exe`.

### 2. Add plugins

You can:

- drag files, folders or ZIP archives into the window;
- use the **Files** / **Folder** buttons;
- paste a public GitHub repository URL;
- paste a GitHub `/tree/...` URL;
- paste a direct GitHub `/blob/...` plugin URL.

RVCPM scans the source and shows the Vencord plugins it found before installation.

### 3. Install

Press **Install**. RVCPM will automatically:

```text
Update / clone Vencord
        ↓
Check Node.js + Git
        ↓
Resolve the pnpm version required by current Vencord
        ↓
Restore managed userplugins
        ↓
Install dependencies
        ↓
Build Vencord
        ↓
Inject the fresh custom build into Discord
```

The manager keeps its own Vencord checkout, so your normal files do not have to be used as a build workspace.

## Supported plugin sources

### Native Vencord userplugin layouts

Single-file plugin:

```text
myPlugin.ts
```

or:

```text
myPlugin.tsx
```

Folder plugin:

```text
myPlugin/
├── index.ts
├── native.ts
├── styles.css
├── components/
└── ...
```

or:

```text
myPlugin/
├── index.tsx
└── ...
```

The entry point must expose a normal Vencord-style plugin definition such as:

```ts
export default definePlugin({
    name: "MyPlugin",
    description: "My custom plugin",
    authors: [...]
});
```

RVCPM also understands companion files and assets used by the plugin, including additional TypeScript/TSX modules, `native.ts`, CSS, JSON and local assets.

### Transport formats handled by RVCPM

These are unpacked/resolved into valid Vencord userplugins:

| Source | Supported |
|---|:---:|
| `.ts` | ✅ |
| `.tsx` | ✅ |
| Plugin folder | ✅ |
| ZIP archive | ✅ |
| Multiple ZIPs | ✅ |
| Mixed files + folders + ZIPs | ✅ |
| Public GitHub repository | ✅ |
| GitHub `/tree/...` | ✅ |
| GitHub `/blob/...` | ✅ |
| Drag & Drop | ✅ |

For the full layout rules, see [`SUPPORTED_PLUGINS.md`](SUPPORTED_PLUGINS.md).

## Vencord target suffixes

RVCPM recognizes Vencord target suffixes such as:

```text
.dev
.web
.browser
.desktop
.discordDesktop
.vesktop
```

Desktop management accepts normal desktop-compatible plugins and rejects web/browser/Vesktop-only entries from the standard Discord Desktop install flow. Dev-only plugins require a dev-compatible build.

## Plugin library

Every managed plugin gets its own card with its current state and source information.

From the library you can:

- enable or disable a plugin;
- open its settings;
- view its description;
- view GitHub README information when available;
- check for an update;
- update a single plugin;
- remove the plugin;
- optionally remove its stored Vencord settings.

When changing a state that requires a restart, RVCPM shows a **Discord restart required** banner with a one-click restart button.

## Plugin settings

RVCPM can edit normal statically detectable Vencord settings without opening Discord.

| Vencord setting type | RVCPM |
|---|:---:|
| `STRING` | ✅ |
| `NUMBER` | ✅ |
| `BOOLEAN` | ✅ |
| `SELECT` | ✅ |
| `SLIDER` | ✅ |
| Simple static `CUSTOM` | ⚠️ Partial |
| `BIGINT` | ➖ Discord only |
| `COMPONENT` | ➖ Discord only |
| Complex dynamic `CUSTOM` | ➖ Discord only |

`COMPONENT` settings are arbitrary React UI rendered inside Discord/Vencord, so RVCPM deliberately does not execute them inside its own WebView2 interface.

## Updates

RVCPM keeps enough source information to update plugins later.

**GitHub plugins** are checked against their source commit.  
**Local plugins** are tracked using SHA-256 source fingerprints.

You can update one plugin or use **Update All**. Before replacing a working plugin, RVCPM keeps a rollback copy and only commits the new state after the Vencord build/injection succeeds.

When installing or rebuilding managed plugins, RVCPM also updates its managed Vencord checkout to the latest `main` branch before compilation.

## Plugins+

When at least one RVCPM-managed plugin exists, the manager automatically adds a small internal integration userplugin during the build.

It changes the standard Vencord Settings entry:

```text
Plugins
```

to:

```text
Plugins+
```

This only affects the label of Vencord's existing plugin section. When there are no RVCPM-managed plugins, the integration plugin is not added.

## Local data

RVCPM stores machine-specific state under:

```text
%LOCALAPPDATA%\RVCPM\
```

Typical structure:

```text
RVCPM/
├── config/          # manager configuration
├── packages/        # managed plugin snapshots
├── repositories/    # GitHub working copies / source state
├── vencord/         # dedicated managed Vencord checkout
├── logs/            # operation logs
├── temp/            # temporary import/build files
└── WebView2/        # WebView2 profile data
```

This makes each computer independent: the manager does not assume that another machine has the same paths, Discord installation or plugin files.

## Discord installations

RVCPM supports targeting:

- Discord Stable;
- Discord PTB;
- Discord Canary;
- automatic detection;
- a custom Discord location.

## Requirements

- Windows 10 or Windows 11;
- .NET Framework 4.7.2;
- Microsoft Edge WebView2 Runtime;
- internet access for Vencord/GitHub updates and first-time dependency installation;
- Git and a Vencord-compatible Node.js version.

RVCPM checks the required build tools and can bootstrap missing Git/Node components through `winget` when available. The required pnpm version is read from the **current Vencord `package.json`**, rather than being hard-coded into RVCPM.

## Building from source

### Visual Studio

Open:

```text
RVCPM.sln
```

and build the **Release** configuration.

### One-click build

Run:

```bat
Build_Release.bat
```

The release output is copied to:

```text
dist\RVCPM\
```

### Project stack

```text
C# / WinForms
.NET Framework 4.7.2
Microsoft Edge WebView2
HTML + CSS + JavaScript UI
Newtonsoft.Json
```

## Security

> [!WARNING]
> **A Vencord plugin is executable code that runs inside Discord.**

RVCPM can validate source structure, safely unpack ZIP archives and avoid executing plugin source during import analysis, but it cannot make an untrusted plugin safe.

Only install plugins from developers or repositories you trust.

ZIP imports include path-traversal protection and extraction limits to reduce the risk of malicious archives writing outside RVCPM's temporary workspace.

## Troubleshooting

<details>
<summary><b>A plugin is detected through Files but not through Drag & Drop</b></summary>
<br>
Make sure you are running the latest RVCPM build. Drag & Drop and the native file picker share the same ZIP/plugin analysis pipeline in current versions.
</details>

<details>
<summary><b>Discord says a restart is required</b></summary>
<br>
This is expected after changing certain plugin states/settings. Use the <b>Restart Discord</b> button in RVCPM so staged changes can be applied safely before Discord starts again.
</details>

<details>
<summary><b>The Vencord build fails</b></summary>
<br>
Open the <b>Logs</b> page. A custom plugin can stop the Vencord build if its source is outdated, invalid, or incompatible with the current Vencord/Discord build. RVCPM keeps rollback state until the new build succeeds.
</details>

<details>
<summary><b>Why can't RVCPM edit every plugin setting?</b></summary>
<br>
Some Vencord settings are custom React components or dynamic code. Recreating arbitrary plugin UI outside Discord would require executing plugin code inside RVCPM, which is intentionally avoided.
</details>

## Related links

- [Vencord](https://github.com/Vendicated/Vencord)
- [Vencord custom plugin documentation](https://docs.vencord.dev/installing/custom-plugins/)
- [Microsoft WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)

---

<div align="center">
  <sub><b>RVCPM</b> — custom Vencord plugins without the repetitive build work.</sub>
</div>
