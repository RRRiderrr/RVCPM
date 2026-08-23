using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RVCPM.Services
{
    internal sealed class PluginScanner
    {
        private readonly PluginParser _parser = new PluginParser();
        private static readonly HashSet<string> IgnoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "dist", "build", "out", ".idea", ".vs", "coverage"
        };

        public CandidateBatch AnalyzePaths(IEnumerable<string> inputPaths)
        {
            var paths = inputPaths.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var batch = new CandidateBatch
            {
                SourceKind = paths.Count == 1 && Path.GetExtension(paths[0]).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    ? PluginSourceKind.Zip
                    : PluginSourceKind.LocalFile,
                SourceReference = string.Join(";", paths)
            };

            var roots = new List<Tuple<string, PluginSourceKind, string>>();
            foreach (var path in paths)
            {
                if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extract = Path.Combine(AppPaths.TempDir, "batch-" + batch.Id, Path.GetFileNameWithoutExtension(path));
                    Directory.CreateDirectory(extract);
                    ExtractZipSafely(path, extract);
                    batch.TempRoot = Path.Combine(AppPaths.TempDir, "batch-" + batch.Id);
                    roots.Add(Tuple.Create(extract, PluginSourceKind.Zip, path));
                }
                else if (Directory.Exists(path))
                {
                    roots.Add(Tuple.Create(path, PluginSourceKind.LocalFolder, path));
                }
                else if (File.Exists(path))
                {
                    roots.Add(Tuple.Create(path, PluginSourceKind.LocalFile, path));
                }
            }

            foreach (var root in roots)
            {
                if (File.Exists(root.Item1))
                {
                    var c = _parser.ParseCandidate(root.Item1, Path.GetFileName(root.Item1), true);
                    if (c != null)
                    {
                        c.SourceKind = root.Item2;
                        c.OriginReference = root.Item3;
                        batch.Candidates.Add(c);
                    }
                    continue;
                }

                var found = ScanDirectory(root.Item1, root.Item1, 0, 6);
                foreach (var c in found)
                {
                    c.SourceKind = root.Item2;
                    c.OriginReference = root.Item3;
                }
                batch.Candidates.AddRange(found);
            }

            batch.Candidates = Deduplicate(batch.Candidates);
            if (batch.Candidates.Count == 0)
                throw new InvalidOperationException("No Vencord userplugins were detected. Expected a .ts/.tsx file containing definePlugin({ name: ... }) or a folder with index.ts/index.tsx.");

            if (paths.Count == 1)
            {
                if (Directory.Exists(paths[0])) batch.SourceKind = PluginSourceKind.LocalFolder;
                else if (Path.GetExtension(paths[0]).Equals(".zip", StringComparison.OrdinalIgnoreCase)) batch.SourceKind = PluginSourceKind.Zip;
                else batch.SourceKind = PluginSourceKind.LocalFile;
            }
            return batch;
        }

        public List<PluginCandidate> ScanDirectory(string root, string current, int depth, int maxDepth)
        {
            var result = new List<PluginCandidate>();
            if (depth > maxDepth) return result;

            var entry = PluginParser.ResolveEntry(current);
            if (entry != null && PluginParser.LooksLikePluginFile(entry))
            {
                var rel = MakeRelative(root, current);
                var c = _parser.ParseCandidate(current, rel, false);
                if (c != null) result.Add(c);
                // Do not recursively treat implementation files below a valid plugin root as more plugins.
                return result;
            }

            foreach (var file in SafeGetFiles(current))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(file).Equals("native.ts", StringComparison.OrdinalIgnoreCase)) continue;
                if (!PluginParser.LooksLikePluginFile(file)) continue;
                var c = _parser.ParseCandidate(file, MakeRelative(root, file), true);
                if (c != null) result.Add(c);
            }

            foreach (var dir in SafeGetDirectories(current))
            {
                if (IgnoredDirs.Contains(Path.GetFileName(dir))) continue;
                result.AddRange(ScanDirectory(root, dir, depth + 1, maxDepth));
            }
            return result;
        }


        internal static void ExtractZipSafely(string archivePath, string destination)
        {
            var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long total = 0;
            var count = 0;
            const long MaxExtractedBytes = 128L * 1024L * 1024L;
            const int MaxEntries = 2000;

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (++count > MaxEntries) throw new InvalidOperationException("ZIP package contains too many entries.");
                    total += Math.Max(0, entry.Length);
                    if (total > MaxExtractedBytes) throw new InvalidOperationException("ZIP package expands beyond RVCPM's 128 MB safety limit.");

                    var normalized = (entry.FullName ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(normalized)) continue;
                    var target = Path.GetFullPath(Path.Combine(destination, normalized));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("ZIP package contains an unsafe path: " + entry.FullName);

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = File.Create(target))
                        input.CopyTo(output);
                }
            }
        }

        private static List<PluginCandidate> Deduplicate(List<PluginCandidate> input)
        {
            return input
                .GroupBy(x => x.Name + "|" + Path.GetFullPath(x.SourcePath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string[] SafeGetFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch { return new string[0]; }
        }

        private static string[] SafeGetDirectories(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        public static string MakeRelative(string root, string path)
        {
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var pathFull = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (rootFull.Equals(pathFull, StringComparison.OrdinalIgnoreCase)) return ".";

                var rootUri = new Uri(AppendSlash(rootFull));
                var targetForUri = Directory.Exists(pathFull) ? AppendSlash(pathFull) : pathFull;
                var pathUri = new Uri(targetForUri);
                var rel = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
                return rel.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { return Path.GetFileName(path); }
        }

        private static string AppendSlash(string p)
        {
            return p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p : p + Path.DirectorySeparatorChar;
        }
    }
}
