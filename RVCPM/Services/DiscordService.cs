using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RVCPM.Services
{
    internal sealed class DiscordService
    {
        private readonly Action<string> _log;

        public DiscordService(Action<string> log)
        {
            _log = log;
        }

        public bool IsAnyDiscordRunning()
        {
            return GetRunningDiscordKinds().Count > 0;
        }

        public List<string> GetRunningDiscordKinds()
        {
            var result = new List<string>();
            if (Process.GetProcessesByName("Discord").Length > 0) result.Add("stable");
            if (Process.GetProcessesByName("DiscordPTB").Length > 0) result.Add("ptb");
            if (Process.GetProcessesByName("DiscordCanary").Length > 0) result.Add("canary");
            return result;
        }

        public async Task StopAsync(string branch, CancellationToken token)
        {
            var kinds = ResolveKinds(branch, true);
            foreach (var kind in kinds)
            {
                var processName = ProcessName(kind);
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        _log("Closing " + processName + " (PID " + p.Id + ")...");
                        if (!p.CloseMainWindow()) p.Kill();
                    }
                    catch { }
                }
            }

            for (var i = 0; i < 20 && ResolveKinds(branch, true).Any(k => Process.GetProcessesByName(ProcessName(k)).Length > 0); i++)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(250, token).ConfigureAwait(false);
            }

            foreach (var kind in kinds)
            {
                foreach (var p in Process.GetProcessesByName(ProcessName(kind)))
                {
                    try { p.Kill(); } catch { }
                }
            }
        }

        public Task StartAsync(string branch)
        {
            var kinds = ResolveKinds(branch, false);
            if (kinds.Count == 0) kinds.Add("stable");
            foreach (var kind in kinds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var baseDir = DiscordBaseDir(kind);
                    var update = Path.Combine(baseDir, "Update.exe");
                    var exe = ProcessName(kind) + ".exe";
                    if (File.Exists(update))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = update,
                            Arguments = "--processStart " + exe,
                            WorkingDirectory = baseDir,
                            UseShellExecute = true
                        });
                        _log("Started " + exe + " via Update.exe");
                        continue;
                    }

                    var latest = FindLatestAppExe(baseDir, exe);
                    if (latest != null)
                    {
                        Process.Start(new ProcessStartInfo { FileName = latest, WorkingDirectory = Path.GetDirectoryName(latest), UseShellExecute = true });
                        _log("Started " + latest);
                    }
                }
                catch (Exception ex) { _log("Failed to start Discord: " + ex.Message); }
            }
            return Task.CompletedTask;
        }

        public Task StartCustomAsync(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return Task.CompletedTask;
            try
            {
                var baseDir = Path.GetFullPath(location);
                if (File.Exists(baseDir)) baseDir = Path.GetDirectoryName(baseDir);
                if (!Directory.Exists(baseDir)) throw new DirectoryNotFoundException(baseDir);

                var update = Path.Combine(baseDir, "Update.exe");
                var exeNames = new[] { "Discord.exe", "DiscordPTB.exe", "DiscordCanary.exe" };
                if (File.Exists(update))
                {
                    var exeName = exeNames.FirstOrDefault(n => FindLatestAppExe(baseDir, n) != null) ?? "Discord.exe";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = update,
                        Arguments = "--processStart " + exeName,
                        WorkingDirectory = baseDir,
                        UseShellExecute = true
                    });
                    _log("Started custom Discord via " + update);
                    return Task.CompletedTask;
                }

                foreach (var exeName in exeNames)
                {
                    var direct = Path.Combine(baseDir, exeName);
                    var candidate = File.Exists(direct) ? direct : FindLatestAppExe(baseDir, exeName);
                    if (candidate == null) continue;
                    Process.Start(new ProcessStartInfo { FileName = candidate, WorkingDirectory = Path.GetDirectoryName(candidate), UseShellExecute = true });
                    _log("Started custom Discord: " + candidate);
                    return Task.CompletedTask;
                }
                throw new FileNotFoundException("Could not find Discord executable under custom location: " + baseDir);
            }
            catch (Exception ex) { _log("Failed to start custom Discord: " + ex.Message); }
            return Task.CompletedTask;
        }

        public async Task RestartAsync(string branch, CancellationToken token)
        {
            var running = GetRunningDiscordKinds();
            var toStart = branch == "auto" && running.Count > 0 ? running : ResolveKinds(branch, false);
            await StopAsync(branch, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            foreach (var kind in toStart)
                await StartAsync(kind).ConfigureAwait(false);
        }

        public string GetStatusLabel()
        {
            var kinds = GetRunningDiscordKinds();
            return kinds.Count == 0 ? "stopped" : string.Join(", ", kinds);
        }

        private List<string> ResolveKinds(string branch, bool runningOnly)
        {
            if (string.IsNullOrWhiteSpace(branch) || branch.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var running = GetRunningDiscordKinds();
                if (runningOnly || running.Count > 0) return running;
                var installed = new List<string>();
                foreach (var k in new[] { "stable", "canary", "ptb" })
                    if (Directory.Exists(DiscordBaseDir(k))) installed.Add(k);
                return installed;
            }
            return new List<string> { branch.ToLowerInvariant() };
        }

        private static string ProcessName(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "ptb": return "DiscordPTB";
                case "canary": return "DiscordCanary";
                default: return "Discord";
            }
        }

        private static string DiscordBaseDir(string kind)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            switch (kind.ToLowerInvariant())
            {
                case "ptb": return Path.Combine(local, "DiscordPTB");
                case "canary": return Path.Combine(local, "DiscordCanary");
                default: return Path.Combine(local, "Discord");
            }
        }

        private static string FindLatestAppExe(string baseDir, string exe)
        {
            if (!Directory.Exists(baseDir)) return null;
            try
            {
                return Directory.GetDirectories(baseDir, "app-*")
                    .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => Path.Combine(x, exe))
                    .FirstOrDefault(File.Exists);
            }
            catch { return null; }
        }
    }
}
