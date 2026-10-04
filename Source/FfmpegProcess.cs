using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace JustReadTheInstructions
{
    internal abstract class FfmpegProcess : IDisposable
    {
        protected const int ErrorTailChars = 2000;

        public abstract int ExitCode { get; }
        public abstract string ErrorTail { get; }
        public abstract bool WaitForExit(int milliseconds);
        public abstract void Dispose();

        public static FfmpegProcess Start(string executable, string arguments, Action exited)
            => Wine.IsRunning
                ? new WineFfmpegProcess(executable, arguments, exited)
                : (FfmpegProcess)new StandardFfmpegProcess(executable, arguments, exited);

        protected static string Tail(string text)
            => (text.Length > ErrorTailChars ? text.Substring(text.Length - ErrorTailChars) : text).Trim();
    }

    internal sealed class StandardFfmpegProcess : FfmpegProcess
    {
        private readonly Process _process;
        private readonly StringBuilder _errors = new StringBuilder();

        public StandardFfmpegProcess(string executable, string arguments, Action exited)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = exited != null,
            };
            _process.ErrorDataReceived += (sender, e) => RememberError(e.Data);
            if (exited != null) _process.Exited += (sender, e) => exited();
            _process.Start();
            _process.BeginErrorReadLine();
        }

        public override int ExitCode => _process.ExitCode;

        public override string ErrorTail
        {
            get { lock (_errors) return Tail(_errors.ToString()); }
        }

        public override bool WaitForExit(int milliseconds)
        {
            if (!_process.WaitForExit(milliseconds)) return false;
            _process.WaitForExit();
            return true;
        }

        public override void Dispose()
        {
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException) { }
            _process.Dispose();
        }

        private void RememberError(string line)
        {
            if (line == null) return;
            lock (_errors)
            {
                _errors.AppendLine(line);
                if (_errors.Length > ErrorTailChars) _errors.Remove(0, _errors.Length - ErrorTailChars);
            }
        }
    }

    internal sealed class WineFfmpegProcess : FfmpegProcess
    {
        private const string LaunchScript = "log=$1; shift; exec \"$@\" 2>\"$log\"";
        private const string ContainerLibraries = "/usr/lib/pressure-vessel/";

        private readonly string _log = Path.GetTempFileName();
        private readonly ManualResetEventSlim _exited = new ManualResetEventSlim();
        private volatile int _exitCode = -1;

        public WineFfmpegProcess(string executable, string arguments, Action exited)
        {
            var argv = new List<string> { "/bin/sh", "-c", LaunchScript, "sh", Wine.UnixPath(_log), "env" };
            argv.AddRange(LibraryPathWithoutProton());
            argv.Add(Wine.UnixPath(executable));
            argv.AddRange(Split(arguments));
            new Thread(() =>
            {
                _exitCode = Wine.Run(argv);
                _exited.Set();
                exited?.Invoke();
            }) { IsBackground = true, Name = "JRTI-Ffmpeg" }.Start();
        }

        public override int ExitCode => _exitCode;

        public override string ErrorTail
        {
            get
            {
                try { return Tail(File.ReadAllText(_log)); }
                catch (Exception) { return ""; }
            }
        }

        public override bool WaitForExit(int milliseconds) => _exited.Wait(milliseconds);

        public override void Dispose()
        {
            if (!_exited.IsSet) return;
            try { File.Delete(_log); }
            catch (Exception) { }
        }

        private static IEnumerable<string> LibraryPathWithoutProton()
        {
            var kept = new List<string>();
            foreach (var folder in (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "").Split(':'))
                if (folder.StartsWith(ContainerLibraries, StringComparison.Ordinal)) kept.Add(folder);
            return kept.Count == 0
                ? new[] { "-u", "LD_LIBRARY_PATH" }
                : new[] { "LD_LIBRARY_PATH=" + string.Join(":", kept.ToArray()) };
        }

        private static IEnumerable<string> Split(string arguments)
        {
            var current = new StringBuilder();
            bool quoted = false;
            bool started = false;
            foreach (char c in arguments)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    started = true;
                }
                else if (c == ' ' && !quoted)
                {
                    if (started) yield return current.ToString();
                    current.Length = 0;
                    started = false;
                }
                else
                {
                    current.Append(c);
                    started = true;
                }
            }
            if (started) yield return current.ToString();
        }
    }
}
