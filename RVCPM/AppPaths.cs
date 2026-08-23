using System;
using System.IO;

namespace RVCPM
{
    internal static class AppPaths
    {
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RVCPM");
        public static readonly string ConfigDir = Path.Combine(Root, "config");
        public static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
        public static readonly string PackagesDir = Path.Combine(Root, "packages");
        public static readonly string RepositoriesDir = Path.Combine(Root, "repositories");
        public static readonly string VencordDir = Path.Combine(Root, "vencord");
        public static readonly string TempDir = Path.Combine(Root, "temp");
        public static readonly string LogsDir = Path.Combine(Root, "logs");
        public static readonly string LogFile = Path.Combine(LogsDir, "rvcpm.log");

        public static string DefaultVencordSettingsFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vencord", "settings", "settings.json");
            }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(PackagesDir);
            Directory.CreateDirectory(RepositoriesDir);
            Directory.CreateDirectory(TempDir);
            Directory.CreateDirectory(LogsDir);
        }

        public static void CleanupStaleTemp()
        {
            try
            {
                if (!Directory.Exists(TempDir)) return;
                var cutoff = DateTime.UtcNow.AddDays(-2);
                foreach (var dir in Directory.GetDirectories(TempDir))
                {
                    try
                    {
                        var stamp = Directory.GetLastWriteTimeUtc(dir);
                        if (stamp < cutoff) Directory.Delete(dir, true);
                    }
                    catch { }
                }
                foreach (var file in Directory.GetFiles(TempDir))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
