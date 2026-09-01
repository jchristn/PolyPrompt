namespace Test.Automated
{
    using Test.Shared;
    using Touchstone.Cli;
    using Touchstone.Core;

    /// <summary>
    /// Entry point for running PolyPrompt automated Touchstone tests.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs deterministic local tests and optional live provider tests.
        /// </summary>
        /// <param name="args">Command-line arguments for provider selection and result output.</param>
        /// <returns>Process exit code returned by the Touchstone console runner.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = ExtractOptionValue(ref args, "--results");

            if (args.Length == 1
                && (string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase)))
            {
                PrintUsage();
                return 0;
            }

            if (args.Length == 1 && string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase))
            {
                return await ConsoleRunner.RunAsync(PolyPromptSuites.LocalOnly, resultsPath: resultsPath).ConfigureAwait(false);
            }

            ProviderTestConfiguration? configuration;

            try
            {
                if (args.Length == 0)
                {
                    configuration = ProviderTestConfiguration.FromEnvironment();
                }
                else if (HasNamedArguments(args))
                {
                    configuration = CreateConfigurationFromNamedArguments(args);
                }
                else if (args.Length >= 2)
                {
                    configuration = ProviderTestConfiguration.CreateWithDefaults(
                        args[0],
                        args[1],
                        args.Length >= 3 ? args[2] : null,
                        args.Length >= 4 ? args[3] : null,
                        args.Length >= 5 ? args[4] : null);
                }
                else
                {
                    PrintUsage();
                    return 1;
                }
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine("Configuration error: " + ex.Message);
                Console.WriteLine();
                PrintUsage();
                return 1;
            }

            IReadOnlyList<TestSuiteDescriptor> suites = PolyPromptSuites.FromConfiguration(
                configuration,
                includeLocal: true,
                includeSkippedProviderPlaceholder: true);

            if (configuration != null)
            {
                Console.WriteLine("Provider        : " + configuration.ProviderType);
                Console.WriteLine("Endpoint        : " + configuration.Endpoint);
                Console.WriteLine("API Key         : " + (string.IsNullOrEmpty(configuration.ApiKey) ? "(none)" : "(set)"));
                Console.WriteLine("Inference model : " + (string.IsNullOrEmpty(configuration.InferenceModel) ? "(provider default)" : configuration.InferenceModel));
                Console.WriteLine("Embedding model : " + configuration.EmbeddingModel);
                Console.WriteLine();
            }
            else if (args.Length > 0)
            {
                PrintUsage();
                return 1;
            }

            return await ConsoleRunner.RunAsync(suites, resultsPath: resultsPath).ConfigureAwait(false);
        }

        private static bool HasNamedArguments(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static ProviderTestConfiguration? CreateConfigurationFromNamedArguments(string[] args)
        {
            Dictionary<string, string?> options = ParseNamedArguments(args);

            string? genericProvider = GetOption(options, "--provider");
            if (!string.IsNullOrWhiteSpace(genericProvider))
            {
                EnsureNoProviderSpecificOptions(options, genericProvider);
                return ProviderTestConfiguration.CreateWithDefaults(
                    genericProvider,
                    GetOption(options, "--endpoint"),
                    GetApiKeyOption(options),
                    GetOption(options, "--model"),
                    GetOption(options, "--embedding-model"));
            }

            bool hasOpenAi = HasAnyOption(options, "--openai-key", "--openai-api-key", "--openai-endpoint", "--openai-model", "--openai-embedding-model");
            bool hasOllama = HasAnyOption(options, "--ollama-key", "--ollama-api-key", "--ollama-endpoint", "--ollama-model", "--ollama-embedding-model");
            bool hasGemini = HasAnyOption(options, "--gemini-key", "--gemini-api-key", "--gemini-endpoint", "--gemini-model", "--gemini-embedding-model");
            bool hasAnthropic = HasAnyOption(options, "--anthropic-key", "--anthropic-api-key", "--anthropic-endpoint", "--anthropic-model", "--anthropic-workspace");

            int providerCount = 0;
            if (hasOpenAi) providerCount++;
            if (hasOllama) providerCount++;
            if (hasGemini) providerCount++;
            if (hasAnthropic) providerCount++;

            if (providerCount == 0) return null;

            if (providerCount > 1)
                throw new ArgumentException("Specify only one provider group at a time.");

            if (hasOpenAi)
            {
                return ProviderTestConfiguration.CreateWithDefaults(
                    "openai",
                    GetOption(options, "--openai-endpoint"),
                    GetFirstOption(options, "--openai-key", "--openai-api-key"),
                    GetOption(options, "--openai-model"),
                    GetOption(options, "--openai-embedding-model"));
            }

            if (hasOllama)
            {
                return ProviderTestConfiguration.CreateWithDefaults(
                    "ollama",
                    GetOption(options, "--ollama-endpoint"),
                    GetFirstOption(options, "--ollama-key", "--ollama-api-key"),
                    GetOption(options, "--ollama-model"),
                    GetOption(options, "--ollama-embedding-model"));
            }

            if (hasAnthropic)
            {
                ProviderTestConfiguration anthropic = ProviderTestConfiguration.CreateWithDefaults(
                    "anthropic",
                    GetOption(options, "--anthropic-endpoint"),
                    GetFirstOption(options, "--anthropic-key", "--anthropic-api-key"),
                    GetOption(options, "--anthropic-model"),
                    null);
                anthropic.AnthropicWorkspaceId = GetOption(options, "--anthropic-workspace");
                return anthropic;
            }

            return ProviderTestConfiguration.CreateWithDefaults(
                "gemini",
                GetOption(options, "--gemini-endpoint"),
                GetFirstOption(options, "--gemini-key", "--gemini-api-key"),
                GetOption(options, "--gemini-model"),
                GetOption(options, "--gemini-embedding-model"));
        }

        private static Dictionary<string, string?> ParseNamedArguments(string[] args)
        {
            Dictionary<string, string?> options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string name = args[i];
                if (!name.StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Unexpected positional argument '" + name + "' in named argument mode.");

                string? value = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[i + 1];
                    i++;
                }

                options[name] = value;
            }

            return options;
        }

        private static void EnsureNoProviderSpecificOptions(Dictionary<string, string?> options, string provider)
        {
            bool hasOpenAi = HasAnyOption(options, "--openai-key", "--openai-api-key", "--openai-endpoint", "--openai-model", "--openai-embedding-model");
            bool hasOllama = HasAnyOption(options, "--ollama-key", "--ollama-api-key", "--ollama-endpoint", "--ollama-model", "--ollama-embedding-model");
            bool hasGemini = HasAnyOption(options, "--gemini-key", "--gemini-api-key", "--gemini-endpoint", "--gemini-model", "--gemini-embedding-model");
            bool hasAnthropic = HasAnyOption(options, "--anthropic-key", "--anthropic-api-key", "--anthropic-endpoint", "--anthropic-model", "--anthropic-workspace");

            if (hasOpenAi || hasOllama || hasGemini || hasAnthropic)
                throw new ArgumentException("--provider cannot be combined with provider-specific options.");

            if (string.IsNullOrWhiteSpace(provider))
                throw new ArgumentException("--provider requires a value.");
        }

        private static string? GetApiKeyOption(Dictionary<string, string?> options)
        {
            return GetFirstOption(options, "--key", "--api-key");
        }

        private static bool HasAnyOption(Dictionary<string, string?> options, params string[] names)
        {
            foreach (string name in names)
            {
                if (options.ContainsKey(name)) return true;
            }

            return false;
        }

        private static string? GetFirstOption(Dictionary<string, string?> options, params string[] names)
        {
            foreach (string name in names)
            {
                string? value = GetOption(options, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        private static string? GetOption(Dictionary<string, string?> options, string name)
        {
            if (!options.TryGetValue(name, out string? value)) return null;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? ExtractOptionValue(ref string[] args, string optionName)
        {
            List<string> remaining = new List<string>();
            string? value = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                        throw new ArgumentException(optionName + " requires a value.");

                    value = args[i + 1];
                    i++;
                    continue;
                }

                remaining.Add(args[i]);
            }

            args = remaining.ToArray();
            return value;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: Test.Automated [provider options] [--results path]");
            Console.WriteLine("       Test.Automated <provider> <endpoint> [apikey] [model] [embedding-model] [--results path]");
            Console.WriteLine("       Test.Automated selftest [--results path]");
            Console.WriteLine("       Test.Automated [--results path]  # Uses POLYPROMPT_TEST_* environment variables");
            Console.WriteLine();
            Console.WriteLine("Named provider options:");
            Console.WriteLine("  --openai-key <key> [--openai-endpoint <url>] [--openai-model <model>] [--openai-embedding-model <model>]");
            Console.WriteLine("  --ollama-endpoint <url> [--ollama-key <key>] [--ollama-model <model>] [--ollama-embedding-model <model>]");
            Console.WriteLine("  --gemini-key <key> [--gemini-endpoint <url>] [--gemini-model <model>] [--gemini-embedding-model <model>]");
            Console.WriteLine("  --anthropic-key <key> [--anthropic-endpoint <url>] [--anthropic-model <model>] [--anthropic-workspace <id>]");
            Console.WriteLine("  --provider <ollama|openai|gemini|anthropic> [--endpoint <url>] [--key <key>] [--model <model>] [--embedding-model <model>]");
            Console.WriteLine();
            Console.WriteLine("  provider        : ollama | openai | gemini | anthropic");
            Console.WriteLine("  endpoint        : Provider API endpoint URL. OpenAI, Gemini, and Anthropic default to their public APIs.");
            Console.WriteLine("  apikey          : API key (optional for Ollama)");
            Console.WriteLine("  model           : Inference model override (optional, uses provider default)");
            Console.WriteLine("  embedding-model : Embedding model override (optional; Anthropic has no embeddings API)");
            Console.WriteLine();
            Console.WriteLine("Environment variables:");
            Console.WriteLine("  POLYPROMPT_TEST_PROVIDER");
            Console.WriteLine("  POLYPROMPT_TEST_ENDPOINT");
            Console.WriteLine("  POLYPROMPT_TEST_API_KEY");
            Console.WriteLine("  POLYPROMPT_TEST_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_OPENAI_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_OLLAMA_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_GEMINI_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_ANTHROPIC_API_KEY / ENDPOINT / MODEL / WORKSPACE_ID");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Test.Automated selftest");
            Console.WriteLine("  Test.Automated --openai-key sk-... --openai-model gpt-4o-mini");
            Console.WriteLine("  Test.Automated --ollama-endpoint http://localhost:11434 --ollama-model gemma3:4b");
            Console.WriteLine("  Test.Automated --gemini-key AIza... --gemini-model gemini-2.5-flash");
            Console.WriteLine("  Test.Automated --anthropic-key sk-ant-... --anthropic-model claude-opus-4-8");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434 \"\" gemma3:4b all-minilm");
        }
    }
}
