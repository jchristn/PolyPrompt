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
                    configuration.Validate();
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
                Console.WriteLine("Endpoint        : " + (string.IsNullOrEmpty(configuration.Endpoint) ? "(regional default)" : configuration.Endpoint));
                Console.WriteLine("API Key         : " + (string.IsNullOrEmpty(configuration.ApiKey) ? "(none)" : "(set)"));
                Console.WriteLine("Inference model : " + (string.IsNullOrEmpty(configuration.InferenceModel) ? "(provider default)" : configuration.InferenceModel));
                Console.WriteLine("Embedding model : " + (string.IsNullOrEmpty(configuration.EmbeddingModel) ? "(provider default or n/a)" : configuration.EmbeddingModel));
                Console.WriteLine("Rerank model    : " + (string.IsNullOrEmpty(configuration.RerankModel) ? "(provider default or n/a)" : configuration.RerankModel));
                if (configuration.Region != null) Console.WriteLine("Region          : " + configuration.Region);
                if (configuration.Project != null) Console.WriteLine("Project         : " + configuration.Project);
                if (configuration.CredentialsPath != null) Console.WriteLine("Credentials     : " + configuration.CredentialsPath);
                if (configuration.ProviderType == "bedrock") Console.WriteLine("AWS credentials : " + (string.IsNullOrEmpty(configuration.AwsAccessKeyId) ? "(environment)" : "(set)"));
                if (configuration.ApiVersion != null) Console.WriteLine("API version     : " + configuration.ApiVersion);
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

            if (options.ContainsKey("--provider"))
            {
                string? genericProvider = GetOption(options, "--provider");
                if (string.IsNullOrWhiteSpace(genericProvider))
                    throw new ArgumentException("--provider requires a value.");
                if (ProviderTestConfiguration.ProviderTypes.Any(p => HasProviderOptions(options, p)))
                    throw new ArgumentException("--provider cannot be combined with provider-specific options.");

                return Build(genericProvider!, options, "--");
            }

            List<string> providers = ProviderTestConfiguration.ProviderTypes.Where(p => HasProviderOptions(options, p)).ToList();
            if (providers.Count == 0) return null;
            if (providers.Count > 1)
                throw new ArgumentException("Specify only one provider group at a time.");

            return Build(providers[0], options, "--" + providers[0] + "-");
        }

        /// <summary>
        /// Build a configuration from options named {prefix}endpoint, {prefix}key, {prefix}model, and so on. The generic
        /// form uses the prefix "--"; a provider group uses "--{provider}-".
        /// </summary>
        private static ProviderTestConfiguration Build(string provider, Dictionary<string, string?> options, string prefix)
        {
            ProviderTestConfiguration configuration = ProviderTestConfiguration.CreateWithDefaults(
                provider,
                GetOption(options, prefix + "endpoint"),
                GetFirstOption(options, prefix + "key", prefix + "api-key"),
                GetOption(options, prefix + "model"),
                GetOption(options, prefix + "embedding-model"));

            if (GetOption(options, prefix + "rerank-model") is string rerank) configuration.RerankModel = rerank;
            if (GetOption(options, prefix + "workspace") is string workspace) configuration.AnthropicWorkspaceId = workspace;
            if (GetOption(options, prefix + "region") is string region) configuration.Region = region;
            configuration.Project = GetOption(options, prefix + "project");
            configuration.CredentialsPath = GetOption(options, prefix + "credentials");
            configuration.AwsAccessKeyId = GetOption(options, prefix + "access-key-id");
            configuration.AwsSecretAccessKey = GetOption(options, prefix + "secret-access-key");
            configuration.AwsSessionToken = GetOption(options, prefix + "session-token");
            configuration.ApiVersion = GetOption(options, prefix + "api-version");
            configuration.Validate();
            return configuration;
        }

        private static bool HasProviderOptions(Dictionary<string, string?> options, string provider)
        {
            string prefix = "--" + provider + "-";
            return options.Keys.Any(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
            Console.WriteLine("Provider options: either the generic form");
            Console.WriteLine("  --provider <name> [--endpoint <url>] [--key <key>] [--model <m>] [--embedding-model <m>] [--rerank-model <m>]");
            Console.WriteLine("                    [--workspace <id>] [--region <r>] [--project <p>] [--credentials <path>]");
            Console.WriteLine("                    [--access-key-id <id>] [--secret-access-key <s>] [--session-token <t>] [--api-version <v>]");
            Console.WriteLine("or one provider group, with every option prefixed by the provider name, for example:");
            Console.WriteLine("  --openai-key <key> [--openai-model <m>] [--openai-embedding-model <m>]");
            Console.WriteLine("  --azure-endpoint <url> --azure-key <key> --azure-model <chat deployment> [--azure-embedding-model <deployment>] [--azure-api-version <v>]");
            Console.WriteLine("  --vertex-project <p> [--vertex-region <r>] [--vertex-credentials <service-account.json>]  (omit credentials to use ADC)");
            Console.WriteLine("  --bedrock-region <r> [--bedrock-access-key-id <id> --bedrock-secret-access-key <s> [--bedrock-session-token <t>]]  (omit keys to use AWS_* env)");
            Console.WriteLine("  --typesafe-key <key> [--typesafe-endpoint <url>] [--typesafe-model <m>]");
            Console.WriteLine();
            Console.WriteLine("  provider        : " + string.Join(" | ", ProviderTestConfiguration.ProviderTypes));
            Console.WriteLine("  endpoint        : Hosted APIs default to their public endpoint; Ollama and TEI default to localhost; Vertex AI and Bedrock");
            Console.WriteLine("                    derive a regional endpoint; Azure OpenAI requires the resource endpoint.");
            Console.WriteLine("  apikey          : Not used by Vertex AI or Bedrock; optional for Ollama and TEI.");
            Console.WriteLine("  model           : Inference (or decision) model; for Azure OpenAI, the chat deployment (required).");
            Console.WriteLine("  embedding-model : Embedding model; for Azure OpenAI, the embedding deployment (embedding cases skip without one).");
            Console.WriteLine("  rerank-model    : Rerank model (Cohere, VoyageAI, Bedrock).");
            Console.WriteLine();
            Console.WriteLine("Environment variables (generic, or one provider group):");
            Console.WriteLine("  POLYPROMPT_TEST_PROVIDER / ENDPOINT / API_KEY / MODEL / EMBEDDING_MODEL / RERANK_MODEL / REGION / PROJECT");
            Console.WriteLine("  POLYPROMPT_TEST_CREDENTIALS / AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY / AWS_SESSION_TOKEN / API_VERSION");
            Console.WriteLine("  POLYPROMPT_TEST_{OPENAI|OLLAMA|GEMINI}_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_ANTHROPIC_API_KEY / ENDPOINT / MODEL / WORKSPACE_ID");
            Console.WriteLine("  POLYPROMPT_TEST_{VOYAGEAI|COHERE}_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL / RERANK_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_TEI_API_KEY / ENDPOINT");
            Console.WriteLine("  POLYPROMPT_TEST_AZURE_API_KEY / ENDPOINT / MODEL / EMBEDDING_MODEL / API_VERSION");
            Console.WriteLine("  POLYPROMPT_TEST_VERTEX_PROJECT / REGION / CREDENTIALS / ENDPOINT / MODEL / EMBEDDING_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_BEDROCK_ACCESS_KEY_ID / SECRET_ACCESS_KEY / SESSION_TOKEN / REGION / ENDPOINT / MODEL / EMBEDDING_MODEL / RERANK_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_TYPESAFE_API_KEY / ENDPOINT / MODEL");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Test.Automated selftest");
            Console.WriteLine("  Test.Automated --openai-key sk-... --openai-model gpt-4o-mini");
            Console.WriteLine("  Test.Automated --ollama-endpoint http://localhost:11434 --ollama-model gemma3:4b");
            Console.WriteLine("  Test.Automated --gemini-key AIza... --gemini-model gemini-2.5-flash");
            Console.WriteLine("  Test.Automated --anthropic-key sk-ant-... --anthropic-model claude-opus-4-8");
            Console.WriteLine("  Test.Automated --azure-endpoint https://my.openai.azure.com --azure-key ... --azure-model gpt-4o");
            Console.WriteLine("  Test.Automated --vertex-project my-project --vertex-region us-central1");
            Console.WriteLine("  Test.Automated --bedrock-region us-east-1");
            Console.WriteLine("  Test.Automated --typesafe-key ts-...");
            Console.WriteLine("  Test.Automated --tei-endpoint http://localhost:8080");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434 \"\" gemma3:4b all-minilm");
        }
    }
}
