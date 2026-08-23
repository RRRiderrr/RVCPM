using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RVCPM.Services
{
    internal sealed class PackageService
    {
        private readonly PluginParser _parser = new PluginParser();
        private readonly Action<string> _log;
        private static readonly HashSet<string> IgnoreDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", ".vs", ".idea", "dist", "build", "out", "coverage"
        };

        public PackageService(Action<string> log)
        {
            _log = log;
        }

        public ManagedPlugin InstallCandidate(AppConfig config, CandidateBatch batch, PluginCandidate candidate, ManagedRepository repo)
        {
            if (config.Plugins.Any(p => p.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A managed plugin named '" + candidate.Name + "' is already installed. Remove or update it instead of installing a duplicate name.");

            var plugin = new ManagedPlugin
            {
                Name = candidate.Name,
                PluginDescription = candidate.Description ?? "",
                Description = candidate.Description ?? "",
                Author = candidate.Author ?? "",
                Version = candidate.Version ?? "",
                EnabledByDefault = candidate.EnabledByDefault,
                Required = candidate.Required,
                RequiresRestart = candidate.RequiresRestart,
                HasSettings = HasUserFacingSettings(candidate.Settings),
                Settings = candidate.Settings ?? new List<PluginSettingSchema>(),
                Dependencies = candidate.Dependencies ?? new List<string>(),
                SourceIsFile = candidate.IsFile,
                EntryExtension = string.IsNullOrWhiteSpace(candidate.Extension) ? ".ts" : candidate.Extension,
                TargetSuffix = candidate.TargetSuffix ?? "",
                RelativePath = candidate.RelativePath ?? "",
                SourceReference = batch.SourceReference ?? candidate.SourcePath,
                InstalledUtc = DateTime.UtcNow
            };
            plugin.TargetFolder = MakeTargetFolder(plugin);

            if (batch.SourceKind == PluginSourceKind.GitHub)
            {
                if (repo == null) throw new InvalidOperationException("GitHub repository record is missing.");
                plugin.SourceKind = PluginSourceKind.GitHub;
                plugin.RepositoryId = repo.Id;
                plugin.GitHubUrl = repo.Url;
                plugin.GitHubDescription = repo.Description ?? "";
                plugin.Description = string.IsNullOrWhiteSpace(repo.Description) ? plugin.PluginDescription : repo.Description;
                plugin.Readme = repo.Readme ?? "";
                plugin.LastKnownCommit = repo.Commit ?? "";
                // Keep an immutable installed snapshot. The repository cache is only an update source;
                // analyzing/fetching a repository must never silently change the next Vencord build.
                SnapshotSource(candidate, plugin);
            }
            else
            {
                plugin.SourceKind = candidate.SourceKind == PluginSourceKind.Zip || candidate.SourceKind == PluginSourceKind.DropSnapshot
                    ? candidate.SourceKind
                    : (Directory.Exists(candidate.SourcePath) ? PluginSourceKind.LocalFolder : PluginSourceKind.LocalFile);
                SnapshotSource(candidate, plugin);
                plugin.SourceReference = (plugin.SourceKind == PluginSourceKind.Zip || plugin.SourceKind == PluginSourceKind.DropSnapshot)
                    ? (string.IsNullOrWhiteSpace(candidate.OriginReference) ? batch.SourceReference : candidate.OriginReference)
                    : candidate.SourcePath;
            }

            plugin.ContentHash = HashUtil.Sha256Path(GetSourcePath(config, plugin));
            if (string.IsNullOrWhiteSpace(plugin.Version))
                plugin.Version = plugin.SourceKind == PluginSourceKind.GitHub
                    ? ShortCommit(plugin.LastKnownCommit)
                    : "local";
            config.Plugins.Add(plugin);
            _log("Added managed plugin: " + plugin.Name);
            return plugin;
        }

        public string GetSourcePath(AppConfig config, ManagedPlugin plugin)
        {
            // v0.1.0 stores every installed plugin as its own package snapshot, including GitHub plugins.
            // Keep a fallback for configs created by early development builds where GitHub plugins pointed
            // directly at the repository cache.
            if (!string.IsNullOrWhiteSpace(plugin.PackagePath) && (File.Exists(plugin.PackagePath) || Directory.Exists(plugin.PackagePath)))
                return plugin.PackagePath;

            if (plugin.SourceKind != PluginSourceKind.GitHub) return plugin.PackagePath;
            return GetGitHubRepositorySourcePath(config, plugin);
        }

        public void SyncPluginToVencord(AppConfig config, ManagedPlugin plugin, string userPluginsDir)
        {
            var source = GetSourcePath(config, plugin);
            if (!File.Exists(source) && !Directory.Exists(source))
                throw new FileNotFoundException("Source files for plugin '" + plugin.Name + "' are missing: " + source);

            var target = Path.Combine(userPluginsDir, plugin.TargetFolder);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(target);

            if (plugin.SourceIsFile || File.Exists(source))
            {
                var ext = Path.GetExtension(source);
                if (!ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase))
                    ext = plugin.EntryExtension;
                File.Copy(source, Path.Combine(target, "index" + ext), true);
                CopyRelativeDependencies(source, target);
            }
            else
            {
                CopyDirectory(source, target);
                var entry = PluginParser.ResolveEntry(target);
                if (entry == null)
                    throw new InvalidOperationException("Plugin folder '" + plugin.Name + "' no longer has index.ts or index.tsx.");
            }

            File.WriteAllText(Path.Combine(target, ".rvcpm-managed"), plugin.Id + "\n" + plugin.Name);
        }

        public bool CheckLocalUpdate(AppConfig config, ManagedPlugin plugin)
        {
            if (plugin.SourceKind == PluginSourceKind.GitHub) return plugin.UpdateAvailable;
            if (plugin.SourceKind == PluginSourceKind.Zip || plugin.SourceKind == PluginSourceKind.DropSnapshot) return false;
            var original = plugin.SourceReference;
            if (!File.Exists(original) && !Directory.Exists(original)) return false;
            var hash = HashUtil.Sha256Path(original);
            return !string.IsNullOrWhiteSpace(hash) && !hash.Equals(plugin.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        public void UpdateFromLocalSource(AppConfig config, ManagedPlugin plugin)
        {
            if (plugin.SourceKind == PluginSourceKind.Zip || plugin.SourceKind == PluginSourceKind.DropSnapshot)
                throw new InvalidOperationException("Snapshot imports cannot be auto-updated. Import the newer files/package again to replace them.");
            var original = plugin.SourceReference;
            if (!File.Exists(original) && !Directory.Exists(original))
                throw new FileNotFoundException("Original local source no longer exists: " + original);

            PluginCandidate candidate;
            if (File.Exists(original))
            {
                candidate = _parser.ParseCandidate(original, Path.GetFileName(original), true);
            }
            else
            {
                var scanner = new PluginScanner();
                candidate = scanner.ScanDirectory(original, original, 0, 6).FirstOrDefault(x => x.Name.Equals(plugin.Name, StringComparison.OrdinalIgnoreCase));
            }
            if (candidate == null) throw new InvalidOperationException("Could not find plugin '" + plugin.Name + "' in its original local source.");

            ReplaceSnapshot(candidate, plugin);
            ApplyCandidateMetadata(plugin, candidate);
            plugin.ContentHash = HashUtil.Sha256Path(plugin.PackagePath);
            plugin.LastUpdatedUtc = DateTime.UtcNow;
            plugin.UpdateAvailable = false;
        }

        public bool RefreshInstalledMetadata(AppConfig config, ManagedPlugin plugin)
        {
            var source = GetSourcePath(config, plugin);
            if (!File.Exists(source) && !Directory.Exists(source)) return false;
            var candidate = _parser.ParseCandidate(source, plugin.RelativePath, File.Exists(source) || plugin.SourceIsFile);
            if (candidate == null && Directory.Exists(source))
                candidate = _parser.ParseCandidate(source, plugin.RelativePath, false);
            if (candidate == null || !candidate.Name.Equals(plugin.Name, StringComparison.Ordinal)) return false;

            var oldSignature = SettingsSignature(plugin.Settings);
            var oldHas = plugin.HasSettings;
            ApplyCandidateMetadata(plugin, candidate);
            return oldSignature != SettingsSignature(plugin.Settings) || oldHas != plugin.HasSettings;
        }

        public void RefreshMetadata(AppConfig config, ManagedPlugin plugin)
        {
            string source;
            if (plugin.SourceKind == PluginSourceKind.GitHub)
                source = GetGitHubRepositorySourcePath(config, plugin);
            else
                source = GetSourcePath(config, plugin);

            if (!File.Exists(source) && !Directory.Exists(source))
                throw new FileNotFoundException("Updated source for plugin '" + plugin.Name + "' is missing: " + source);

            var c = _parser.ParseCandidate(source, plugin.RelativePath, File.Exists(source) || plugin.SourceIsFile);
            if (c == null && Directory.Exists(source))
            {
                var entry = PluginParser.ResolveEntry(source);
                if (entry != null) c = _parser.ParseCandidate(source, plugin.RelativePath, false);
            }
            if (c == null)
                throw new InvalidOperationException("The updated source no longer contains a valid Vencord entry for '" + plugin.Name + "'.");
            if (!c.Name.Equals(plugin.Name, StringComparison.Ordinal))
                throw new InvalidOperationException("The updated source changed plugin name from '" + plugin.Name + "' to '" + c.Name + "'. Remove and reinstall it to accept a plugin identity change.");

            if (plugin.SourceKind == PluginSourceKind.GitHub)
                ReplaceSnapshot(c, plugin);

            ApplyCandidateMetadata(plugin, c);
            plugin.ContentHash = HashUtil.Sha256Path(GetSourcePath(config, plugin));
        }

        public void RemovePackageFiles(ManagedPlugin plugin)
        {
            try
            {
                var root = Path.Combine(AppPaths.PackagesDir, plugin.Id);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (Exception ex) { _log("Could not delete package cache: " + ex.Message); }
        }

        public static string MakeTargetFolder(ManagedPlugin plugin)
        {
            var slug = Regex.Replace(plugin.Name ?? "plugin", @"[^A-Za-z0-9_-]+", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(slug)) slug = "plugin-" + plugin.Id.Substring(0, 8);
            var suffix = string.IsNullOrWhiteSpace(plugin.TargetSuffix) ? "" : "." + plugin.TargetSuffix;
            return "rvcpm-" + slug + suffix;
        }

        private string GetGitHubRepositorySourcePath(AppConfig config, ManagedPlugin plugin)
        {
            var repo = config.Repositories.FirstOrDefault(r => r.Id == plugin.RepositoryId);
            if (repo == null) return "";
            var repoRoot = Path.GetFullPath(repo.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(plugin.RelativePath) || plugin.RelativePath == ".") return repoRoot;
            var resolved = Path.GetFullPath(Path.Combine(repoRoot, plugin.RelativePath));
            var prefix = repoRoot + Path.DirectorySeparatorChar;
            if (!resolved.Equals(repoRoot, StringComparison.OrdinalIgnoreCase) && !resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Managed GitHub plugin path points outside its repository: " + plugin.RelativePath);
            return resolved;
        }

        private static void SnapshotSource(PluginCandidate candidate, ManagedPlugin plugin)
        {
            var packageRoot = Path.Combine(AppPaths.PackagesDir, plugin.Id, "source");
            Directory.CreateDirectory(packageRoot);
            if (candidate.IsFile)
            {
                var dst = Path.Combine(packageRoot, Path.GetFileName(candidate.SourcePath));
                File.Copy(candidate.SourcePath, dst, true);
                CopyRelativeDependencies(candidate.SourcePath, packageRoot);
                plugin.PackagePath = dst;
            }
            else
            {
                CopyDirectory(candidate.SourcePath, packageRoot);
                plugin.PackagePath = packageRoot;
            }
        }

        private static void ReplaceSnapshot(PluginCandidate candidate, ManagedPlugin plugin)
        {
            var packageRoot = Path.Combine(AppPaths.PackagesDir, plugin.Id, "source");
            var tempRoot = Path.Combine(AppPaths.PackagesDir, plugin.Id, "source.new-" + Guid.NewGuid().ToString("N"));
            var backupRoot = Path.Combine(AppPaths.PackagesDir, plugin.Id, "source.previous");
            Directory.CreateDirectory(tempRoot);
            try
            {
                string newPath;
                if (candidate.IsFile)
                {
                    var dst = Path.Combine(tempRoot, Path.GetFileName(candidate.SourcePath));
                    File.Copy(candidate.SourcePath, dst, true);
                    CopyRelativeDependencies(candidate.SourcePath, tempRoot);
                    newPath = dst;
                }
                else
                {
                    CopyDirectory(candidate.SourcePath, tempRoot);
                    newPath = tempRoot;
                }

                if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
                if (Directory.Exists(packageRoot)) Directory.Move(packageRoot, backupRoot);
                Directory.Move(tempRoot, packageRoot);
                plugin.PackagePath = candidate.IsFile
                    ? Path.Combine(packageRoot, Path.GetFileName(newPath))
                    : packageRoot;
                if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            }
            catch
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
                try
                {
                    if (!Directory.Exists(packageRoot) && Directory.Exists(backupRoot))
                        Directory.Move(backupRoot, packageRoot);
                }
                catch { }
                throw;
            }
        }


        private static bool HasUserFacingSettings(IEnumerable<PluginSettingSchema> settings)
        {
            return settings != null && settings.Any(x => x.UserFacing && !x.Hidden);
        }

        private static string SettingsSignature(IEnumerable<PluginSettingSchema> settings)
        {
            if (settings == null) return "";
            return string.Join("|", settings.Select(x => string.Join(":", new[]
            {
                x.Key ?? "", x.Type.ToString(), x.UserFacing.ToString(), x.EditableInManager.ToString(),
                x.Hidden.ToString(), x.Disabled.ToString(), x.ConditionalVisibility.ToString(), x.ConditionalDisabled.ToString(),
                x.DisplayName ?? "", x.Description ?? "", x.Placeholder ?? ""
            })));
        }

        private static void ApplyCandidateMetadata(ManagedPlugin plugin, PluginCandidate c)
        {
            plugin.PluginDescription = c.Description ?? plugin.PluginDescription;
            if (plugin.SourceKind != PluginSourceKind.GitHub || string.IsNullOrWhiteSpace(plugin.GitHubDescription))
                plugin.Description = plugin.PluginDescription;
            plugin.Author = c.Author ?? plugin.Author;
            if (!string.IsNullOrWhiteSpace(c.Version)) plugin.Version = c.Version;
            plugin.Settings = c.Settings ?? new List<PluginSettingSchema>();
            plugin.HasSettings = HasUserFacingSettings(plugin.Settings);
            plugin.Dependencies = c.Dependencies ?? new List<string>();
            plugin.EnabledByDefault = c.EnabledByDefault;
            plugin.Required = c.Required;
            plugin.RequiresRestart = c.RequiresRestart;
            plugin.SourceIsFile = c.IsFile;
            plugin.EntryExtension = c.Extension;
            plugin.TargetSuffix = c.TargetSuffix;
            plugin.TargetFolder = MakeTargetFolder(plugin);
        }

        public static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.GetDirectories(source))
            {
                if (IgnoreDirs.Contains(Path.GetFileName(dir))) continue;
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
            }
            foreach (var file in Directory.GetFiles(source))
            {
                var name = Path.GetFileName(file);
                if (name.Equals(".rvcpm-managed", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(destination, name), true);
            }
        }

        private static void CopyRelativeDependencies(string entryFile, string destinationRoot)
        {
            var sourceRoot = Path.GetDirectoryName(entryFile);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CopyDepsRecursive(entryFile, sourceRoot, destinationRoot, visited);
        }

        private static void CopyDepsRecursive(string sourceFile, string sourceRoot, string destinationRoot, HashSet<string> visited)
        {
            var full = Path.GetFullPath(sourceFile);
            if (!visited.Add(full)) return;
            foreach (var import in PluginParser.FindRelativeImports(full))
            {
                if (import.StartsWith("../", StringComparison.Ordinal)) continue;
                var resolved = ResolveImport(Path.GetDirectoryName(full), import);
                if (resolved == null || !File.Exists(resolved)) continue;
                var rel = PluginScanner.MakeRelative(sourceRoot, resolved);
                if (rel.StartsWith("..")) continue;
                var dst = Path.Combine(destinationRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(resolved, dst, true);
                var ext = Path.GetExtension(resolved);
                if (ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".js", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
                    CopyDepsRecursive(resolved, sourceRoot, destinationRoot, visited);
            }
        }

        private static string ResolveImport(string baseDir, string import)
        {
            var cleanImport = import ?? "";
            var query = cleanImport.IndexOfAny(new[] { '?', '#' });
            if (query >= 0) cleanImport = cleanImport.Substring(0, query);
            var raw = Path.GetFullPath(Path.Combine(baseDir, cleanImport.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(raw)) return raw;
            foreach (var ext in new[] { ".ts", ".tsx", ".js", ".jsx", ".css", ".json" })
                if (File.Exists(raw + ext)) return raw + ext;
            if (Directory.Exists(raw))
            {
                foreach (var name in new[] { "index.ts", "index.tsx", "index.js", "index.jsx" })
                {
                    var p = Path.Combine(raw, name);
                    if (File.Exists(p)) return p;
                }
            }
            return null;
        }

        private static string ShortCommit(string commit)
        {
            if (string.IsNullOrWhiteSpace(commit)) return "GitHub";
            return commit.Substring(0, Math.Min(8, commit.Length));
        }
    }
}
