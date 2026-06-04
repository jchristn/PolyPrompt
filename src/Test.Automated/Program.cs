namespace Test.Automated
{
    using Test.Shared;
    using Touchstone.Cli;
    using Touchstone.Core;

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = ExtractOptionValue(ref args, "--results");

            if (args.Length == 1 && string.Equals(args[0], "selftest", StringComparison.OrdinalIgnoreCase))
            {
                return await ConsoleRunner.RunAsync(PolyPromptSuites.LocalOnly, resultsPath: resultsPath).ConfigureAwait(false);
            }

            ProviderTestConfiguration? configuration;

            if (args.Length == 0)
            {
                configuration = ProviderTestConfiguration.FromEnvironment();
            }
            else if (args.Length >= 2)
            {
                configuration = ProviderTestConfiguration.Create(
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
            Console.WriteLine("Usage: Test.Automated <provider> <endpoint> [apikey] [model] [embedding-model] [--results path]");
            Console.WriteLine("       Test.Automated selftest [--results path]");
            Console.WriteLine("       Test.Automated [--results path]  # Uses POLYPROMPT_TEST_* environment variables");
            Console.WriteLine();
            Console.WriteLine("  provider        : ollama | openai | gemini");
            Console.WriteLine("  endpoint        : Provider API endpoint URL");
            Console.WriteLine("  apikey          : API key (optional for Ollama)");
            Console.WriteLine("  model           : Inference model override (optional, uses provider default)");
            Console.WriteLine("  embedding-model : Embedding model override (optional, uses provider default)");
            Console.WriteLine();
            Console.WriteLine("Environment variables:");
            Console.WriteLine("  POLYPROMPT_TEST_PROVIDER");
            Console.WriteLine("  POLYPROMPT_TEST_ENDPOINT");
            Console.WriteLine("  POLYPROMPT_TEST_API_KEY");
            Console.WriteLine("  POLYPROMPT_TEST_MODEL");
            Console.WriteLine("  POLYPROMPT_TEST_EMBEDDING_MODEL");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Test.Automated selftest");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434");
            Console.WriteLine("  Test.Automated ollama http://localhost:11434 \"\" gemma3:4b all-minilm");
            Console.WriteLine("  Test.Automated openai https://api.openai.com sk-... gpt-4o text-embedding-3-small");
            Console.WriteLine("  Test.Automated gemini https://generativelanguage.googleapis.com AIza... gemini-2.5-flash gemini-embedding-001");
        }
    }
}
