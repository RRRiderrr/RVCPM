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
            try
            {
                _progress(new OperationProgress { Stage = "toolchain", Message = "Checking Git and Vencord source", Percent = 5 });
                await EnsureGitAsync(token).ConfigureAwait(false);
                await EnsureVencordSourceAsync(updateVencord, token).ConfigureAwait(false);

                var packageInfo = ReadVencordPackageInfo();
                _progress(new OperationProgress { Stage = "toolchain", Message = "Checking Node.js " + packageInfo.NodeRequirement, Percent = 12 });
                await EnsureNodeAsync(packageInfo.NodeRequirement, token).ConfigureAwait(false);

                _progress(new OperationProgress { Stage = "plugins", Message = "Synchronizing managed plugins", Percent = 20 });
                PrepareUserPlugins();

                _progress(new OperationProgress { Stage = "dependencies", Message = "Installing Vencord dependencies", Percent = 30 });
                var install = await RunNpxPnpmAsync(packageInfo.PnpmVersion, "install --frozen-lockfile", token).ConfigureAwait(false);
                if (!install.Success) throw new InvalidOperationException("Vencord dependency installation failed.\n" + install.Error);

                _progress(new OperationProgress { Stage = "build", Message = "Building Vencord with custom plugins", Percent = 55 });
                var buildArgs = "build" + (_store.Config.DevBuild ? " --dev" : "");
                var build = await RunNpxPnpmAsync(packageInfo.PnpmVersion, buildArgs, token).ConfigureAwait(false);
                if (!build.Success) throw new InvalidOperationException("Vencord build failed.\n" + build.Error);

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
            var code = @"import definePlugin from ""@utils/types"";

let originalBuildEntry: any = null;

export default definePlugin({
    name: ""RVCPMIntegration"",
    description: ""Internal RVCPM integration. Renames Vencord's Plugins entry to Plugins+ while RVCPM manages at least one custom plugin."",
    authors: [{ name: ""Rider"", id: 0n }],
    required: true,
    hidden: true,

    start() {
        const settingsPlugin: any = Vencord.Plugins.plugins.Settings;
        if (!settingsPlugin || originalBuildEntry || typeof settingsPlugin.buildEntry !== ""function"") return;
        originalBuildEntry = settingsPlugin.buildEntry;
        settingsPlugin.buildEntry = function(options: any) {
            if (options?.key === ""vencord_plugins"") options = { ...options, title: ""Plugins+"" };
            return originalBuildEntry.call(this, options);
        };
    },

    stop() {
        const settingsPlugin: any = Vencord.Plugins.plugins.Settings;
        if (settingsPlugin && originalBuildEntry) settingsPlugin.buildEntry = originalBuildEntry;
        originalBuildEntry = null;
    }
});
";
            File.WriteAllText(Path.Combine(dir, "index.ts"), code);
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
