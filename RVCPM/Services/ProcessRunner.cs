using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace RVCPM.Services
{
    internal sealed class ProcessRunner
    {
        private readonly Action<string> _log;
        private readonly object _processLock = new object();
        private Process _currentProcess;

        public ProcessRunner(Action<string> log)
        {
            _log = log;
        }

        public async Task<ProcessResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken, bool shell = false)
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var tcs = new TaskCompletionSource<int>();

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? "",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                UseShellExecute = shell,
                CreateNoWindow = !shell,
                RedirectStandardOutput = !shell,
                RedirectStandardError = !shell,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!shell)
            {
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    stdout.AppendLine(e.Data);
                    _log(e.Data);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    stderr.AppendLine(e.Data);
                    _log(e.Data);
                };
            }
            p.Exited += (s, e) => tcs.TrySetResult(p.ExitCode);

            lock (_processLock) _currentProcess = p;
            try
            {
                _log("> " + fileName + " " + arguments);
                if (!p.Start())
                    throw new InvalidOperationException("Failed to start process: " + fileName);

                if (!shell)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                using (cancellationToken.Register(() => KillCurrentProcessTree()))
                {
                    var code = await tcs.Task.ConfigureAwait(false);
                    return new ProcessResult { ExitCode = code, Output = stdout.ToString(), Error = stderr.ToString() };
                }
            }
            finally
            {
                lock (_processLock)
                {
                    if (ReferenceEquals(_currentProcess, p)) _currentProcess = null;
                }
                p.Dispose();
            }
        }

        public void CancelCurrent()
        {
            KillCurrentProcessTree();
        }

        private void KillCurrentProcessTree()
        {
            Process p;
            lock (_processLock) p = _currentProcess;
            if (p == null) return;

            try
            {
                if (p.HasExited) return;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + p.Id + " /T /F",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit(5000);
                }
                catch { p.Kill(); }
            }
            catch { }
        }

        public static bool CommandExists(string command)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = command,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    p.WaitForExit(4000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        public static void RefreshProcessPath()
        {
            try
            {
                var machine = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")?.GetValue("Path") as string ?? "";
                var user = Registry.CurrentUser.OpenSubKey("Environment")?.GetValue("Path") as string ?? "";
                Environment.SetEnvironmentVariable("PATH", machine + ";" + user, EnvironmentVariableTarget.Process);
            }
            catch { }
        }

        public static string Quote(string value)
        {
            if (value == null) return "\"\"";
            if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '\"' }) < 0) return value;

            var sb = new StringBuilder();
            sb.Append('\"');
            var backslashes = 0;
            foreach (var c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '\"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('\"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    sb.Append('\\', backslashes);
                    backslashes = 0;
                }
                sb.Append(c);
            }
            if (backslashes > 0) sb.Append('\\', backslashes * 2);
            sb.Append('\"');
            return sb.ToString();
        }
    }
}
