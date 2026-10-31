using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class VencordService
    {
        private readonly ConfigStore _store;
        private readonly ProcessRunner _runner;
        private readonly PackageService _packages;
        private readonly DiscordService _discord;
        private readonly VencordSettingsService _settings;
        private readonly Action<OperationProgress> _progress;
        private readonly Action<string> _log;

        public VencordService(ConfigStore store, ProcessRunner runner, PackageService packages, DiscordService discord, VencordSettingsService settings, Action<OperationProgress> progress, Action<string> log)
        {
            _store = store;
            _runner = runner;
            _packages = packages;
            _discord = discord;
            _settings = settings;
            _progress = progress;
            _log = log;
        }

        public async Task BuildAndInjectAsync(bool updateVencord, CancellationToken token, VencordSettingsService.Snapshot failureSettingsSnapshot = null, bool forceRestart = false)
        {
            var runningKinds = _discord.GetRunningDiscordKinds();
            var wasRunning = runningKinds.Count > 0;
            var shouldRestart = wasRunning && (forceRestart || _store.Config.AutoRestartAfterInstall);
            var settingsSnapshot = _settings.CaptureSnapshot();
            var settingsFlushed = false;
            var buildSucceeded = false;
            string rollbackCommit = null;
            try
            {
                _progress(new OperationProgress { Stage = "toolchain", Message = "Checking Git and Vencord source", Percent = 5 });
                await EnsureGitAsync(token).ConfigureAwait(false);
                rollbackCommit = await ResolveRollbackCommitAsync(token).ConfigureAwait(false);
                await EnsureVencordSourceAsync(updateVencord, token).ConfigureAwait(false);

                var packageInfo = ReadVencordPackageInfo();
                _progress(new OperationProgress { Stage = "toolchain", Message = "Checking Node.js " + packageInfo.NodeRequirement, Percent = 12 });
                await EnsureNodeAsync(packageInfo.NodeRequirement, token).ConfigureAwait(false);

                _progress(new OperationProgress { Stage = "plugins", Message = "Synchronizing managed plugins", Percent = 20 });
                PrepareUserPlugins();
                ApplyCompatibilityRules();

                _progress(new OperationProgress { Stage = "dependencies", Message = "Installing Vencord dependencies", Percent = 30 });
                var install = await RunNpxPnpmAsync(packageInfo.PnpmVersion, "install --frozen-lockfile", token).ConfigureAwait(false);
                if (!install.Success) throw new InvalidOperationException("Vencord dependency installation failed.\n" + install.Error);

                _progress(new OperationProgress { Stage = "build", Message = "Building Vencord with custom plugins", Percent = 55 });
                var buildArgs = "build" + (_store.Config.DevBuild ? " --dev" : "");
                var build = await RunNpxPnpmAsync(packageInfo.PnpmVersion, buildArgs, token).ConfigureAwait(false);
                if (!build.Success && updateVencord && !string.IsNullOrWhiteSpace(rollbackCommit))
                {
                    var latestError = FormatBuildFailure(build.Error);
                    _log(latestError);
                    _progress(new OperationProgress { Stage = "compatibility", Message = "Latest Vencord is incompatible; trying last compatible revision", Percent = 58, CanCancel = false });

                    await RestoreVencordCommitAsync(rollbackCommit, token).ConfigureAwait(false);
                    packageInfo = ReadVencordPackageInfo();
                    await EnsureNodeAsync(packageInfo.NodeRequirement, token).ConfigureAwait(false);
                    PrepareUserPlugins();
                    ApplyCompatibilityRules();

                    var fallbackInstall = await RunNpxPnpmAsync(packageInfo.PnpmVersion, "install --frozen-lockfile", token).ConfigureAwait(false);
                    if (!fallbackInstall.Success)
                        throw new InvalidOperationException(latestError + "\n\nRVCPM also failed to prepare the last compatible Vencord revision.\n" + fallbackInstall.Error);

                    build = await RunNpxPnpmAsync(packageInfo.PnpmVersion, buildArgs, token).ConfigureAwait(false);
                    if (!build.Success)
                        throw new InvalidOperationException(latestError + "\n\nThe last known-compatible Vencord revision also failed to build.\n" + build.Error);

                    _log("Latest Vencord is currently incompatible with managed plugins; continuing on last compatible revision " + ShortHash(rollbackCommit) + ".");
                    _progress(new OperationProgress { Stage = "compatibility", Message = "Using last compatible Vencord revision", Percent = 68, CanCancel = false });
                }
                else if (!build.Success)
                {
                    throw new InvalidOperationException(FormatBuildFailure(build.Error));
                }
                buildSucceeded = true;

                _progress(new OperationProgress { Stage = "discord", Message = "Preparing Discord for injection", Percent = 78 });
                if (wasRunning)
                {
                    _progress(new OperationProgress { Stage = "discord", Message = "Closing Discord automatically", Percent = 78, CanCancel = false });
                    await _discord.StopAsync("auto", token).ConfigureAwait(false);
                }
                if (!_discord.IsAnyDiscordRunning())
                {
                    _settings.FlushPending();
                    settingsFlushed = true;
                }

                _progress(new OperationProgress { Stage = "inject", Message = "Injecting the freshly built Vencord", Percent = 86 });
                var installerArgs = "scripts/runInstaller.mjs -- --install";
                if (!string.IsNullOrWhiteSpace(_store.Config.CustomDiscordLocation))
                    installerArgs += " --location " + ProcessRunner.Quote(_store.Config.CustomDiscordLocation);
                else
                    installerArgs += " --branch " + (_store.Config.DiscordBranch ?? "auto");

                var inject = await _runner.RunAsync("node", installerArgs, AppPaths.VencordDir, token).ConfigureAwait(false);
                if (!inject.Success) throw new InvalidOperationException("Vencord injection failed.\n" + inject.Error);

                var commit = await _runner.RunAsync("git", "rev-parse HEAD", AppPaths.VencordDir, token).ConfigureAwait(false);
                _store.Config.LastVencordCommit = commit.Success ? commit.Output.Trim() : "";
                _store.Config.LastVencordVersion = packageInfo.Version;
                _store.Config.LastBuildUtc = DateTime.UtcNow;
                _store.Config.PendingRestart = false;
                _store.Save();

                _progress(new OperationProgress { Stage = "done", Message = "Vencord and custom plugins are installed", Percent = 100, CanCancel = false });
            }
            catch
            {
                var restore = failureSettingsSnapshot ?? settingsSnapshot;
                try
                {
                    // If Discord is already stopped (or settings were flushed), restoring the file is safe.
                    // If the failure happened while Discord was still running, only restore RVCPM's staged values.
                    if (settingsFlushed || !_discord.IsAnyDiscordRunning()) _settings.RestoreSnapshot(restore);
                    else _settings.RestorePendingSnapshot(restore);
                }
                catch (Exception restoreEx) { _log("Could not restore Vencord settings after build failure: " + restoreEx.Message); }

                // Vencord is a moving API. Never leave the managed source tree on a revision that
                // could not build all installed RVCPM plugins. Roll back to the last known-good
                // commit so a future retry starts from a working baseline.
                if (updateVencord && !buildSucceeded && !string.IsNullOrWhiteSpace(rollbackCommit))
                {
                    try
                    {
                        _progress(new OperationProgress { Stage = "rollback", Message = "Restoring last compatible Vencord revision", Percent = 70, CanCancel = false });
                        await RestoreVencordCommitAsync(rollbackCommit, CancellationToken.None).ConfigureAwait(false);
                        _log("Latest Vencord revision was incompatible with one or more managed plugins. Restored " + ShortHash(rollbackCommit) + ".");
                    }
                    catch (Exception rollbackEx)
                    {
                        _log("Warning: failed to restore previous Vencord revision: " + rollbackEx.Message);
                    }
                }
                throw;
            }
            finally
            {
                if (shouldRestart && !_discord.IsAnyDiscordRunning())
                {
                    try
                    {
                        _progress(new OperationProgress { Stage = "discord", Message = "Starting Discord", Percent = 100, CanCancel = false });
                        if (!string.IsNullOrWhiteSpace(_store.Config.CustomDiscordLocation))
                        {
                            await _discord.StartCustomAsync(_store.Config.CustomDiscordLocation).ConfigureAwait(false);
                        }
                        else if ((_store.Config.DiscordBranch ?? "auto").Equals("auto", StringComparison.OrdinalIgnoreCase) && runningKinds.Count > 0)
                        {
                            foreach (var kind in runningKinds.Distinct(StringComparer.OrdinalIgnoreCase))
                                await _discord.StartAsync(kind).ConfigureAwait(false);
                        }
                        else
                        {
                            await _discord.StartAsync(_store.Config.DiscordBranch).ConfigureAwait(false);
                        }
                    }
                    catch { }
                }
            }
        }

        private async Task<string> ResolveRollbackCommitAsync(CancellationToken token)
        {
            // Prefer the revision that last completed a successful build/injection. The worktree
            // itself may already be on a broken fetched revision after an interrupted older RVCPM run.
            if (!string.IsNullOrWhiteSpace(_store.Config.LastVencordCommit))
                return _store.Config.LastVencordCommit.Trim();

            if (!Directory.Exists(Path.Combine(AppPaths.VencordDir, ".git"))) return null;
            var current = await _runner.RunAsync("git", "rev-parse HEAD", AppPaths.VencordDir, token).ConfigureAwait(false);
            return current.Success ? current.Output.Trim() : null;
        }

        private async Task RestoreVencordCommitAsync(string commit, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(commit) || !Directory.Exists(Path.Combine(AppPaths.VencordDir, ".git"))) return;

            // A shallow repository may have discarded an older object. Try the reset first, then
            // fetch that exact revision if necessary.
            var reset = await _runner.RunAsync("git", "reset --hard " + commit, AppPaths.VencordDir, token).ConfigureAwait(false);
            if (reset.Success) return;

            var fetch = await _runner.RunAsync("git", "fetch --depth 1 origin " + commit, AppPaths.VencordDir, token).ConfigureAwait(false);
            if (!fetch.Success) throw new InvalidOperationException("Could not fetch previous Vencord revision.\n" + fetch.Error);
            reset = await _runner.RunAsync("git", "reset --hard FETCH_HEAD", AppPaths.VencordDir, token).ConfigureAwait(false);
            if (!reset.Success) throw new InvalidOperationException("Could not restore previous Vencord revision.\n" + reset.Error);
        }

        private void ApplyCompatibilityRules()
        {
            var userPlugins = Path.Combine(AppPaths.VencordDir, "src", "userplugins");
            if (!Directory.Exists(userPlugins)) return;

            // Compatibility rules are deliberately applied only to the generated Vencord copy,
            // never to the user's package in %LOCALAPPDATA%\\RVCPM\\packages. This keeps packages
            // pristine and makes every repair reversible. Unknown future API breaks are still
            // protected by the last-known-good Vencord rollback above.
            var rules = new[]
            {
                new CompatibilityImportRule("@components/Heading", Path.Combine("src", "components", "Heading.tsx"), "HeadingSecondary", "Heading")
            };

            foreach (var file in Directory.EnumerateFiles(userPlugins, "*.*", SearchOption.AllDirectories)
                .Where(x => x.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)))
            {
                var text = File.ReadAllText(file);
                var original = text;
                foreach (var rule in rules)
                {
                    if (rule.IsNeeded(AppPaths.VencordDir)) text = rule.Apply(text);
                }
                if (text == original) continue;
                File.WriteAllText(file, text);
                _log("Applied Vencord compatibility shim to " + MakeRelativePluginPath(file) + ".");
            }
        }

        private static string MakeRelativePluginPath(string file)
        {
            var root = Path.Combine(AppPaths.VencordDir, "src", "userplugins") + Path.DirectorySeparatorChar;
            return file.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? file.Substring(root.Length) : Path.GetFileName(file);
        }

        private static string FormatBuildFailure(string error)
        {
            var raw = error ?? "";
            var names = Regex.Matches(raw, @"src[\\/]userplugins[\\/]rvcpm-([^\\/\r\n:]+)", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (names.Length == 0) return "Vencord build failed.\n" + raw;
            return "Vencord update is incompatible with managed plugin(s): " + string.Join(", ", names) + ".\n" +
                   "RVCPM will restore the last known-compatible Vencord revision instead of leaving the managed installation half-updated.\n\n" + raw;
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "unknown";
            return hash.Length > 8 ? hash.Substring(0, 8) : hash;
        }

        private sealed class CompatibilityImportRule
        {
            private readonly string _module;
            private readonly string _targetRelativePath;
            private readonly string _oldName;
            private readonly string _newName;

            public CompatibilityImportRule(string module, string targetRelativePath, string oldName, string newName)
            {
                _module = module;
                _targetRelativePath = targetRelativePath;
                _oldName = oldName;
                _newName = newName;
            }

            public bool IsNeeded(string vencordRoot)
            {
                try
                {
                    var target = Path.Combine(vencordRoot, _targetRelativePath);
                    if (!File.Exists(target)) return false;
                    var moduleSource = File.ReadAllText(target);
                    return moduleSource.IndexOf(_oldName, StringComparison.Ordinal) < 0 && moduleSource.IndexOf(_newName, StringComparison.Ordinal) >= 0;
                }
                catch { return false; }
            }

            public string Apply(string source)
            {
                if (string.IsNullOrEmpty(source) || source.IndexOf(_oldName, StringComparison.Ordinal) < 0 || source.IndexOf(_module, StringComparison.Ordinal) < 0)
                    return source;

                // Preserve all call sites by aliasing the replacement export back to the old local
                // symbol: `HeadingSecondary` -> `Heading as HeadingSecondary`.
                var importPattern = @"import\s*\{(?<items>[^}]*)\}\s*from\s*[""']" + Regex.Escape(_module) + @"[""']\s*;?";
                return Regex.Replace(source, importPattern, m =>
                {
                    var items = m.Groups["items"].Value;
                    if (!Regex.IsMatch(items, @"(?<![\w$])" + Regex.Escape(_oldName) + @"(?![\w$])")) return m.Value;
                    if (Regex.IsMatch(items, @"(?<![\w$])" + Regex.Escape(_newName) + @"\s+as\s+" + Regex.Escape(_oldName) + @"(?![\w$])")) return m.Value;
                    items = Regex.Replace(items, @"(?<![\w$])" + Regex.Escape(_oldName) + @"(?![\w$])", _newName + " as " + _oldName);
                    return m.Value.Substring(0, m.Groups["items"].Index - m.Index) + items + m.Value.Substring(m.Groups["items"].Index - m.Index + m.Groups["items"].Length);
                }, RegexOptions.Multiline);
            }
        }

        public async Task EnsureVencordSourceAsync(bool update, CancellationToken token)
        {
            if (!Directory.Exists(Path.Combine(AppPaths.VencordDir, ".git")))
            {
                if (Directory.Exists(AppPaths.VencordDir)) Directory.Delete(AppPaths.VencordDir, true);
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.VencordDir));
                _log("Cloning latest Vencord source...");
                var clone = await _runner.RunAsync("git", "clone --depth 1 --branch main https://github.com/Vendicated/Vencord.git " + ProcessRunner.Quote(AppPaths.VencordDir), AppPaths.Root, token).ConfigureAwait(false);
                if (!clone.Success) throw new InvalidOperationException("Could not clone Vencord.\n" + clone.Error);
                return;
            }

            if (!update) return;
            _log("Updating Vencord source to origin/main...");
            var fetch = await _runner.RunAsync("git", "fetch --depth 1 origin main", AppPaths.VencordDir, token).ConfigureAwait(false);
            if (!fetch.Success) throw new InvalidOperationException("Could not fetch latest Vencord.\n" + fetch.Error);
            var reset = await _runner.RunAsync("git", "reset --hard FETCH_HEAD", AppPaths.VencordDir, token).ConfigureAwait(false);
            if (!reset.Success) throw new InvalidOperationException("Could not update Vencord worktree.\n" + reset.Error);
        }

        public async Task EnsureGitAsync(CancellationToken token)
        {
            ProcessRunner.RefreshProcessPath();
            if (ProcessRunner.CommandExists("git")) return;
            if (!ProcessRunner.CommandExists("winget"))
                throw new InvalidOperationException("Git is required and was not found. Windows Package Manager (winget) is also unavailable, so RVCPM cannot install Git automatically.");

            _log("Git not found. Installing Git with winget...");
            var r = await _runner.RunAsync("winget", "install --id Git.Git -e --silent --accept-package-agreements --accept-source-agreements", AppPaths.Root, token).ConfigureAwait(false);
            ProcessRunner.RefreshProcessPath();
            if (!r.Success || !ProcessRunner.CommandExists("git"))
                throw new InvalidOperationException("Automatic Git installation failed.\n" + r.Error);
        }

        private async Task EnsureNodeAsync(string requirement, CancellationToken token)
        {
            ProcessRunner.RefreshProcessPath();
            var min = ParseMinimumMajor(requirement);
            var current = await GetNodeMajorAsync(token).ConfigureAwait(false);
            if (current >= min && current > 0) return;

            if (!ProcessRunner.CommandExists("winget"))
                throw new InvalidOperationException("Node.js " + requirement + " is required. winget is unavailable, so it cannot be installed automatically.");

            _log("Node.js is missing or too old. Installing/updating Node.js LTS with winget...");
            var upgrade = await _runner.RunAsync("winget", "upgrade --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements", AppPaths.Root, token).ConfigureAwait(false);
            if (!upgrade.Success)
                await _runner.RunAsync("winget", "install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements", AppPaths.Root, token).ConfigureAwait(false);

            ProcessRunner.RefreshProcessPath();
            current = await GetNodeMajorAsync(token).ConfigureAwait(false);
            if (current < min)
                throw new InvalidOperationException("Node.js installation completed, but the current process still cannot find a compatible Node.js version. Required: " + requirement + ". Close RVCPM, reopen it, and retry.");
        }

        private async Task<int> GetNodeMajorAsync(CancellationToken token)
        {
            if (!ProcessRunner.CommandExists("node")) return 0;
            var r = await _runner.RunAsync("node", "--version", AppPaths.Root, token).ConfigureAwait(false);
            if (!r.Success) return 0;
            var m = Regex.Match(r.Output ?? "", @"v?(?<m>\d+)");
            int v;
            return m.Success && int.TryParse(m.Groups["m"].Value, out v) ? v : 0;
        }

        private Task<ProcessResult> RunNpxPnpmAsync(string version, string args, CancellationToken token)
        {
            // Using npx avoids global pnpm installation and honors the exact packageManager version requested by Vencord.
            var cmd = "npx --yes pnpm@" + version + " " + args;
            return _runner.RunAsync("cmd.exe", "/d /s /c \"" + cmd.Replace("\"", "\\\"") + "\"", AppPaths.VencordDir, token);
        }

        private void PrepareUserPlugins()
        {
            ValidatePluginSet();
            var dir = Path.Combine(AppPaths.VencordDir, "src", "userplugins");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            foreach (var plugin in _store.Config.Plugins)
                _packages.SyncPluginToVencord(_store.Config, plugin, dir);

            if (_store.Config.Plugins.Count > 0)
                WriteIntegrationPlugin(dir);
        }

        private void ValidatePluginSet()
        {
            var dup = _store.Config.Plugins.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw new InvalidOperationException("Duplicate Vencord plugin name: " + dup.Key);

            foreach (var p in _store.Config.Plugins)
            {
                if (p.TargetSuffix == "web" || p.TargetSuffix == "browser" || p.TargetSuffix == "vesktop")
                    _log("Warning: " + p.Name + " targets " + p.TargetSuffix + " and is not compatible with Discord Desktop.");
                if (p.TargetSuffix == "dev" && !_store.Config.DevBuild)
                    _log("Warning: " + p.Name + " is a dev plugin and will be excluded unless Dev Build is enabled.");
            }
        }

        private static void WriteIntegrationPlugin(string userPluginsDir)
        {
            var dir = Path.Combine(userPluginsDir, "rvcpmIntegration");
            Directory.CreateDirectory(dir);

            var code = @"import { openPluginModal } from ""@components/settings/tabs/plugins/PluginModal"";
import definePlugin, { PluginNative } from ""@utils/types"";

const Native = VencordNative.pluginHelpers.RVCPMIntegration as PluginNative<typeof import(""./native"")>;

let originalBuildEntry: any = null;
let bridgeTimer: ReturnType<typeof setInterval> | null = null;
let bridgePollInFlight = false;

async function pollBridge() {
    if (bridgePollInFlight) return;
    bridgePollInFlight = true;
    let command: any = null;
    try {
        command = await Native.takeBridgeCommand();
        if (!command) return;

        if (command.action !== ""openPluginSettings"")
            throw new Error(`Unsupported RVCPM bridge action: ${String(command.action)}`);

        const plugin = (Vencord.Plugins.plugins as Record<string, any>)[command.pluginName];
        if (!plugin)
            throw new Error(`Plugin '${String(command.pluginName)}' is not present in the active Vencord build. Apply pending RVCPM changes first.`);

        // Use Vencord's own settings modal. This is important for OptionType.COMPONENT and
        // settingsAboutComponent: arbitrary plugin React must execute in Discord's real
        // webpack/store/Vencord environment rather than being emulated in RVCPM's WebView2.
        openPluginModal(plugin);
        await Native.writeBridgeAck(String(command.id), true, """");
    } catch (error) {
        if (command?.id) {
            try {
                const message = error instanceof Error ? error.message : String(error);
                await Native.writeBridgeAck(String(command.id), false, message);
            } catch { }
        }
    } finally {
        bridgePollInFlight = false;
    }
}

export default definePlugin({
    name: ""RVCPMIntegration"",
    description: ""Internal RVCPM integration. Provides Plugins+ branding and the local bridge used to open native Vencord custom settings UI."",
    authors: [{ name: ""Rider"", id: 0n }],
    required: true,
    hidden: true,

    start() {
        const settingsPlugin: any = Vencord.Plugins.plugins.Settings;
        if (settingsPlugin && !originalBuildEntry && typeof settingsPlugin.buildEntry === ""function"") {
            originalBuildEntry = settingsPlugin.buildEntry;
            settingsPlugin.buildEntry = function(options: any) {
                if (options?.key === ""vencord_plugins"") options = { ...options, title: ""Plugins+"" };
                return originalBuildEntry.call(this, options);
            };
        }

        void pollBridge();
        bridgeTimer = setInterval(() => void pollBridge(), 250);
    },

    stop() {
        if (bridgeTimer !== null) clearInterval(bridgeTimer);
        bridgeTimer = null;
        const settingsPlugin: any = Vencord.Plugins.plugins.Settings;
        if (settingsPlugin && originalBuildEntry) settingsPlugin.buildEntry = originalBuildEntry;
        originalBuildEntry = null;
    }
});
";

            var nativeCode = @"import { IpcMainInvokeEvent } from ""electron"";
import { existsSync, mkdirSync, readFileSync, renameSync, unlinkSync, writeFileSync } from ""fs"";
import { join } from ""path"";

const root = join(process.env.LOCALAPPDATA || process.env.TEMP || ""."", ""RVCPM"");
const commandFile = join(root, ""bridge-command.json"");
const ackFile = join(root, ""bridge-ack.json"");

function safeUnlink(path: string) {
    try { if (existsSync(path)) unlinkSync(path); } catch { }
}

function atomicWrite(path: string, value: unknown) {
    mkdirSync(root, { recursive: true });
    const temp = `${path}.${process.pid}.${Date.now()}.tmp`;
    writeFileSync(temp, JSON.stringify(value), ""utf8"");
    try {
        safeUnlink(path);
        renameSync(temp, path);
    } finally {
        safeUnlink(temp);
    }
}

export function takeBridgeCommand(_: IpcMainInvokeEvent) {
    try {
        if (!existsSync(commandFile)) return null;
        const raw = readFileSync(commandFile, ""utf8"");
        safeUnlink(commandFile);
        return JSON.parse(raw);
    } catch (error: any) {
        if (error?.code === ""ENOENT"") return null;
        throw error;
    }
}

export function writeBridgeAck(_: IpcMainInvokeEvent, id: string, ok: boolean, error: string) {
    atomicWrite(ackFile, {
        version: 1,
        id,
        ok,
        error: error || """",
        createdUtc: new Date().toISOString()
    });
}
";

            File.WriteAllText(Path.Combine(dir, "index.ts"), code);
            File.WriteAllText(Path.Combine(dir, "native.ts"), nativeCode);
            File.WriteAllText(Path.Combine(dir, ".rvcpm-managed"), "internal\nRVCPMIntegration");
        }

        private VencordPackageInfo ReadVencordPackageInfo()
        {
            var path = Path.Combine(AppPaths.VencordDir, "package.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Vencord package.json is missing.", path);
            var jo = JObject.Parse(File.ReadAllText(path));
            var manager = (string)jo["packageManager"] ?? "pnpm@latest";
            var at = manager.LastIndexOf('@');
            var pnpm = at >= 0 ? manager.Substring(at + 1) : "latest";
            var engines = jo["engines"] as JObject;
            return new VencordPackageInfo
            {
                Version = (string)jo["version"] ?? "unknown",
                PnpmVersion = pnpm,
                NodeRequirement = engines != null ? (string)engines["node"] ?? ">=22" : ">=22"
            };
        }

        private static int ParseMinimumMajor(string requirement)
        {
            var m = Regex.Match(requirement ?? "", @"(?<m>\d+)");
            int major;
            return m.Success && int.TryParse(m.Groups["m"].Value, out major) ? major : 22;
        }

        private sealed class VencordPackageInfo
        {
            public string Version { get; set; }
            public string PnpmVersion { get; set; }
            public string NodeRequirement { get; set; }
        }
    }
}
