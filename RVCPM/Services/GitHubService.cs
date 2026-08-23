using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    internal sealed class GitHubLocation
    {
        public string Owner { get; set; }
        public string Repo { get; set; }
        public string Branch { get; set; }
        public string SubPath { get; set; }
        public string CloneUrl { get { return "https://github.com/" + Owner + "/" + Repo + ".git"; } }
        public string WebUrl { get { return "https://github.com/" + Owner + "/" + Repo; } }
    }

    internal sealed class GitHubService
    {
        private readonly ProcessRunner _runner;
        private readonly PluginScanner _scanner;
        private readonly HttpClient _http;

        public GitHubService(ProcessRunner runner, PluginScanner scanner)
        {
            _runner = runner;
            _scanner = scanner;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("RVCPM/0.1.1 (+https://github.com/Vencord/Vencord)");
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        public GitHubLocation ParseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("GitHub URL is empty.");
            Uri uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only github.com project URLs are supported.");

            var parts = uri.AbsolutePath.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new ArgumentException("Expected https://github.com/owner/repository");
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1].Substring(0, parts[1].Length - 4) : parts[1];
            var loc = new GitHubLocation { Owner = parts[0], Repo = repo, Branch = "", SubPath = "" };

            if (parts.Length >= 4 && (parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("blob", StringComparison.OrdinalIgnoreCase)))
            {
                // Common GitHub URL form. Branches containing '/' are ambiguous in a URL; users can use the repo root for those.
                loc.Branch = parts[3];
                if (parts.Length > 4) loc.SubPath = string.Join("/", parts.Skip(4));
            }
            return loc;
        }

        public async Task<Tuple<string, string, string>> GetRepoMetadataAsync(GitHubLocation loc, CancellationToken token)
        {
            try
            {
                var text = await _http.GetStringAsync("https://api.github.com/repos/" + Uri.EscapeDataString(loc.Owner) + "/" + Uri.EscapeDataString(loc.Repo)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var jo = JObject.Parse(text);
                return Tuple.Create(
                    (string)jo["default_branch"] ?? "main",
                    (string)jo["description"] ?? "",
                    (string)jo["homepage"] ?? "");
            }
            catch
            {
                return Tuple.Create("main", "", "");
            }
        }

        public async Task<ManagedRepository> CloneOrGetRepositoryAsync(AppConfig config, string url, CancellationToken token)
        {
            var loc = ParseUrl(url);
            var meta = await GetRepoMetadataAsync(loc, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(loc.Branch)) loc.Branch = meta.Item1;

            var existing = config.Repositories.FirstOrDefault(r =>
                r.Owner.Equals(loc.Owner, StringComparison.OrdinalIgnoreCase) &&
                r.Name.Equals(loc.Repo, StringComparison.OrdinalIgnoreCase) &&
                r.Branch.Equals(loc.Branch, StringComparison.OrdinalIgnoreCase));

            if (existing != null && Directory.Exists(Path.Combine(existing.LocalPath, ".git")))
            {
                existing.Description = string.IsNullOrWhiteSpace(meta.Item2) ? existing.Description : meta.Item2;
                existing.Homepage = string.IsNullOrWhiteSpace(meta.Item3) ? existing.Homepage : meta.Item3;
                existing.Readme = ReadReadme(existing.LocalPath);
                return existing;
            }

            var repo = existing ?? new ManagedRepository
            {
                Owner = loc.Owner,
                Name = loc.Repo,
                Branch = loc.Branch,
                Url = loc.WebUrl,
                Description = meta.Item2,
                Homepage = meta.Item3
            };
            repo.LocalPath = Path.Combine(AppPaths.RepositoriesDir, repo.Id);
            if (Directory.Exists(repo.LocalPath)) Directory.Delete(repo.LocalPath, true);
            Directory.CreateDirectory(Path.GetDirectoryName(repo.LocalPath));

            var clone = await _runner.RunAsync("git", "clone --depth 1 --branch " + ProcessRunner.Quote(repo.Branch) + " " + ProcessRunner.Quote(loc.CloneUrl) + " " + ProcessRunner.Quote(repo.LocalPath), AppPaths.RepositoriesDir, token).ConfigureAwait(false);
            if (!clone.Success) throw new InvalidOperationException("Git clone failed.\n" + clone.Error);

            repo.Commit = await GetLocalCommitAsync(repo, token).ConfigureAwait(false);
            repo.Readme = ReadReadme(repo.LocalPath);
            repo.LastCheckedUtc = DateTime.UtcNow;
            if (existing == null) config.Repositories.Add(repo);
            return repo;
        }

        public CandidateBatch AnalyzeRepository(ManagedRepository repo, string originalUrl)
        {
            var loc = ParseUrl(originalUrl);
            var root = repo.LocalPath;
            if (!string.IsNullOrWhiteSpace(loc.SubPath))
            {
                var sub = loc.SubPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var repoRoot = Path.GetFullPath(repo.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                root = Path.GetFullPath(Path.Combine(repoRoot, sub));
                var repoPrefix = repoRoot + Path.DirectorySeparatorChar;
                if (!root.Equals(repoRoot, StringComparison.OrdinalIgnoreCase) && !root.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The GitHub subpath points outside the repository.");
            }
            if (!File.Exists(root) && !Directory.Exists(root))
                throw new FileNotFoundException("The selected path does not exist in the cloned repository: " + loc.SubPath);

            var batch = new CandidateBatch
            {
                SourceKind = PluginSourceKind.GitHub,
                SourceReference = originalUrl,
                RepositoryId = repo.Id
            };

            if (File.Exists(root))
            {
                var parser = new PluginParser();
                var c = parser.ParseCandidate(root, PluginScanner.MakeRelative(repo.LocalPath, root), true);
                if (c != null) batch.Candidates.Add(c);
            }
            else
            {
                batch.Candidates.AddRange(_scanner.ScanDirectory(repo.LocalPath, root, 0, 7));
            }

            foreach (var candidate in batch.Candidates)
            {
                candidate.SourceKind = PluginSourceKind.GitHub;
                candidate.OriginReference = originalUrl;
            }

            if (batch.Candidates.Count == 0)
                throw new InvalidOperationException("No Vencord plugin entry points were found in this GitHub project/path.");
            return batch;
        }

        public async Task<string> GetRemoteCommitAsync(ManagedRepository repo, CancellationToken token)
        {
            var cloneUrl = "https://github.com/" + repo.Owner + "/" + repo.Name + ".git";
            var result = await _runner.RunAsync("git", "ls-remote " + ProcessRunner.Quote(cloneUrl) + " refs/heads/" + ProcessRunner.Quote(repo.Branch), AppPaths.RepositoriesDir, token).ConfigureAwait(false);
            if (!result.Success) return "";
            var first = (result.Output ?? "").Trim().Split(new[] { '\t', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first ?? "";
        }

        public async Task<bool> UpdateRepositoryAsync(ManagedRepository repo, CancellationToken token)
        {
            if (!Directory.Exists(Path.Combine(repo.LocalPath, ".git")))
                throw new DirectoryNotFoundException("Repository cache is missing: " + repo.LocalPath);
            var before = await GetLocalCommitAsync(repo, token).ConfigureAwait(false);
            var fetch = await _runner.RunAsync("git", "fetch --depth 1 origin " + ProcessRunner.Quote(repo.Branch), repo.LocalPath, token).ConfigureAwait(false);
            if (!fetch.Success) throw new InvalidOperationException("Git fetch failed.\n" + fetch.Error);
            var reset = await _runner.RunAsync("git", "reset --hard FETCH_HEAD", repo.LocalPath, token).ConfigureAwait(false);
            if (!reset.Success) throw new InvalidOperationException("Git reset failed.\n" + reset.Error);
            var after = await GetLocalCommitAsync(repo, token).ConfigureAwait(false);
            repo.Commit = after;
            repo.Readme = ReadReadme(repo.LocalPath);
            repo.LastCheckedUtc = DateTime.UtcNow;
            return !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> GetLocalCommitAsync(ManagedRepository repo, CancellationToken token)
        {
            var result = await _runner.RunAsync("git", "rev-parse HEAD", repo.LocalPath, token).ConfigureAwait(false);
            return result.Success ? result.Output.Trim() : "";
        }

        public async Task<string> GetVersionLabelAsync(ManagedRepository repo, CancellationToken token)
        {
            var tag = await _runner.RunAsync("git", "describe --tags --abbrev=0", repo.LocalPath, token).ConfigureAwait(false);
            if (tag.Success && !string.IsNullOrWhiteSpace(tag.Output)) return tag.Output.Trim();
            var commit = string.IsNullOrWhiteSpace(repo.Commit) ? await GetLocalCommitAsync(repo, token).ConfigureAwait(false) : repo.Commit;
            return string.IsNullOrWhiteSpace(commit) ? "GitHub" : commit.Substring(0, Math.Min(8, commit.Length));
        }

        private static string ReadReadme(string root)
        {
            try
            {
                var names = new[] { "README.md", "README.MD", "README.txt", "readme.md" };
                foreach (var n in names)
                {
                    var p = Path.Combine(root, n);
                    if (File.Exists(p))
                    {
                        var text = File.ReadAllText(p);
                        return text.Length > 50000 ? text.Substring(0, 50000) + "\n\n[truncated by RVCPM]" : text;
                    }
                }
            }
            catch { }
            return "";
        }
    }
}
