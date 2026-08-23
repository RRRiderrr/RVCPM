using System;
using System.IO;
using Newtonsoft.Json;

namespace RVCPM
{
    internal sealed class ConfigStore
    {
        private readonly object _sync = new object();
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public AppConfig Config { get; private set; }

        public ConfigStore()
        {
            Config = Load();
        }

        private AppConfig Load()
        {
            try
            {
                if (!File.Exists(AppPaths.ConfigFile))
                    return new AppConfig();

                var json = File.ReadAllText(AppPaths.ConfigFile);
                var cfg = JsonConvert.DeserializeObject<AppConfig>(json, _settings) ?? new AppConfig();
                if (cfg.Plugins == null) cfg.Plugins = new System.Collections.Generic.List<ManagedPlugin>();
                if (cfg.Repositories == null) cfg.Repositories = new System.Collections.Generic.List<ManagedRepository>();
                if (cfg.PendingPluginSettings == null)
                    cfg.PendingPluginSettings = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JObject>(StringComparer.OrdinalIgnoreCase);
                if (cfg.PendingPackageCleanup == null) cfg.PendingPackageCleanup = new System.Collections.Generic.List<ManagedPlugin>();
                return cfg;
            }
            catch
            {
                try
                {
                    if (File.Exists(AppPaths.ConfigFile))
                        File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                }
                catch { }
                return new AppConfig();
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                AppPaths.EnsureDirectories();
                var tmp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Config, _settings));
                if (File.Exists(AppPaths.ConfigFile))
                {
                    try
                    {
                        File.Replace(tmp, AppPaths.ConfigFile, AppPaths.ConfigFile + ".bak", true);
                    }
                    catch
                    {
                        File.Copy(tmp, AppPaths.ConfigFile, true);
                        File.Delete(tmp);
                    }
                }
                else
                {
                    File.Move(tmp, AppPaths.ConfigFile);
                }
            }
        }
    }
}
