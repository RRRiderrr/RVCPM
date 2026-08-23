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
            // Settings/injection are only safe when every Discord Desktop process is gone.
            // For "auto" we snapshot every currently running Discord channel. For an explicit
            // branch we still include any other running Discord channel because Vencord's
            // settings file must not be edited while another Discord instance is alive.
            var kinds = GetRunningDiscordKinds();
            if (!string.IsNullOrWhiteSpace(branch) && !branch.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var explicitKind = branch.ToLowerInvariant();
                if (!kinds.Contains(explicitKind, StringComparer.OrdinalIgnoreCase)) kinds.Add(explicitKind);
            }
            kinds = kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (kinds.Count == 0) return;

            // First ask the UI process(es) to close normally.
            foreach (var kind in kinds)
            {
                var processName = ProcessName(kind);
                foreach (var p in SafeGetProcesses(processName))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        _log("Closing " + processName + " (PID " + p.Id + ")...");
                        p.CloseMainWindow();
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }

            if (await WaitUntilStoppedAsync(kinds, 2500, token).ConfigureAwait(false))
            {
                // Give Discord/Vencord a short moment to release settings.json after process exit.
                await Task.Delay(350, token).ConfigureAwait(false);
                return;
            }

            // Graceful close did not finish everything. Force-kill all remaining Discord processes.
            foreach (var kind in kinds)
            {
                var processName = ProcessName(kind);
                foreach (var p in SafeGetProcesses(processName))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        _log("Force stopping " + processName + " (PID " + p.Id + ")...");
                        p.Kill();
                        try { p.WaitForExit(2500); } catch { }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }

            if (!await WaitUntilStoppedAsync(kinds, 3500, token).ConfigureAwait(false))
            {
                // Last-resort Windows process-tree termination. Discord can occasionally leave a
                // renderer/updater child alive for a fraction of a second after Process.Kill().
                foreach (var kind in kinds)
                {
                    token.ThrowIfCancellationRequested();
                    TryTaskKill(ProcessName(kind) + ".exe");
                }
            }

            if (!await WaitUntilStoppedAsync(kinds, 5000, token).ConfigureAwait(false))
            {
                var alive = kinds.Where(IsKindRunning).Select(ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                throw new InvalidOperationException("RVCPM could not stop Discord automatically. Still running: " + string.Join(", ", alive));
            }

            await Task.Delay(500, token).ConfigureAwait(false);
            _log("Discord fully stopped; settings are safe to apply.");
        }

        private static Process[] SafeGetProcesses(string processName)
        {
            try { return Process.GetProcessesByName(processName); }
            catch { return new Process[0]; }
        }

        private static bool IsKindRunning(string kind)
        {
            var processes = SafeGetProcesses(ProcessName(kind));
            try { return processes.Length > 0; }
            finally
            {
                foreach (var p in processes) try { p.Dispose(); } catch { }
            }
        }

        private static async Task<bool> WaitUntilStoppedAsync(IEnumerable<string> kinds, int timeoutMs, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (!kinds.Any(IsKindRunning)) return true;
                await Task.Delay(150, token).ConfigureAwait(false);
            }
            return !kinds.Any(IsKindRunning);
        }

        private void TryTaskKill(string exeName)
        {
            try
            {
                _log("Using taskkill for remaining " + exeName + " process tree...");
                using (var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/F /T /IM \"" + exeName + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (killer != null) killer.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _log("taskkill fallback failed for " + exeName + ": " + ex.Message);
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
