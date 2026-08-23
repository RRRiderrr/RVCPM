using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class VencordSettingsService
    {
        private const string RemoveMarker = "__rvcpm_remove_plugin_settings";
        private readonly ConfigStore _store;
        private readonly DiscordService _discord;
        private readonly Action<string> _log;
        private readonly object _sync = new object();

        public VencordSettingsService(ConfigStore store, DiscordService discord, Action<string> log)
        {
            _store = store;
            _discord = discord;
            _log = log;
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
                if (!_discord.IsAnyDiscordRunning())
                {
                    ApplyPatchToFile(pluginName, patch);
                    return;
                }

                JObject existing;
                if (!_store.Config.PendingPluginSettings.TryGetValue(pluginName, out existing))
                {
                    existing = new JObject();
                    _store.Config.PendingPluginSettings[pluginName] = existing;
                }
                foreach (var p in patch.Properties()) existing[p.Name] = p.Value.DeepClone();
                _store.Config.PendingRestart = true;
                _store.Save();
                _log("Staged Vencord settings change for " + pluginName + "; Discord restart required.");
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
        }
    }
}
