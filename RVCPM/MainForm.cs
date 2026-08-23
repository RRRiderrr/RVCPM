using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RVCPM.Services;

namespace RVCPM
{
    public sealed class MainForm : Form
    {
        private readonly WebView2 _web = new WebView2();
        private readonly ManagerService _manager = new ManagerService();
        private bool _webReady;

        public MainForm()
        {
            Text = "Rider's Vencord Custom Plugin Manager";
            Icon = LoadApplicationIcon();
            ShowIcon = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 820);
            MinimumSize = new Size(1040, 680);
            BackColor = Color.FromArgb(30, 31, 34);
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            AllowDrop = true;

            _web.Dock = DockStyle.Fill;
            _web.BackColor = BackColor;
            _web.AllowExternalDrop = true;
            Controls.Add(_web);

            Load += async (s, e) => await InitializeWebViewAsync();
            FormClosed += (s, e) => _manager.Dispose();
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            _manager.EventRaised += ManagerOnEventRaised;

            TryEnableRoundedCorners();
        }

        private static Icon LoadApplicationIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RVCPM.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);

                var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (associated != null)
                    return associated;
            }
            catch
            {
                // Fall back to a system icon only if neither packaged icon source is available.
            }

            return SystemIcons.Application;
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var userData = Path.Combine(AppPaths.Root, "WebView2");
                Directory.CreateDirectory(userData);
                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);

                var webDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
                if (!Directory.Exists(webDir))
                    throw new DirectoryNotFoundException("Web UI assets were not copied to output: " + webDir);

                _web.CoreWebView2.SetVirtualHostNameToFolderMapping("rvcpm.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _web.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                _web.CoreWebView2.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    OpenExternal(e.Uri);
                };
                _web.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _webReady = e.IsSuccess;
                    if (_webReady) PostEvent("stateChanged", _manager.GetState());
                };
                _web.CoreWebView2.Navigate("https://rvcpm.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "RVCPM could not initialize Microsoft Edge WebView2.\n\n" + ex.Message +
                    "\n\nWindows 11 normally includes the WebView2 Runtime. Repair/install Microsoft Edge WebView2 Runtime and restart RVCPM.",
                    "RVCPM - WebView2 error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string id = null;
            try
            {
                var msg = JObject.Parse(e.WebMessageAsJson);
                if ((string)msg["type"] != "rpc") return;
                id = (string)msg["id"] ?? Guid.NewGuid().ToString("N");
                var action = (string)msg["action"] ?? "";
                var payload = msg["payload"] as JObject ?? new JObject();

                if (HandleWindowAction(action))
                {
                    PostRpcResult(id, true, new JObject(), null);
                    return;
                }

                var result = await HandleRpcAsync(action, payload);
                PostRpcResult(id, true, result ?? JValue.CreateNull(), null);
            }
            catch (OperationCanceledException)
            {
                if (id != null) PostRpcResult(id, false, null, "Operation cancelled.");
            }
            catch (Exception ex)
            {
                if (id != null) PostRpcResult(id, false, null, ex.Message);
            }
        }

        private async Task<JToken> HandleRpcAsync(string action, JObject payload)
        {
            switch (action)
            {
                case "getState": return _manager.GetState();
                case "getLogs": return new JObject { ["text"] = _manager.GetLogText() };
                case "clearLogs": _manager.ClearLogs(); return new JObject();
                case "cancelOperation": _manager.CancelOperation(); return new JObject();

                case "browseFiles":
                {
                    var files = PickFiles();
                    if (files.Length == 0) return new JObject { ["cancelled"] = true };
                    var batch = _manager.AnalyzeLocalPaths(files);
                    return new JObject { ["batchId"] = batch.Id };
                }
                case "browseFolder":
                {
                    var folder = PickFolder("Select a Vencord plugin folder or a folder containing plugins");
                    if (string.IsNullOrWhiteSpace(folder)) return new JObject { ["cancelled"] = true };
                    var batch = _manager.AnalyzeLocalPaths(new[] { folder });
                    return new JObject { ["batchId"] = batch.Id };
                }
                case "importDroppedFiles":
                {
                    var files = payload["files"] as JArray ?? new JArray();
                    var batch = _manager.AnalyzeDroppedFiles(files);
                    return new JObject { ["batchId"] = batch.Id };
                }
                case "browseDiscordLocation":
                {
                    var folder = PickFolder("Select Discord installation folder");
                    return new JObject { ["path"] = folder ?? "" };
                }
                case "analyzeGithub":
                {
                    var batch = await _manager.AnalyzeGitHubAsync((string)payload["url"] ?? "");
                    return new JObject { ["batchId"] = batch.Id };
                }
                case "installCandidates":
                {
                    var ids = payload["candidateIds"] is JArray a ? a.Values<string>().ToList() : new List<string>();
                    await _manager.InstallCandidatesAsync((string)payload["batchId"], ids);
                    return new JObject();
                }
                case "togglePlugin":
                    _manager.TogglePlugin((string)payload["pluginId"], (bool?)payload["enabled"] ?? false); return _manager.GetState();
                case "getPluginSettings": return _manager.GetPluginSettings((string)payload["pluginId"]);
                case "savePluginSettings":
                    _manager.SavePluginSettings((string)payload["pluginId"], payload["values"] as JObject ?? new JObject()); return _manager.GetState();
                case "removePlugin":
                    await _manager.RemovePluginAsync((string)payload["pluginId"], (bool?)payload["removeSettings"] ?? false); return _manager.GetState();
                case "checkUpdates": await _manager.CheckUpdatesAsync(); return _manager.GetState();
                case "updatePlugin": await _manager.UpdatePluginAsync((string)payload["pluginId"]); return _manager.GetState();
                case "updateAll": await _manager.UpdateAllAsync(); return _manager.GetState();
                case "restartDiscord": await _manager.RestartDiscordAsync(); return _manager.GetState();
                case "rebuild": await _manager.RebuildAsync((bool?)payload["updateVencord"] ?? true); return _manager.GetState();
                case "saveAppSettings": _manager.SaveAppSettings(payload); return _manager.GetState();
                case "openPluginSource": _manager.OpenPluginSource((string)payload["pluginId"]); return new JObject();
                case "openDataFolder": _manager.OpenDataFolder(); return new JObject();
                case "openExternal": OpenExternal((string)payload["url"]); return new JObject();
                default: throw new InvalidOperationException("Unknown UI action: " + action);
            }
        }

        private bool HandleWindowAction(string action)
        {
            switch (action)
            {
                case "windowMinimize": WindowState = FormWindowState.Minimized; return true;
                case "windowMaximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return true;
                case "windowClose": Close(); return true;
                case "beginDrag":
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                    return true;
                default: return false;
            }
        }

        private string[] PickFiles()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select Vencord custom plugins",
                Filter = "Vencord plugins / packages (*.ts;*.tsx;*.zip)|*.ts;*.tsx;*.zip|TypeScript plugins (*.ts;*.tsx)|*.ts;*.tsx|ZIP packages (*.zip)|*.zip|All files (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileNames : new string[0];
        }

        private string PickFolder(string description)
        {
            using (var dlg = new FolderBrowserDialog { Description = description, ShowNewFolderButton = false })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void OnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null || paths.Length == 0) return;
                await Task.Yield();
                _manager.AnalyzeLocalPaths(paths);
            }
            catch (Exception ex) { PostEvent("toast", new { kind = "error", message = ex.Message }); }
        }

        private void ManagerOnEventRaised(string name, object data)
        {
            PostEvent(name, data);
        }

        private void PostEvent(string name, object data)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => PostEvent(name, data)));
                return;
            }
            if (!_webReady || _web.CoreWebView2 == null) return;
            var packet = new JObject
            {
                ["type"] = "event",
                ["name"] = name,
                ["data"] = data == null ? JValue.CreateNull() : (data as JToken ?? JToken.FromObject(data))
            };
            _web.CoreWebView2.PostWebMessageAsJson(packet.ToString(Formatting.None));
        }

        private void PostRpcResult(string id, bool ok, JToken data, string error)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => PostRpcResult(id, ok, data, error)));
                return;
            }
            if (!_webReady || _web.CoreWebView2 == null) return;
            var packet = new JObject
            {
                ["type"] = "rpcResult",
                ["id"] = id,
                ["ok"] = ok,
                ["data"] = data ?? JValue.CreateNull(),
                ["error"] = error == null ? JValue.CreateNull() : new JValue(error)
            };
            _web.CoreWebView2.PostWebMessageAsJson(packet.ToString(Formatting.None));
        }

        private static void OpenExternal(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    var p = PointToClient(new Point((short)((long)m.LParam & 0xffff), (short)(((long)m.LParam >> 16) & 0xffff)));
                    const int grip = 7;
                    var left = p.X <= grip; var right = p.X >= ClientSize.Width - grip;
                    var top = p.Y <= grip; var bottom = p.Y >= ClientSize.Height - grip;
                    if (left && top) m.Result = (IntPtr)HTTOPLEFT;
                    else if (right && top) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (left && bottom) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (right && bottom) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void TryEnableRoundedCorners()
        {
            try
            {
                var preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
