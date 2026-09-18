using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class VencordSettingsService : IDisposable
    {
        private const string RemoveMarker = "__rvcpm_remove_plugin_settings";
        private readonly ConfigStore _store;
        private readonly DiscordService _discord;
        private readonly Action<string> _log;
        private readonly object _sync = new object();
        private FileSystemWatcher _watcher;
        private Timer _debounceTimer;
        private Timer _pollTimer;
        private string _lastObservedRaw;
        private JObject _lastObservedRoot;
        private bool _disposed;

        public sealed class SettingsChangeInfo
        {
            public Dictionary<string, List<string>> PluginKeys { get; private set; }
                = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            public IEnumerable<string> PluginNames { get { return PluginKeys.Keys; } }
        }

        public event Action<SettingsChangeInfo> SettingsChanged;

        public VencordSettingsService(ConfigStore store, DiscordService discord, Action<string> log)
        {
            _store = store;
            _discord = discord;
            _log = log;
            StartLiveWatcher();
        }

        public string SettingsFile { get { return AppPaths.DefaultVencordSettingsFile; } }

        internal sealed class Snapshot
        {
            public bool FileExisted { get; set; }
            public string FileContents { get; set; } = "";
            public System.Collections.Generic.Dictionary<string, JObject> Pending { get; set; }
                = new System.Collections.Generic.Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            public bool PendingRestart { get; set; }
        }

        private void StartLiveWatcher()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFile);
                if (string.IsNullOrWhiteSpace(dir)) return;
                Directory.CreateDirectory(dir);

                lock (_sync)
                {
                    _lastObservedRaw = ReadRawSettings();
                    _lastObservedRoot = ParseRootOrEmpty(_lastObservedRaw);
                }

                _watcher = new FileSystemWatcher(dir, Path.GetFileName(SettingsFile));
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
                _watcher.Changed += OnSettingsFileSignal;
                _watcher.Created += OnSettingsFileSignal;
                _watcher.Deleted += OnSettingsFileSignal;
                _watcher.Renamed += OnSettingsFileRenamed;
                _watcher.EnableRaisingEvents = true;

                // FileSystemWatcher is fast, but Windows can occasionally coalesce or miss replace events.
                // The low-frequency poll is only a safety net; UI updates normally come from the watcher.
                _pollTimer = new Timer(_ => DetectLiveChange(), null, 1500, 1500);
                _log("Live Vencord settings monitor started.");
            }
            catch (Exception ex)
            {
                _log("Could not start live Vencord settings monitor: " + ex.Message);
            }
        }

        private void OnSettingsFileSignal(object sender, FileSystemEventArgs e)
        {
            ScheduleLiveRead();
        }

        private void OnSettingsFileRenamed(object sender, RenamedEventArgs e)
        {
            ScheduleLiveRead();
        }

        private void ScheduleLiveRead()
        {
            if (_disposed) return;
            lock (_sync)
            {
                if (_debounceTimer == null)
                    _debounceTimer = new Timer(_ => DetectLiveChange(), null, 180, Timeout.Infinite);
                else
                    _debounceTimer.Change(180, Timeout.Infinite);
            }
        }

        private void DetectLiveChange()
        {
            if (_disposed) return;

            SettingsChangeInfo info = null;
            try
            {
                lock (_sync)
                {
                    var raw = ReadRawSettings();
                    if (string.Equals(raw, _lastObservedRaw, StringComparison.Ordinal)) return;

                    JObject next;
                    if (!TryParseRoot(raw, out next))
                    {
                        // Vencord can be in the middle of writing the file when the filesystem event arrives.
                        // Keep the old baseline and retry shortly instead of reporting a bogus empty state.
                        if (_debounceTimer != null) _debounceTimer.Change(220, Timeout.Infinite);
                        return;
                    }

                    var previous = _lastObservedRoot ?? new JObject { ["plugins"] = new JObject() };
                    info = DiffPluginSettings(previous, next);
                    _lastObservedRaw = raw;
                    _lastObservedRoot = (JObject)next.DeepClone();
                }
            }
            catch (Exception ex)
            {
                _log("Live settings refresh failed: " + ex.Message);
                return;
            }

            if (info != null && info.PluginKeys.Count > 0)
            {
                var handler = SettingsChanged;
                if (handler != null) handler(info);
            }
        }

        private string ReadRawSettings()
        {
            if (!File.Exists(SettingsFile)) return null;
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    using (var stream = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd();
                }
                catch (IOException)
                {
                    Thread.Sleep(35);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(35);
                }
            }
            return _lastObservedRaw;
        }

        private static bool TryParseRoot(string raw, out JObject root)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                root = new JObject { ["plugins"] = new JObject() };
                return true;
            }
            try
            {
                root = JObject.Parse(raw);
                if (!(root["plugins"] is JObject)) root["plugins"] = new JObject();
                return true;
            }
            catch
            {
                root = null;
                return false;
            }
        }

        private static JObject ParseRootOrEmpty(string raw)
        {
            JObject root;
            return TryParseRoot(raw, out root) ? root : new JObject { ["plugins"] = new JObject() };
        }

        private static SettingsChangeInfo DiffPluginSettings(JObject previousRoot, JObject nextRoot)
        {
            var info = new SettingsChangeInfo();
            var before = previousRoot["plugins"] as JObject ?? new JObject();
            var after = nextRoot["plugins"] as JObject ?? new JObject();
            var names = new HashSet<string>(before.Properties().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            names.UnionWith(after.Properties().Select(x => x.Name));

            foreach (var name in names)
            {
                var oldObj = before[name] as JObject ?? new JObject();
                var newObj = after[name] as JObject ?? new JObject();
                if (JToken.DeepEquals(oldObj, newObj)) continue;

                var keys = new HashSet<string>(oldObj.Properties().Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                keys.UnionWith(newObj.Properties().Select(x => x.Name));
                var changed = keys.Where(k => !JToken.DeepEquals(oldObj[k], newObj[k])).ToList();
                if (changed.Count > 0) info.PluginKeys[name] = changed;
            }
            return info;
        }

        public void ReconcilePendingWithLiveChange(SettingsChangeInfo info, IEnumerable<string> managedPluginNames)
        {
            if (info == null) return;
            var managed = new HashSet<string>(managedPluginNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var changedPending = false;

            lock (_sync)
            {
                foreach (var pair in info.PluginKeys)
                {
                    if (!managed.Contains(pair.Key)) continue;
                    JObject pending;
                    if (!_store.Config.PendingPluginSettings.TryGetValue(pair.Key, out pending)) continue;
                    if ((bool?)pending[RemoveMarker] == true) continue;

                    foreach (var key in pair.Value)
                    {
                        var prop = pending.Properties().FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                        if (prop != null)
                        {
                            prop.Remove();
                            changedPending = true;
                        }
                    }
                    if (!pending.HasValues)
                        _store.Config.PendingPluginSettings.Remove(pair.Key);
                }

                if (changedPending)
                {
                    _store.Config.PendingRestart = _store.Config.PendingPluginSettings.Count > 0;
                    _store.Save();
                    _log("Live Discord/Vencord changes superseded matching staged RVCPM settings.");
                }
            }
        }

        public Snapshot CaptureSnapshot()
        {
            lock (_sync)
            {
                var snapshot = new Snapshot
                {
                    FileExisted = File.Exists(SettingsFile),
                    PendingRestart = _store.Config.PendingRestart
                };
                if (snapshot.FileExisted)
                {
                    try { snapshot.FileContents = File.ReadAllText(SettingsFile); }
                    catch { snapshot.FileContents = ""; }
                }
                foreach (var item in _store.Config.PendingPluginSettings)
                    snapshot.Pending[item.Key] = (JObject)item.Value.DeepClone();
                return snapshot;
            }
        }

        public void RestoreSnapshot(Snapshot snapshot)
        {
            if (snapshot == null) return;
            lock (_sync)
            {
                _store.Config.PendingPluginSettings.Clear();
                foreach (var item in snapshot.Pending)
                    _store.Config.PendingPluginSettings[item.Key] = (JObject)item.Value.DeepClone();
                _store.Config.PendingRestart = snapshot.PendingRestart;

                var dir = Path.GetDirectoryName(SettingsFile);
                if (snapshot.FileExisted)
                {
                    Directory.CreateDirectory(dir);
                    var tmp = SettingsFile + ".rvcpm.restore.tmp";
                    File.WriteAllText(tmp, snapshot.FileContents ?? "");
                    if (File.Exists(SettingsFile))
                    {
                        try { File.Replace(tmp, SettingsFile, SettingsFile + ".rvcpm.restore.bak", true); }
                        catch { File.Copy(tmp, SettingsFile, true); File.Delete(tmp); }
                    }
                    else File.Move(tmp, SettingsFile);
                }
                else
                {
                    try { if (File.Exists(SettingsFile)) File.Delete(SettingsFile); } catch { }
                }
                _lastObservedRaw = snapshot.FileExisted ? (snapshot.FileContents ?? "") : null;
                _lastObservedRoot = ParseRootOrEmpty(_lastObservedRaw);
                _store.Save();
                _log("Restored Vencord settings after a failed RVCPM operation.");
            }
        }

        public void RestorePendingSnapshot(Snapshot snapshot)
        {
            if (snapshot == null) return;
            lock (_sync)
            {
                _store.Config.PendingPluginSettings.Clear();
                foreach (var item in snapshot.Pending)
                    _store.Config.PendingPluginSettings[item.Key] = (JObject)item.Value.DeepClone();
                _store.Config.PendingRestart = snapshot.PendingRestart;
                _store.Save();
            }
        }

        public JObject GetEffectivePluginObject(string pluginName)
        {
            lock (_sync)
            {
                var root = ReadRoot();
                var plugins = root["plugins"] as JObject;
                var current = plugins != null && plugins[pluginName] is JObject ? (JObject)((JObject)plugins[pluginName]).DeepClone() : new JObject();
                JObject pending;
                if (_store.Config.PendingPluginSettings.TryGetValue(pluginName, out pending))
                {
                    if ((bool?)pending[RemoveMarker] == true) return new JObject();
                    foreach (var prop in pending.Properties())
                    {
                        if (prop.Name == RemoveMarker) continue;
                        current[prop.Name] = prop.Value.DeepClone();
                    }
                }
                return current;
            }
        }

        public bool GetEnabled(ManagedPlugin plugin)
        {
            var obj = GetEffectivePluginObject(plugin.Name);
            return (bool?)obj["enabled"] ?? plugin.EnabledByDefault;
        }

        public void StageValues(string pluginName, JObject patch)
        {
            lock (_sync)
            {
                JObject existing;
                if (!_store.Config.PendingPluginSettings.TryGetValue(pluginName, out existing))
                {
                    existing = new JObject();
                    _store.Config.PendingPluginSettings[pluginName] = existing;
                }
                foreach (var p in patch.Properties()) existing[p.Name] = p.Value.DeepClone();
                _store.Config.PendingRestart = true;
                _store.Save();
                _log("Staged Vencord settings change for " + pluginName + "; waiting for Apply changes.");
            }
        }

        public void StageEnabled(string pluginName, bool enabled)
        {
            StageValues(pluginName, new JObject { ["enabled"] = enabled });
        }

        public void StageRemovePluginSettings(string pluginName)
        {
            StageValues(pluginName, new JObject { [RemoveMarker] = true });
        }

        public void FlushPending()
        {
            lock (_sync)
            {
                if (_discord.IsAnyDiscordRunning())
                    throw new InvalidOperationException("Discord must be stopped before applying pending Vencord settings.");

                if (_store.Config.PendingPluginSettings.Count == 0)
                {
                    _store.Config.PendingRestart = false;
                    _store.Save();
                    return;
                }

                var root = ReadRoot();
                var plugins = root["plugins"] as JObject;
                if (plugins == null) root["plugins"] = plugins = new JObject();

                foreach (var item in _store.Config.PendingPluginSettings)
                {
                    var patch = item.Value;
                    if ((bool?)patch[RemoveMarker] == true)
                    {
                        plugins.Remove(item.Key);
                        continue;
                    }

                    var obj = plugins[item.Key] as JObject;
                    if (obj == null) plugins[item.Key] = obj = new JObject();
                    foreach (var p in patch.Properties())
                    {
                        if (p.Name == RemoveMarker) continue;
                        obj[p.Name] = p.Value.DeepClone();
                    }
                }

                WriteRoot(root);
                _store.Config.PendingPluginSettings.Clear();
                _store.Config.PendingRestart = false;
                _store.Save();
                _log("Applied pending Vencord settings.");
            }
        }

        public void ApplyImmediately(string pluginName, JObject patch)
        {
            lock (_sync)
            {
                if (_discord.IsAnyDiscordRunning())
                    throw new InvalidOperationException("Refusing to edit Vencord settings while Discord is running. Stage the change or stop Discord first.");
                ApplyPatchToFile(pluginName, patch);
            }
        }

        private void ApplyPatchToFile(string pluginName, JObject patch)
        {
            var root = ReadRoot();
            var plugins = root["plugins"] as JObject;
            if (plugins == null) root["plugins"] = plugins = new JObject();

            if ((bool?)patch[RemoveMarker] == true)
            {
                plugins.Remove(pluginName);
            }
            else
            {
                var obj = plugins[pluginName] as JObject;
                if (obj == null) plugins[pluginName] = obj = new JObject();
                foreach (var p in patch.Properties())
                {
                    if (p.Name == RemoveMarker) continue;
                    obj[p.Name] = p.Value.DeepClone();
                }
            }
            WriteRoot(root);
        }

        private JObject ReadRoot()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return new JObject { ["plugins"] = new JObject() };
                return JObject.Parse(File.ReadAllText(SettingsFile));
            }
            catch (Exception ex)
            {
                _log("Could not parse Vencord settings.json; a backup will be created before writing. " + ex.Message);
                return new JObject { ["plugins"] = new JObject() };
            }
        }

        private void WriteRoot(JObject root)
        {
            var dir = Path.GetDirectoryName(SettingsFile);
            Directory.CreateDirectory(dir);
            if (File.Exists(SettingsFile))
            {
                try { File.Copy(SettingsFile, SettingsFile + ".rvcpm.bak", true); } catch { }
            }
            var tmp = SettingsFile + ".rvcpm.tmp";
            File.WriteAllText(tmp, root.ToString(Formatting.Indented));
            if (File.Exists(SettingsFile))
            {
                try { File.Replace(tmp, SettingsFile, SettingsFile + ".rvcpm.prev", true); }
                catch { File.Copy(tmp, SettingsFile, true); File.Delete(tmp); }
            }
            else File.Move(tmp, SettingsFile);

            // Keep the live watcher baseline in sync with RVCPM's own atomic write so it does not
            // report our own apply operation as an external Discord change.
            _lastObservedRaw = root.ToString(Formatting.Indented);
            _lastObservedRoot = (JObject)root.DeepClone();
        }

        public void Dispose()
        {
            _disposed = true;
            try { if (_watcher != null) _watcher.Dispose(); } catch { }
            try { if (_debounceTimer != null) _debounceTimer.Dispose(); } catch { }
            try { if (_pollTimer != null) _pollTimer.Dispose(); } catch { }
            _watcher = null;
            _debounceTimer = null;
            _pollTimer = null;
        }
    }
}
