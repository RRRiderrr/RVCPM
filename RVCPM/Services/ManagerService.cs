using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class ManagerService : IDisposable
    {
        private readonly ConfigStore _store;
        private readonly ProcessRunner _runner;
        private readonly PluginScanner _scanner;
        private readonly GitHubService _github;
        private readonly DiscordService _discord;
        private readonly VencordSettingsService _settings;
        private readonly PackageService _packages;
        private readonly VencordService _vencord;
        private readonly ConcurrentDictionary<string, CandidateBatch> _batches = new ConcurrentDictionary<string, CandidateBatch>();
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _operationCts;
        private readonly object _logLock = new object();

        public event Action<string, object> EventRaised;

        public ManagerService()
        {
            _store = new ConfigStore();
            _runner = new ProcessRunner(Log);
            _scanner = new PluginScanner();
            _github = new GitHubService(_runner, _scanner);
            _discord = new DiscordService(Log);
            _settings = new VencordSettingsService(_store, _discord, Log);
            _packages = new PackageService(Log);
            _vencord = new VencordService(_store, _runner, _packages, _discord, _settings, ReportProgress, Log);

            CleanupUnreferencedRepositories();
            RefreshManagedPluginMetadata();
        }

        public AppConfig Config { get { return _store.Config; } }

        public JObject GetState()
        {
            var plugins = new JArray();
            foreach (var p in _store.Config.Plugins.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var effective = _settings.GetEffectivePluginObject(p.Name);
                plugins.Add(new JObject
                {
                    ["id"] = p.Id,
                    ["name"] = p.Name,
                    ["description"] = p.Description ?? "",
                    ["pluginDescription"] = p.PluginDescription ?? "",
                    ["author"] = p.Author ?? "",
                    ["version"] = p.Version ?? "",
                    ["sourceKind"] = p.SourceKind.ToString(),
                    ["sourceReference"] = p.SourceReference ?? "",
                    ["githubUrl"] = p.GitHubUrl ?? "",
                    ["readme"] = p.Readme ?? "",
                    ["enabled"] = p.Required || ((bool?)effective["enabled"] ?? p.EnabledByDefault),
                    ["required"] = p.Required,
                    ["hasSettings"] = p.HasSettings,
                    ["settingsCount"] = CountManagerEditableSettings(p.Settings),
                    ["customSettingsUi"] = HasCustomSettingsUi(p.Settings),
                    ["customSettingsUiCount"] = CountCustomSettingsUi(p.Settings),
                    ["requiresRestart"] = p.RequiresRestart,
                    ["updateAvailable"] = p.UpdateAvailable,
                    ["target"] = string.IsNullOrWhiteSpace(p.TargetSuffix) ? "desktop/default" : p.TargetSuffix,
                    ["dependencies"] = JArray.FromObject(p.Dependencies ?? new List<string>()),
                    ["installedUtc"] = p.InstalledUtc,
                    ["lastUpdatedUtc"] = p.LastUpdatedUtc.HasValue ? JToken.FromObject(p.LastUpdatedUtc.Value) : JValue.CreateNull()
                });
            }

            return new JObject
            {
                ["appVersion"] = "0.1.6",
                ["language"] = _store.Config.Language ?? "en",
                ["discordBranch"] = _store.Config.DiscordBranch ?? "auto",
                ["customDiscordLocation"] = _store.Config.CustomDiscordLocation ?? "",
                ["autoUpdateVencord"] = _store.Config.AutoUpdateVencordBeforeBuild,
                ["autoRestartAfterInstall"] = _store.Config.AutoRestartAfterInstall,
                ["enableAfterInstall"] = _store.Config.EnablePluginsAfterInstall,
                ["devBuild"] = _store.Config.DevBuild,
                ["pendingRestart"] = _store.Config.PendingRestart,
                ["pendingBuildChanges"] = _store.Config.PendingBuildChanges,
                ["pendingChanges"] = _store.Config.PendingRestart || _store.Config.PendingBuildChanges,
                ["pendingChangeCount"] = _store.Config.PendingPluginSettings.Count + (_store.Config.PendingBuildChanges ? 1 : 0),
                ["discordStatus"] = _discord.GetStatusLabel(),
                ["discordRunning"] = _discord.IsAnyDiscordRunning(),
                ["vencordInstalledByManager"] = Directory.Exists(Path.Combine(AppPaths.VencordDir, ".git")),
                ["vencordVersion"] = _store.Config.LastVencordVersion ?? "",
                ["vencordCommit"] = Short(_store.Config.LastVencordCommit),
                ["vencordPath"] = AppPaths.VencordDir,
                ["dataPath"] = AppPaths.Root,
                ["settingsPath"] = _settings.SettingsFile,
                ["lastBuildUtc"] = _store.Config.LastBuildUtc.HasValue ? JToken.FromObject(_store.Config.LastBuildUtc.Value) : JValue.CreateNull(),
                ["plugins"] = plugins,
                ["supported"] = new JArray(
                    "Single-file Vencord userplugins: .ts, .tsx",
                    "Plugin folders: index.ts or index.tsx plus companion files (native.ts, CSS, components, assets)",
                    "ZIP packages containing one or more valid userplugins",
                    "GitHub repository / tree / blob URLs containing valid userplugins"
                )
            };
        }

        public CandidateBatch AnalyzeLocalPaths(IEnumerable<string> paths)
        {
            var batch = _scanner.AnalyzePaths(paths);
            _batches[batch.Id] = batch;
            Raise("candidateBatch", ToBatchJson(batch));
            return batch;
        }

        public CandidateBatch AnalyzeDroppedFiles(JArray files)
        {
            if (files == null || files.Count == 0)
                throw new InvalidOperationException("No files were dropped.");

            var dropId = Guid.NewGuid().ToString("N");
            var root = Path.Combine(AppPaths.TempDir, "drop-" + dropId);
            var payloadRoot = Path.Combine(root, "payload");
            var extractedRoot = Path.Combine(root, "extracted");
            Directory.CreateDirectory(payloadRoot);
            long totalBytes = 0;
            const long MaxTotalBytes = 16L * 1024L * 1024L;

            try
            {
                foreach (var item in files.OfType<JObject>())
                {
                    var relative = ((string)item["path"] ?? "").Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
                        throw new InvalidOperationException("Unsafe dropped path: " + relative);

                    var data = (string)item["dataBase64"] ?? "";
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(data); }
                    catch { throw new InvalidOperationException("Dropped file could not be decoded: " + relative); }
                    totalBytes += bytes.LongLength;
                    if (totalBytes > MaxTotalBytes)
                        throw new InvalidOperationException("The drag-and-drop package is larger than 16 MB. Use the Files or Folder button for large packages.");

                    var target = Path.GetFullPath(Path.Combine(payloadRoot, relative));
                    var payloadFull = Path.GetFullPath(payloadRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(payloadFull, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsafe dropped path: " + relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, bytes);
                }

                // Drag & Drop used to scan the materialized temp directory as a normal folder.
                // That meant a dropped ZIP stayed an opaque .zip file, while the Files button
                // correctly routed it through the ZIP importer. Expand dropped archives first,
                // then scan both the materialized payload and each archive extraction using the
                // same plugin parser rules as regular file/folder imports.
                var candidates = new List<PluginCandidate>();
                candidates.AddRange(_scanner.ScanDirectory(payloadRoot, payloadRoot, 0, 6));

                var zipFiles = Directory.GetFiles(payloadRoot, "*.zip", SearchOption.AllDirectories)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                for (var i = 0; i < zipFiles.Count; i++)
                {
                    var zip = zipFiles[i];
                    var safeName = Path.GetFileNameWithoutExtension(zip);
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "archive";
                    safeName = new string(safeName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
                    var extract = Path.Combine(extractedRoot, i.ToString("D3") + "-" + safeName);
                    Directory.CreateDirectory(extract);
                    PluginScanner.ExtractZipSafely(zip, extract);
                    candidates.AddRange(_scanner.ScanDirectory(extract, extract, 0, 6));
                }

                candidates = candidates
                    .GroupBy(c => c.Name + "|" + Path.GetFullPath(c.SourcePath), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count == 0)
                    throw new InvalidOperationException("No Vencord userplugins were detected. Expected a .ts/.tsx file containing definePlugin({ name: ... }), a folder with index.ts/index.tsx, or a ZIP containing one of those layouts.");

                var batch = new CandidateBatch
                {
                    TempRoot = root,
                    SourceKind = PluginSourceKind.DropSnapshot,
                    SourceReference = "Drag & Drop snapshot",
                    Candidates = candidates
                };
                foreach (var candidate in batch.Candidates)
                {
                    candidate.SourceKind = PluginSourceKind.DropSnapshot;
                    candidate.OriginReference = "Drag & Drop snapshot";
                }
                _batches[batch.Id] = batch;
                Raise("candidateBatch", ToBatchJson(batch));
                return batch;
            }
            catch
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
                throw;
            }
        }

        public async Task<CandidateBatch> AnalyzeGitHubAsync(string url)
        {
            return await RunExclusiveAsync("github", async token =>
            {
                ReportProgress(new OperationProgress { Stage = "github", Message = "Preparing GitHub repository", Percent = 10 });
                await _vencord.EnsureGitAsync(token).ConfigureAwait(false);
                var repo = await _github.CloneOrGetRepositoryAsync(_store.Config, url, token).ConfigureAwait(false);
                // Always refresh an existing cache before analyzing so Install really uses the latest source.
                await _github.UpdateRepositoryAsync(repo, token).ConfigureAwait(false);
                _store.Save();
                var batch = _github.AnalyzeRepository(repo, url);
                _batches[batch.Id] = batch;
                Raise("candidateBatch", ToBatchJson(batch));
                return batch;
            }).ConfigureAwait(false);
        }

        public async Task InstallCandidatesAsync(string batchId, IEnumerable<string> candidateIds)
        {
            CandidateBatch batch;
            if (!_batches.TryGetValue(batchId, out batch)) throw new InvalidOperationException("Candidate batch expired. Analyze the files/repository again.");
            var selected = batch.Candidates.Where(c => candidateIds.Contains(c.Id)).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("No plugins were selected.");
            var duplicateSelection = selected.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicateSelection != null)
                throw new InvalidOperationException("The selected package contains multiple plugins with the same Vencord name: " + duplicateSelection.Key + ". Select only one copy.");

            await RunExclusiveAsync("stageInstall", async token =>
            {
                var added = new List<ManagedPlugin>();
                var replaced = new List<Tuple<ManagedPlugin, int>>();
                var pendingBefore = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var c in selected)
                    {
                        var existing = _store.Config.Plugins.FirstOrDefault(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing == null) continue;
                        var oldIndex = _store.Config.Plugins.IndexOf(existing);
                        replaced.Add(Tuple.Create(existing, oldIndex));
                        JObject oldPending;
                        if (_store.Config.PendingPluginSettings.TryGetValue(existing.Name, out oldPending))
                            pendingBefore[existing.Name] = (JObject)oldPending.DeepClone();
                        _store.Config.Plugins.Remove(existing);
                        Log("Staging replacement for managed plugin: " + existing.Name);
                    }

                    ManagedRepository repo = null;
                    if (batch.SourceKind == PluginSourceKind.GitHub)
                        repo = _store.Config.Repositories.FirstOrDefault(r => r.Id == batch.RepositoryId);

                    foreach (var c in selected)
                    {
                        token.ThrowIfCancellationRequested();
                        if (c.Name.Equals("RVCPMIntegration", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("'RVCPMIntegration' is reserved for RVCPM's internal Vencord integration.");
                        if (c.TargetSuffix == "web" || c.TargetSuffix == "browser" || c.TargetSuffix == "vesktop")
                            throw new InvalidOperationException(c.Name + " targets '" + c.TargetSuffix + "' and cannot be loaded by a Discord Desktop build.");
                        if (c.TargetSuffix == "dev" && !_store.Config.DevBuild)
                            throw new InvalidOperationException(c.Name + " is a dev-target plugin. Enable Dev Build in RVCPM settings first.");

                        var p = _packages.InstallCandidate(_store.Config, batch, c, repo);
                        if (repo != null)
                        {
                            p.Version = await _github.GetVersionLabelAsync(repo, token).ConfigureAwait(false);
                            p.LastKnownCommit = repo.Commit;
                        }
                        added.Add(p);
                        var wasReplacement = replaced.Any(x => x.Item1.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
                        if (_store.Config.EnablePluginsAfterInstall && !wasReplacement)
                            _settings.StageEnabled(p.Name, true);
                    }

                    foreach (var old in replaced)
                        if (!_store.Config.PendingPackageCleanup.Any(x => x.Id == old.Item1.Id))
                            _store.Config.PendingPackageCleanup.Add(old.Item1);

                    _store.Config.PendingBuildChanges = true;
                    _store.Save();
                    CleanupBatch(batch);
                    CandidateBatch removed;
                    _batches.TryRemove(batch.Id, out removed);
                    Log("Plugin changes staged. Use Apply changes to rebuild Vencord once for all pending changes.");
                    Raise("stateChanged", GetState());
                }
                catch
                {
                    foreach (var p in added)
                    {
                        _store.Config.Plugins.RemoveAll(x => x.Id == p.Id);
                        _store.Config.PendingPluginSettings.Remove(p.Name);
                        _packages.RemovePackageFiles(p);
                    }
                    foreach (var old in replaced.OrderBy(x => x.Item2))
                    {
                        var index = Math.Max(0, Math.Min(old.Item2, _store.Config.Plugins.Count));
                        _store.Config.Plugins.Insert(index, old.Item1);
                        JObject pending;
                        if (pendingBefore.TryGetValue(old.Item1.Name, out pending))
                            _store.Config.PendingPluginSettings[old.Item1.Name] = pending;
                    }
                    _store.Save();
                    throw;
                }
            }).ConfigureAwait(false);
        }

        public void TogglePlugin(string pluginId, bool enabled)
        {
            var p = FindPlugin(pluginId);
            if (p.Required && !enabled) throw new InvalidOperationException("This plugin declares required: true and Vencord will force it enabled.");
            _settings.StageEnabled(p.Name, enabled);
            _store.Save();
            Raise("stateChanged", GetState());
        }

        public JObject GetPluginSettings(string pluginId)
        {
            var p = FindPlugin(pluginId);
            var effective = _settings.GetEffectivePluginObject(p.Name);
            var array = new JArray();
            foreach (var s in (p.Settings ?? new List<PluginSettingSchema>()).Where(IsManagerEditableSetting))
            {
                JToken value = effective[s.Key];
                if (value == null && s.DefaultValue != null) value = s.DefaultValue.DeepClone();
                if (value == null && s.Type == PluginSettingType.Select)
                {
                    var d = s.Options.FirstOrDefault(o => o.IsDefault);
                    if (d != null) value = d.Value.DeepClone();
                }
                array.Add(new JObject
                {
                    ["key"] = s.Key,
                    ["displayName"] = s.DisplayName,
                    ["description"] = s.Description,
                    ["type"] = s.Type.ToString(),
                    ["value"] = value ?? JValue.CreateNull(),
                    ["defaultValue"] = s.DefaultValue ?? JValue.CreateNull(),
                    ["restartNeeded"] = s.RestartNeeded,
                    ["unsupported"] = s.UnsupportedOutsideDiscord,
                    ["disabled"] = s.Disabled,
                    ["conditionalVisibility"] = s.ConditionalVisibility,
                    ["conditionalDisabled"] = s.ConditionalDisabled,
                    ["placeholder"] = s.Placeholder ?? "",
                    ["multiline"] = s.Multiline,
                    ["stickToMarkers"] = s.StickToMarkers,
                    ["options"] = JArray.FromObject(s.Options ?? new List<PluginSettingOption>()),
                    ["markers"] = JArray.FromObject(s.Markers ?? new List<double>())
                });
            }
            return new JObject
            {
                ["pluginId"] = p.Id,
                ["pluginName"] = p.Name,
                ["settings"] = array,
                ["hasCustomSettingsUi"] = HasCustomSettingsUi(p.Settings),
                ["customSettingsUiCount"] = CountCustomSettingsUi(p.Settings),
                ["internalSettingCount"] = (p.Settings ?? new List<PluginSettingSchema>()).Count(x => !x.UserFacing || x.Type == PluginSettingType.Custom)
            };
        }

        public void SavePluginSettings(string pluginId, JObject values)
        {
            var p = FindPlugin(pluginId);
            var patch = new JObject();
            foreach (var schema in (p.Settings ?? new List<PluginSettingSchema>()).Where(IsManagerEditableSetting))
            {
                if (schema.UnsupportedOutsideDiscord || values[schema.Key] == null) continue;
                patch[schema.Key] = values[schema.Key].DeepClone();
            }
            if (!patch.HasValues) return;
            _settings.StageValues(p.Name, patch);
            _store.Save();
            Raise("stateChanged", GetState());
        }

        public async Task RemovePluginAsync(string pluginId, bool removeSettings)
        {
            await RunExclusiveAsync("stageRemove", async token =>
            {
                token.ThrowIfCancellationRequested();
                var p = FindPlugin(pluginId);
                _store.Config.Plugins.RemoveAll(x => x.Id == p.Id);
                if (!_store.Config.PendingPackageCleanup.Any(x => x.Id == p.Id))
                    _store.Config.PendingPackageCleanup.Add(p);
                if (removeSettings) _settings.StageRemovePluginSettings(p.Name);
                _store.Config.PendingBuildChanges = true;
                _store.Save();
                Log("Plugin removal staged: " + p.Name);
                Raise("stateChanged", GetState());
                await Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        public async Task CheckUpdatesAsync()
        {
            await RunExclusiveAsync("checkUpdates", async token =>
            {
                await _vencord.EnsureGitAsync(token).ConfigureAwait(false);
                var repoGroups = _store.Config.Plugins.Where(p => p.SourceKind == PluginSourceKind.GitHub).GroupBy(p => p.RepositoryId).ToList();
                foreach (var group in repoGroups)
                {
                    var repo = _store.Config.Repositories.FirstOrDefault(r => r.Id == group.Key);
                    if (repo == null) continue;
                    ReportProgress(new OperationProgress { Stage = "updates", Message = "Checking " + repo.Owner + "/" + repo.Name, Percent = -1 });
                    var remote = await _github.GetRemoteCommitAsync(repo, token).ConfigureAwait(false);
                    foreach (var p in group) p.UpdateAvailable = !string.IsNullOrWhiteSpace(remote) && !remote.Equals(p.LastKnownCommit, StringComparison.OrdinalIgnoreCase);
                    repo.LastCheckedUtc = DateTime.UtcNow;
                }
                foreach (var p in _store.Config.Plugins.Where(p => p.SourceKind != PluginSourceKind.GitHub))
                    p.UpdateAvailable = _packages.CheckLocalUpdate(_store.Config, p);
                _store.Save();
                Raise("stateChanged", GetState());
            }).ConfigureAwait(false);
        }

        public async Task UpdatePluginAsync(string pluginId)
        {
            await RunExclusiveAsync("update", async token =>
            {
                var p = FindPlugin(pluginId);
                var rollback = CapturePluginRollback(new[] { p });
                try
                {
                    if (p.SourceKind == PluginSourceKind.GitHub)
                    {
                        await _vencord.EnsureGitAsync(token).ConfigureAwait(false);
                        var repo = _store.Config.Repositories.FirstOrDefault(r => r.Id == p.RepositoryId);
                        if (repo == null) throw new InvalidOperationException("Repository record not found.");
                        await _github.UpdateRepositoryAsync(repo, token).ConfigureAwait(false);
                        _packages.RefreshMetadata(_store.Config, p);
                        p.LastKnownCommit = repo.Commit;
                        p.Version = await _github.GetVersionLabelAsync(repo, token).ConfigureAwait(false);
                        p.Readme = repo.Readme;
                        p.GitHubDescription = repo.Description;
                        p.Description = string.IsNullOrWhiteSpace(repo.Description) ? p.PluginDescription : repo.Description;
                        p.UpdateAvailable = false;
                        p.LastUpdatedUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        _packages.UpdateFromLocalSource(_store.Config, p);
                    }
                    _store.Config.PendingBuildChanges = true;
                    _store.Save();
                    CleanupPluginRollback(rollback);
                    Log("Plugin update staged: " + p.Name);
                    Raise("stateChanged", GetState());
                }
                catch
                {
                    RestorePluginRollback(rollback);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        public async Task UpdateAllAsync()
        {
            await RunExclusiveAsync("updateAll", async token =>
            {
                var affected = _store.Config.Plugins
                    .Where(x => x.SourceKind == PluginSourceKind.GitHub || x.SourceKind == PluginSourceKind.LocalFile || x.SourceKind == PluginSourceKind.LocalFolder)
                    .ToList();
                var rollback = CapturePluginRollback(affected);
                try
                {
                    await _vencord.EnsureGitAsync(token).ConfigureAwait(false);
                    foreach (var repo in _store.Config.Repositories.ToList())
                    {
                        if (!_store.Config.Plugins.Any(p => p.RepositoryId == repo.Id)) continue;
                        await _github.UpdateRepositoryAsync(repo, token).ConfigureAwait(false);
                        foreach (var p in _store.Config.Plugins.Where(x => x.RepositoryId == repo.Id))
                        {
                            _packages.RefreshMetadata(_store.Config, p);
                            p.LastKnownCommit = repo.Commit;
                            p.Version = await _github.GetVersionLabelAsync(repo, token).ConfigureAwait(false);
                            p.Readme = repo.Readme;
                            p.GitHubDescription = repo.Description;
                            p.Description = string.IsNullOrWhiteSpace(repo.Description) ? p.PluginDescription : repo.Description;
                            p.UpdateAvailable = false;
                            p.LastUpdatedUtc = DateTime.UtcNow;
                        }
                    }
                    foreach (var p in _store.Config.Plugins.Where(x => x.SourceKind == PluginSourceKind.LocalFile || x.SourceKind == PluginSourceKind.LocalFolder).ToList())
                    {
                        if (_packages.CheckLocalUpdate(_store.Config, p)) _packages.UpdateFromLocalSource(_store.Config, p);
                    }
                    _store.Config.PendingBuildChanges = true;
                    _store.Save();
                    CleanupPluginRollback(rollback);
                    Log("Plugin updates staged. Apply changes when ready.");
                    Raise("stateChanged", GetState());
                }
                catch
                {
                    RestorePluginRollback(rollback);
                    throw;
                }
            }).ConfigureAwait(false);
        }

        public Task RebuildAsync(bool updateVencord)
        {
            return RunExclusiveAsync("rebuild", async token =>
            {
                await _vencord.BuildAndInjectAsync(updateVencord, token, null, true).ConfigureAwait(false);
                FinalizeAppliedChanges();
                Raise("stateChanged", GetState());
            });
        }

        public async Task ApplyPendingChangesAsync()
        {
            await RunExclusiveAsync("applyChanges", async token =>
            {
                if (!_store.Config.PendingBuildChanges && _store.Config.PendingPluginSettings.Count == 0 && !_store.Config.PendingRestart)
                {
                    ReportProgress(new OperationProgress { Stage = "done", Message = "No pending changes", Percent = 100, CanCancel = false });
                    return;
                }

                if (_store.Config.PendingBuildChanges)
                {
                    // Structural plugin changes require one combined Vencord rebuild/injection.
                    // Always refresh Vencord here so a staged install still fulfils RVCPM's latest-Vencord guarantee.
                    await _vencord.BuildAndInjectAsync(true, token, null, true).ConfigureAwait(false);
                }
                else
                {
                    var running = _discord.GetRunningDiscordKinds();
                    var wasRunning = running.Count > 0;
                    ReportProgress(new OperationProgress { Stage = "discord", Message = "Closing Discord automatically", Percent = 30, CanCancel = false });
                    if (wasRunning) await _discord.StopAsync("auto", token).ConfigureAwait(false);
                    ReportProgress(new OperationProgress { Stage = "discord", Message = "Applying staged plugin settings", Percent = 55, CanCancel = false });
                    _settings.FlushPending();
                    if (wasRunning)
                    {
                        ReportProgress(new OperationProgress { Stage = "discord", Message = "Starting Discord", Percent = 85, CanCancel = false });
                        if (!string.IsNullOrWhiteSpace(_store.Config.CustomDiscordLocation))
                            await _discord.StartCustomAsync(_store.Config.CustomDiscordLocation).ConfigureAwait(false);
                        else if ((_store.Config.DiscordBranch ?? "auto") == "auto" && running.Count > 0)
                            foreach (var kind in running) await _discord.StartAsync(kind).ConfigureAwait(false);
                        else
                            await _discord.StartAsync(_store.Config.DiscordBranch).ConfigureAwait(false);
                    }
                }

                FinalizeAppliedChanges();
                ReportProgress(new OperationProgress { Stage = "done", Message = "All pending changes applied", Percent = 100, CanCancel = false });
                Raise("stateChanged", GetState());
            }).ConfigureAwait(false);
        }

        public async Task RestartDiscordAsync()
        {
            await RunExclusiveAsync("restart", async token =>
            {
                var running = _discord.GetRunningDiscordKinds();
                ReportProgress(new OperationProgress { Stage = "discord", Message = "Closing Discord automatically", Percent = 20, CanCancel = false });
                await _discord.StopAsync("auto", token).ConfigureAwait(false);
                _settings.FlushPending();
                if (!string.IsNullOrWhiteSpace(_store.Config.CustomDiscordLocation))
                {
                    await _discord.StartCustomAsync(_store.Config.CustomDiscordLocation).ConfigureAwait(false);
                }
                else if ((_store.Config.DiscordBranch ?? "auto") == "auto" && running.Count > 0)
                {
                    foreach (var kind in running) await _discord.StartAsync(kind).ConfigureAwait(false);
                }
                else await _discord.StartAsync(_store.Config.DiscordBranch).ConfigureAwait(false);
                ReportProgress(new OperationProgress { Stage = "done", Message = "Discord restarted", Percent = 100, CanCancel = false });
                Raise("stateChanged", GetState());
            }).ConfigureAwait(false);
        }

        public void SaveAppSettings(JObject values)
        {
            if (values["language"] != null) _store.Config.Language = (string)values["language"] ?? "en";
            if (values["discordBranch"] != null) _store.Config.DiscordBranch = (string)values["discordBranch"] ?? "auto";
            if (values["customDiscordLocation"] != null) _store.Config.CustomDiscordLocation = (string)values["customDiscordLocation"] ?? "";
            if (values["autoUpdateVencord"] != null) _store.Config.AutoUpdateVencordBeforeBuild = (bool)values["autoUpdateVencord"];
            if (values["autoRestartAfterInstall"] != null) _store.Config.AutoRestartAfterInstall = (bool)values["autoRestartAfterInstall"];
            if (values["enableAfterInstall"] != null) _store.Config.EnablePluginsAfterInstall = (bool)values["enableAfterInstall"];
            if (values["devBuild"] != null)
            {
                var newDevBuild = (bool)values["devBuild"];
                if (newDevBuild != _store.Config.DevBuild) _store.Config.PendingBuildChanges = true;
                _store.Config.DevBuild = newDevBuild;
            }
            _store.Save();
            Raise("stateChanged", GetState());
        }

        public void OpenPluginSource(string pluginId)
        {
            var p = FindPlugin(pluginId);
            var path = _packages.GetSourcePath(_store.Config, p);
            if (File.Exists(path)) path = Path.GetDirectoryName(path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
            Process.Start(new ProcessStartInfo("explorer.exe", ProcessRunner.Quote(path)) { UseShellExecute = true });
        }

        public void OpenDataFolder()
        {
            Process.Start(new ProcessStartInfo("explorer.exe", ProcessRunner.Quote(AppPaths.Root)) { UseShellExecute = true });
        }

        public string GetLogText()
        {
            lock (_logLock)
            {
                try { return File.Exists(AppPaths.LogFile) ? File.ReadAllText(AppPaths.LogFile) : ""; }
                catch { return ""; }
            }
        }

        public void ClearLogs()
        {
            lock (_logLock)
            {
                try { File.WriteAllText(AppPaths.LogFile, ""); } catch { }
            }
            Raise("logCleared", null);
        }

        public void CancelOperation()
        {
            try { _operationCts?.Cancel(); } catch { }
            _runner.CancelCurrent();
        }

        private ManagedPlugin FindPlugin(string id)
        {
            var p = _store.Config.Plugins.FirstOrDefault(x => x.Id == id);
            if (p == null) throw new KeyNotFoundException("Plugin not found: " + id);
            return p;
        }

        private async Task RunExclusiveAsync(string name, Func<CancellationToken, Task> action)
        {
            await RunExclusiveAsync<object>(name, async token => { await action(token).ConfigureAwait(false); return null; }).ConfigureAwait(false);
        }

        private async Task<T> RunExclusiveAsync<T>(string name, Func<CancellationToken, Task<T>> action)
        {
            if (!await _operationLock.WaitAsync(0).ConfigureAwait(false))
                throw new InvalidOperationException("Another RVCPM operation is already running.");
            _operationCts = new CancellationTokenSource();
            Raise("operationStarted", new { name = name });
            try
            {
                var result = await action(_operationCts.Token).ConfigureAwait(false);
                Raise("operationFinished", new { name = name, ok = true });
                return result;
            }
            catch (OperationCanceledException)
            {
                Log("Operation cancelled.");
                Raise("operationFinished", new { name = name, ok = false, cancelled = true });
                throw;
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                Raise("operationFinished", new { name = name, ok = false, error = ex.Message });
                throw;
            }
            finally
            {
                _operationCts.Dispose();
                _operationCts = null;
                _operationLock.Release();
            }
        }

        private void ReportProgress(OperationProgress p)
        {
            Raise("progress", p);
        }

        private void Log(string line)
        {
            var text = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line;
            lock (_logLock)
            {
                try { File.AppendAllText(AppPaths.LogFile, text + Environment.NewLine); } catch { }
            }
            Raise("log", new { line = text });
        }

        private void Raise(string name, object data)
        {
            try { EventRaised?.Invoke(name, data); } catch { }
        }

        private JObject ToBatchJson(CandidateBatch batch)
        {
            var candidates = new JArray();
            foreach (var c in batch.Candidates)
            {
                candidates.Add(new JObject
                {
                    ["id"] = c.Id,
                    ["name"] = c.Name,
                    ["description"] = c.Description ?? "",
                    ["author"] = c.Author ?? "",
                    ["version"] = c.Version ?? "",
                    ["relativePath"] = c.RelativePath ?? "",
                    ["isFile"] = c.IsFile,
                    ["target"] = string.IsNullOrWhiteSpace(c.TargetSuffix) ? "desktop/default" : c.TargetSuffix,
                    ["settingsCount"] = CountManagerEditableSettings(c.Settings),
                    ["customSettingsUi"] = HasCustomSettingsUi(c.Settings),
                    ["customSettingsUiCount"] = CountCustomSettingsUi(c.Settings),
                    ["required"] = c.Required,
                    ["alreadyInstalled"] = _store.Config.Plugins.Any(p => p.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)),
                    ["warnings"] = JArray.FromObject(c.Warnings ?? new List<string>())
                });
            }
            return new JObject { ["batchId"] = batch.Id, ["sourceKind"] = batch.SourceKind.ToString(), ["source"] = batch.SourceReference ?? "", ["candidates"] = candidates };
        }

        private void CleanupBatch(CandidateBatch batch)
        {
            if (string.IsNullOrWhiteSpace(batch.TempRoot)) return;
            try { if (Directory.Exists(batch.TempRoot)) Directory.Delete(batch.TempRoot, true); } catch { }
        }

        private void CleanupUnreferencedRepositories()
        {
            var used = new HashSet<string>(_store.Config.Plugins.Where(p => !string.IsNullOrWhiteSpace(p.RepositoryId)).Select(p => p.RepositoryId), StringComparer.OrdinalIgnoreCase);
            var unused = _store.Config.Repositories.Where(r => !used.Contains(r.Id)).ToList();
            foreach (var repo in unused)
            {
                _store.Config.Repositories.Remove(repo);
                try { if (Directory.Exists(repo.LocalPath)) Directory.Delete(repo.LocalPath, true); } catch { }
            }
            if (unused.Count > 0) _store.Save();
        }

        private void RemoveUnusedRepository(string repositoryId)
        {
            if (string.IsNullOrWhiteSpace(repositoryId)) return;
            if (_store.Config.Plugins.Any(p => p.RepositoryId == repositoryId)) return;
            var repo = _store.Config.Repositories.FirstOrDefault(r => r.Id == repositoryId);
            if (repo == null) return;
            _store.Config.Repositories.Remove(repo);
            try { if (Directory.Exists(repo.LocalPath)) Directory.Delete(repo.LocalPath, true); } catch { }
        }

        private sealed class PluginRollback
        {
            public string Root { get; set; } = "";
            public List<Tuple<int, ManagedPlugin>> Records { get; set; } = new List<Tuple<int, ManagedPlugin>>();
        }

        private PluginRollback CapturePluginRollback(IEnumerable<ManagedPlugin> plugins)
        {
            var rollback = new PluginRollback { Root = Path.Combine(AppPaths.TempDir, "rollback-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(rollback.Root);
            foreach (var plugin in plugins.Distinct().ToList())
            {
                var index = _store.Config.Plugins.FindIndex(x => x.Id == plugin.Id);
                if (index < 0) continue;
                var clone = JsonConvert.DeserializeObject<ManagedPlugin>(JsonConvert.SerializeObject(plugin));
                rollback.Records.Add(Tuple.Create(index, clone));

                var packageRoot = Path.Combine(AppPaths.PackagesDir, plugin.Id);
                if (Directory.Exists(packageRoot))
                    CopyDirectoryForRollback(packageRoot, Path.Combine(rollback.Root, plugin.Id));
            }
            return rollback;
        }

        private void RestorePluginRollback(PluginRollback rollback)
        {
            if (rollback == null) return;
            foreach (var item in rollback.Records.OrderBy(x => x.Item1))
            {
                var record = item.Item2;
                var currentIndex = _store.Config.Plugins.FindIndex(x => x.Id == record.Id);
                if (currentIndex >= 0) _store.Config.Plugins[currentIndex] = record;
                else _store.Config.Plugins.Insert(Math.Min(item.Item1, _store.Config.Plugins.Count), record);

                var packageRoot = Path.Combine(AppPaths.PackagesDir, record.Id);
                var backupRoot = Path.Combine(rollback.Root, record.Id);
                try
                {
                    if (Directory.Exists(packageRoot)) Directory.Delete(packageRoot, true);
                    if (Directory.Exists(backupRoot)) CopyDirectoryForRollback(backupRoot, packageRoot);
                }
                catch (Exception ex) { Log("Could not restore package snapshot for " + record.Name + ": " + ex.Message); }
            }
            _store.Save();
            CleanupPluginRollback(rollback);
            Raise("stateChanged", GetState());
        }

        private static void CleanupPluginRollback(PluginRollback rollback)
        {
            if (rollback == null || string.IsNullOrWhiteSpace(rollback.Root)) return;
            try { if (Directory.Exists(rollback.Root)) Directory.Delete(rollback.Root, true); } catch { }
        }

        private static void CopyDirectoryForRollback(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectoryForRollback(dir, Path.Combine(destination, Path.GetFileName(dir)));
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        private void RefreshManagedPluginMetadata()
        {
            var changed = false;
            foreach (var plugin in _store.Config.Plugins.ToList())
            {
                try { if (_packages.RefreshInstalledMetadata(_store.Config, plugin)) changed = true; }
                catch (Exception ex) { Log("Could not refresh metadata for " + plugin.Name + ": " + ex.Message); }
            }
            if (changed)
            {
                _store.Save();
                Log("Refreshed plugin metadata/settings schema using RVCPM v0.1.6 parser.");
            }
        }

        private void FinalizeAppliedChanges()
        {
            foreach (var old in _store.Config.PendingPackageCleanup.ToList())
            {
                _packages.RemovePackageFiles(old);
                RemoveUnusedRepository(old.RepositoryId);
            }
            _store.Config.PendingPackageCleanup.Clear();
            _store.Config.PendingBuildChanges = false;
            _store.Config.PendingRestart = false;
            _store.Save();
        }

        private static bool IsManagerEditableSetting(PluginSettingSchema setting)
        {
            return setting != null && setting.UserFacing && setting.EditableInManager && !setting.Hidden;
        }

        private static int CountManagerEditableSettings(IEnumerable<PluginSettingSchema> settings)
        {
            return settings == null ? 0 : settings.Count(IsManagerEditableSetting);
        }

        private static bool HasCustomSettingsUi(IEnumerable<PluginSettingSchema> settings)
        {
            return settings != null && settings.Any(x => x.UserFacing && !x.Hidden && x.Type == PluginSettingType.Component);
        }

        private static int CountCustomSettingsUi(IEnumerable<PluginSettingSchema> settings)
        {
            return settings == null ? 0 : settings.Count(x => x.UserFacing && !x.Hidden && x.Type == PluginSettingType.Component);
        }

        private static string Short(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return value.Substring(0, Math.Min(8, value.Length));
        }

        public void Dispose()
        {
            CancelOperation();
            _operationLock.Dispose();
        }
    }
}
