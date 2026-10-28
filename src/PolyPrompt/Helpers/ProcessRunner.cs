namespace PolyPrompt.Helpers
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Text;
    using System.Threading.Channels;

    /// <summary>
    /// Runs one child process without a shell and exposes its standard output as lines. Standard output is bounded by
    /// <see cref="ProcessRunRequest.MaxOutputCharacters"/> and standard error by
    /// <see cref="ProcessRunRequest.MaxStandardErrorCharacters"/>, so a runaway process cannot exhaust memory. The timeout,
    /// the output limit, caller cancellation, and <see cref="Dispose"/> all kill the entire process tree.
    /// </summary>
    public sealed class ProcessRunner : IDisposable
    {
        #region Private-Members

        private const int _ReadBufferSize = 8192;
        private const int _StreamDrainGraceMs = 2000;

        private static readonly Encoding _Utf8 = new UTF8Encoding(false);

        private readonly ProcessRunRequest _Request;
        private readonly Process _Process;
        private readonly Stopwatch _Stopwatch;
        private readonly Channel<string> _Lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly StringBuilder _StandardError = new StringBuilder();
        private readonly object _StandardErrorLock = new object();
        private readonly CancellationTokenSource _TimeoutCts;
        private readonly CancellationTokenRegistration _TimeoutRegistration;
        private readonly SemaphoreSlim _InputLock = new SemaphoreSlim(1, 1);
        private readonly Task _StandardOutputPump;
        private readonly Task _StandardErrorPump;
        private readonly Task _Completion;
        private Task _InitialInput = Task.CompletedTask;
        private long _StandardOutputCharacters = 0;
        private bool _StandardErrorTruncated = false;
        private volatile bool _TimedOut = false;
        private volatile bool _Cancelled = false;
        private volatile bool _OutputTruncated = false;
        private bool _InputClosed = false;
        private int _Disposed = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// Operating system process id.
        /// </summary>
        public int ProcessId { get; }

        /// <summary>
        /// True once the process has exited.
        /// </summary>
        public bool HasExited
        {
            get
            {
                try
                {
                    return _Process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        #endregion

        #region Constructors-and-Factories

        private ProcessRunner(ProcessRunRequest request, Process process)
        {
            _Request = request;
            _Process = process;
            _Stopwatch = Stopwatch.StartNew();
            ProcessId = process.Id;

            _StandardOutputPump = Task.Run(PumpStandardOutputAsync);
            _StandardErrorPump = Task.Run(PumpStandardErrorAsync);

            _TimeoutCts = new CancellationTokenSource(request.TimeoutMs);
            _TimeoutRegistration = _TimeoutCts.Token.Register(() =>
            {
                if (HasExited) return;
                _TimedOut = true;
                Kill();
            });

            _Completion = Task.Run(MonitorAsync);
        }

        /// <summary>
        /// Start a process. The executable must already be resolved (see <see cref="ResolveExecutable"/>).
        /// </summary>
        /// <param name="request">What to start.</param>
        /// <returns>The running process.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the file name is empty or a limit is not positive.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the executable does not exist or cannot be started.</exception>
        public static ProcessRunner Start(ProcessRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.FileName)) throw new ArgumentException("FileName is required.", nameof(request));
            if (request.TimeoutMs <= 0) throw new ArgumentException("TimeoutMs must be greater than zero.", nameof(request));
            if (request.MaxOutputCharacters <= 0) throw new ArgumentException("MaxOutputCharacters must be greater than zero.", nameof(request));
            if (request.MaxStandardErrorCharacters < 0) throw new ArgumentException("MaxStandardErrorCharacters cannot be negative.", nameof(request));

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = request.FileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = _Utf8,
                StandardOutputEncoding = _Utf8,
                StandardErrorEncoding = _Utf8,
            };

            foreach (string argument in request.Arguments ?? new List<string>())
            {
                info.ArgumentList.Add(argument ?? "");
            }

            if (!string.IsNullOrEmpty(request.WorkingDirectory)) info.WorkingDirectory = request.WorkingDirectory;

            if (request.Environment != null)
            {
                foreach (KeyValuePair<string, string?> entry in request.Environment)
                {
                    if (string.IsNullOrEmpty(entry.Key)) continue;
                    if (entry.Value == null)
                        info.Environment.Remove(entry.Key);
                    else
                        info.Environment[entry.Key] = entry.Value;
                }
            }

            Process process = new Process { StartInfo = info };
            try
            {
                if (!process.Start())
                {
                    process.Dispose();
                    throw new FileNotFoundException("The process could not be started.", request.FileName);
                }
            }
            catch (Win32Exception ex)
            {
                process.Dispose();
                throw new FileNotFoundException("The executable could not be started: " + ex.Message, request.FileName, ex);
            }

            ProcessRunner runner = new ProcessRunner(request, process);
            runner._InitialInput = runner.WriteInitialInputAsync();
            return runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve an executable name to a full path. A name containing a directory separator is checked as a path;
        /// otherwise each directory on <paramref name="searchPath"/> (default: the <c>PATH</c> environment variable) is
        /// searched, trying the <c>PATHEXT</c> extensions on Windows.
        /// </summary>
        /// <param name="executable">Executable name or path.</param>
        /// <param name="searchPath">Search path to use instead of <c>PATH</c>, or null.</param>
        /// <returns>The full path, or null when it was not found.</returns>
        public static string? ResolveExecutable(string executable, string? searchPath = null)
        {
            if (string.IsNullOrWhiteSpace(executable)) return null;

            bool windows = OperatingSystem.IsWindows();
            List<string> extensions = new List<string> { "" };
            if (windows && string.IsNullOrEmpty(Path.GetExtension(executable)))
            {
                string pathExt = System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                foreach (string ext in pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries)) extensions.Add(ext);
            }

            if (executable.IndexOf(Path.DirectorySeparatorChar) >= 0 || executable.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                foreach (string ext in extensions)
                {
                    string candidate = Path.GetFullPath(executable + ext);
                    if (IsExecutableFile(candidate)) return candidate;
                }
                return null;
            }

            string path = searchPath ?? System.Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = directory.Trim().Trim('"');
                if (trimmed.Length == 0) continue;

                foreach (string ext in extensions)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(trimmed, executable + ext);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (IsExecutableFile(candidate)) return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Read the next line of standard output. Line terminators (<c>\n</c> or <c>\r\n</c>) are removed; a final line
        /// without a terminator is returned as is.
        /// </summary>
        /// <param name="token">Cancellation token. Cancelling kills the process tree.</param>
        /// <returns>The line, or null once standard output has ended.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        public async Task<string?> ReadLineAsync(CancellationToken token = default)
        {
            try
            {
                while (await _Lines.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    if (_Lines.Reader.TryRead(out string? line)) return line;
                }
                return null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _Cancelled = true;
                Kill();
                throw;
            }
        }

        /// <summary>
        /// Write one line to standard input. Only valid when <see cref="ProcessRunRequest.KeepStandardInputOpen"/> is true.
        /// </summary>
        /// <param name="line">Line to write (a newline is appended).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the line was written; false when standard input is closed or the process has exited.</returns>
        public async Task<bool> WriteLineAsync(string line, CancellationToken token = default)
        {
            await _InitialInput.ConfigureAwait(false);
            await _InputLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_InputClosed) return false;
                await _Process.StandardInput.WriteAsync((line ?? "") + "\n").ConfigureAwait(false);
                await _Process.StandardInput.FlushAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                return false;
            }
            finally
            {
                _InputLock.Release();
            }
        }

        /// <summary>
        /// Close standard input so the process sees end of file. Safe to call more than once.
        /// </summary>
        public void CloseStandardInput()
        {
            _InputLock.Wait();
            try
            {
                CloseStandardInputCore();
            }
            finally
            {
                _InputLock.Release();
            }
        }

        /// <summary>
        /// Wait for the process to exit and its output to drain, then report the outcome.
        /// </summary>
        /// <param name="token">Cancellation token. Cancelling kills the process tree.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is cancelled.</exception>
        public async Task<ProcessRunResult> WaitForExitAsync(CancellationToken token = default)
        {
            try
            {
                await _Completion.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _Cancelled = true;
                Kill();
                throw;
            }

            int? exitCode = null;
            try
            {
                if (_Process.HasExited) exitCode = _Process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }

            string standardError;
            bool standardErrorTruncated;
            lock (_StandardErrorLock)
            {
                standardError = _StandardError.ToString();
                standardErrorTruncated = _StandardErrorTruncated;
            }

            return new ProcessRunResult
            {
                ExitCode = exitCode,
                StandardError = standardError,
                StandardErrorTruncated = standardErrorTruncated,
                TimedOut = _TimedOut,
                Cancelled = _Cancelled,
                OutputTruncated = _OutputTruncated,
                StandardOutputCharacters = Interlocked.Read(ref _StandardOutputCharacters),
                ProcessId = ProcessId,
                DurationMs = _Stopwatch.ElapsedMilliseconds,
            };
        }

        /// <summary>
        /// Kill the process and all of its descendants. Safe to call more than once and after the process has exited.
        /// </summary>
        public void Kill()
        {
            try
            {
                if (!_Process.HasExited) _Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        /// <summary>
        /// Kill the process tree if it is still running and release resources.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) == 1) return;

            Kill();
            _TimeoutRegistration.Dispose();
            _TimeoutCts.Dispose();
            _Lines.Writer.TryComplete();

            // Wait briefly so the pumps release the pipes before the process handle goes away.
            try
            {
                _Completion.Wait(_StreamDrainGraceMs);
            }
            catch
            {
            }

            _Process.Dispose();
        }

        #endregion

        #region Private-Methods

        private static bool IsExecutableFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (OperatingSystem.IsWindows()) return true;

                UnixFileMode mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task WriteInitialInputAsync()
        {
            await _InputLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrEmpty(_Request.StandardInput))
                {
                    await _Process.StandardInput.WriteAsync(_Request.StandardInput).ConfigureAwait(false);
                    await _Process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                // The process exited (or closed its input) before reading everything; its exit code tells the story.
            }
            finally
            {
                if (!_Request.KeepStandardInputOpen) CloseStandardInputCore();
                _InputLock.Release();
            }
        }

        private void CloseStandardInputCore()
        {
            if (_InputClosed) return;
            _InputClosed = true;
            try
            {
                _Process.StandardInput.Close();
            }
            catch
            {
            }
        }

        private async Task PumpStandardOutputAsync()
        {
            char[] buffer = new char[_ReadBufferSize];
            StringBuilder line = new StringBuilder();
            long limit = _Request.MaxOutputCharacters;

            try
            {
                StreamReader reader = _Process.StandardOutput;
                while (true)
                {
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        if (Interlocked.Increment(ref _StandardOutputCharacters) > limit)
                        {
                            _OutputTruncated = true;
                            Kill();
                            return;
                        }

                        char c = buffer[i];
                        if (c == '\n')
                        {
                            if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
                            _Lines.Writer.TryWrite(line.ToString());
                            line.Clear();
                        }
                        else
                        {
                            line.Append(c);
                        }
                    }
                }

                if (line.Length > 0) _Lines.Writer.TryWrite(line.ToString());
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
            }
            finally
            {
                _Lines.Writer.TryComplete();
            }
        }

        private async Task PumpStandardErrorAsync()
        {
            char[] buffer = new char[_ReadBufferSize];
            int limit = _Request.MaxStandardErrorCharacters;

            try
            {
                StreamReader reader = _Process.StandardError;
                while (true)
                {
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0) break;

                    lock (_StandardErrorLock)
                    {
                        int room = limit - _StandardError.Length;
                        if (room >= read)
                        {
                            _StandardError.Append(buffer, 0, read);
                        }
                        else
                        {
                            if (room > 0) _StandardError.Append(buffer, 0, room);
                            _StandardErrorTruncated = true;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
            }
        }

        private async Task MonitorAsync()
        {
            try
            {
                await _Process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            // A descendant that escaped the tree kill can hold the pipes open; do not wait on it forever.
            await Task.WhenAny(Task.WhenAll(_StandardOutputPump, _StandardErrorPump), Task.Delay(_StreamDrainGraceMs)).ConfigureAwait(false);
            _Lines.Writer.TryComplete();
            _TimeoutRegistration.Dispose();
        }

        #endregion
    }
}
