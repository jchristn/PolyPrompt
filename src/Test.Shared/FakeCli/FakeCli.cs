namespace Test.Shared
{
    using System.Reflection;
    using System.Text;
    using PolyPrompt.Clients;

    /// <summary>
    /// A fake agent harness CLI for tests: a POSIX shell shim installed in a private temporary directory under a harness
    /// executable name (for example <c>codex</c>) and placed first on the client's PATH. It records its arguments,
    /// standard input, and working directory, and plays a recorded scenario selected by
    /// <c>POLYPROMPT_FAKE_CLI_SCRIPT</c>. Scenarios live in <c>FakeCli/Scripts/*.txt</c> as embedded resources; see
    /// <c>FakeCli/fake-cli.sh</c> for the directive language. Shared by the NUnit, xUnit, automated, and AOT runners.
    /// </summary>
    public sealed class FakeCli : IDisposable
    {
        #region Private-Members

        private const string ResourcePrefix = "FakeCli/";
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// Private directory holding the shim, the scenarios, and the record of the last invocation.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// Directory placed first on PATH; contains only the shim.
        /// </summary>
        public string BinDirectory { get; }

        /// <summary>
        /// Executable name the shim is installed under.
        /// </summary>
        public string ExecutableName { get; }

        /// <summary>
        /// Full path of the shim.
        /// </summary>
        public string ExecutablePath { get; }

        /// <summary>
        /// Scenario the shim plays.
        /// </summary>
        public string Scenario { get; private set; }

        /// <summary>
        /// Arguments of the last invocation.
        /// </summary>
        public List<string> Arguments
        {
            get
            {
                string path = Path.Combine(Directory, "record", "argv");
                if (!File.Exists(path)) return new List<string>();
                string content = File.ReadAllText(path, Encoding.UTF8);
                List<string> args = content.Split('\0').ToList();
                if (args.Count > 0 && args[args.Count - 1].Length == 0) args.RemoveAt(args.Count - 1);
                return args;
            }
        }

        /// <summary>
        /// Standard input recorded by the last invocation's <c>stdin</c> or <c>readline</c> directives, or null.
        /// </summary>
        public string? StandardInput
        {
            get { return ReadRecord("stdin"); }
        }

        /// <summary>
        /// Working directory of the last invocation.
        /// </summary>
        public string? WorkingDirectory
        {
            get { return ReadRecord("cwd")?.TrimEnd('\n'); }
        }

        /// <summary>
        /// One line per invocation: the arguments joined by spaces.
        /// </summary>
        public List<string> Invocations
        {
            get
            {
                string? content = ReadRecord("invocations");
                if (content == null) return new List<string>();
                return content.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
        }

        /// <summary>
        /// Value of <c>POLYPROMPT_FAKE_CLI_ECHO</c> seen by the last invocation (used to verify environment passing).
        /// </summary>
        public string? EchoedEnvironment
        {
            get { return ReadRecord("echo"); }
        }

        /// <summary>
        /// Process id of the background child started by a <c>child</c> directive, or null.
        /// </summary>
        public int? ChildProcessId
        {
            get
            {
                string? content = ReadRecord("child.pid");
                return int.TryParse(content?.Trim(), out int pid) ? pid : null;
            }
        }

        /// <summary>
        /// Process id of the shim itself in the last invocation, or null.
        /// </summary>
        public int? ProcessId
        {
            get
            {
                string? content = ReadRecord("pid");
                return int.TryParse(content?.Trim(), out int pid) ? pid : null;
            }
        }

        #endregion

        #region Constructors-and-Factories

        private FakeCli(string executableName, string scenario)
        {
            ExecutableName = executableName;
            Scenario = scenario;
            Directory = Path.Combine(Path.GetTempPath(), "polyprompt-fakecli-" + Guid.NewGuid().ToString("N"));
            BinDirectory = Path.Combine(Directory, "bin");
            System.IO.Directory.CreateDirectory(BinDirectory);
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "scripts"));
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "record"));

            Assembly assembly = typeof(FakeCli).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
                string fileName = name.Substring(ResourcePrefix.Length);
                using Stream stream = assembly.GetManifestResourceStream(name)!;
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                string content = reader.ReadToEnd().Replace("\r\n", "\n");

                if (fileName == "fake-cli.sh")
                {
                    ExecutablePath = Path.Combine(BinDirectory, executableName);
                    File.WriteAllText(ExecutablePath, content, new UTF8Encoding(false));
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(ExecutablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
                }
                else
                {
                    File.WriteAllText(Path.Combine(Directory, "scripts", fileName), content, new UTF8Encoding(false));
                }
            }

            if (ExecutablePath == null) throw new InvalidOperationException("The fake CLI shim resource is missing.");
        }

        /// <summary>
        /// Install the shim under <paramref name="executableName"/> playing <paramref name="scenario"/>.
        /// </summary>
        /// <param name="executableName">Harness executable name, for example <c>codex</c>.</param>
        /// <param name="scenario">Scenario name (file name in <c>FakeCli/Scripts</c> without <c>.txt</c>).</param>
        /// <returns>The fake CLI.</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown on Windows, which has no POSIX shell.</exception>
        public static FakeCli Create(string executableName, string scenario)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The fake agent CLI is a POSIX shell script and needs macOS or Linux.");
            return new FakeCli(executableName, scenario);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Point a client at the shim: the shim's directory goes first on the client's PATH, the executable is the bare
        /// harness name (so PATH resolution is exercised), and the scenario variables are set.
        /// </summary>
        /// <typeparam name="T">Client type.</typeparam>
        /// <param name="client">Client.</param>
        /// <returns>The client.</returns>
        public T Attach<T>(T client) where T : CliAgentClientBase
        {
            client.Executable = ExecutableName;
            client.Environment["PATH"] = BinDirectory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "");
            client.Environment["POLYPROMPT_FAKE_CLI_DIR"] = Directory;
            client.Environment["POLYPROMPT_FAKE_CLI_SCRIPT"] = Scenario;
            return client;
        }

        /// <summary>
        /// Switch an attached client to another scenario and clear the previous record.
        /// </summary>
        /// <param name="client">Client attached with <see cref="Attach{T}"/>.</param>
        /// <param name="scenario">Scenario name.</param>
        public void UseScenario(CliAgentClientBase client, string scenario)
        {
            Scenario = scenario;
            client.Environment["POLYPROMPT_FAKE_CLI_SCRIPT"] = scenario;
            ClearRecord();
        }

        /// <summary>
        /// Delete the record of earlier invocations.
        /// </summary>
        public void ClearRecord()
        {
            string record = Path.Combine(Directory, "record");
            if (System.IO.Directory.Exists(record)) System.IO.Directory.Delete(record, true);
            System.IO.Directory.CreateDirectory(record);
        }

        /// <summary>
        /// True when a process with <paramref name="pid"/> is still running.
        /// </summary>
        /// <param name="pid">Process id.</param>
        /// <returns>True when alive.</returns>
        public static bool IsProcessAlive(int pid)
        {
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Wait up to <paramref name="timeoutMs"/> for a process to exit.
        /// </summary>
        /// <param name="pid">Process id.</param>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>True when the process is gone.</returns>
        public static async Task<bool> WaitForProcessExitAsync(int pid, int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (!IsProcessAlive(pid)) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }
            return !IsProcessAlive(pid);
        }

        /// <summary>
        /// Delete the temporary directory.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            try
            {
                System.IO.Directory.Delete(Directory, true);
            }
            catch
            {
            }
        }

        #endregion

        #region Private-Methods

        private string? ReadRecord(string name)
        {
            string path = Path.Combine(Directory, "record", name);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        #endregion
    }
}
