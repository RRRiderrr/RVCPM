using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RVCPM.Services
{
    /// <summary>
    /// Small file-based IPC bridge between the external RVCPM process and the hidden
    /// RVCPMIntegration Vencord userplugin running inside Discord.
    ///
    /// The bridge intentionally supports a tiny command surface only. It does not eval code
    /// and does not expose arbitrary filesystem access to the renderer. This keeps custom
    /// settings UI support generic while the actual React component still runs in Discord.
    /// </summary>
    internal sealed class VencordBridgeService : IDisposable
    {
        private readonly Action<string> _log;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public VencordBridgeService(Action<string> log)
        {
            _log = log;
        }

        public static string CommandFile { get { return Path.Combine(AppPaths.Root, "bridge-command.json"); } }
        public static string AckFile { get { return Path.Combine(AppPaths.Root, "bridge-ack.json"); } }

        public async Task OpenPluginSettingsAsync(string pluginName, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
                throw new ArgumentException("Plugin name is required.", nameof(pluginName));

            await _sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                AppPaths.EnsureDirectories();
                TryDelete(AckFile);

                var id = Guid.NewGuid().ToString("N");
                var command = new JObject
                {
                    ["version"] = 1,
                    ["id"] = id,
                    ["action"] = "openPluginSettings",
                    ["pluginName"] = pluginName,
                    ["createdUtc"] = DateTime.UtcNow.ToString("o")
                };

                WriteJsonAtomic(CommandFile, command);
                _log("Sent custom settings UI request to Discord for " + pluginName + ".");

                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(15))
                {
                    token.ThrowIfCancellationRequested();

                    JObject ack = null;
                    try
                    {
                        if (File.Exists(AckFile))
                            ack = JObject.Parse(File.ReadAllText(AckFile, Encoding.UTF8));
                    }
                    catch
                    {
                        // The native side writes atomically, but antivirus/indexers can still
                        // briefly make the file unavailable. Retry rather than failing the RPC.
                    }

                    if (ack != null && string.Equals((string)ack["id"], id, StringComparison.Ordinal))
                    {
                        TryDelete(AckFile);
                        TryDelete(CommandFile);
                        if ((bool?)ack["ok"] == true)
                            return;

                        var error = (string)ack["error"];
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                            ? "Vencord could not open this plugin's custom settings UI."
                            : error);
                    }

                    await Task.Delay(150, token).ConfigureAwait(false);
                }

                TryDelete(CommandFile);
                throw new InvalidOperationException(
                    "RVCPMIntegration did not answer. Rebuild/inject Vencord once with this RVCPM version, then try again. " +
                    "The custom UI bridge requires the v0.1.9 integration plugin to be active inside Discord.");
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static void WriteJsonAtomic(string path, JObject value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, value.ToString(Formatting.None), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public void Dispose()
        {
            _sendLock.Dispose();
        }
    }
}
