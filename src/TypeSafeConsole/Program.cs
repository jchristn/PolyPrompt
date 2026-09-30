namespace TypeSafeConsole
{
    using GetSomeInput;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    public class Program
    {
        #region Private-Members

        private static bool _RunForever = true;
        private static TypeSafeDecisionClient _Client = null!;

        #endregion

        #region Public-Methods

        public static async Task Main(string[] args)
        {
            Console.WriteLine("");
            Console.WriteLine("TypeSafeConsole - TypeSafe Decision Model Test Harness");
            Console.WriteLine("");
            Console.WriteLine("Use the hosted API (https://api.typesafe.ai) or a local Ollaya server (for example http://localhost:<port>).");
            Console.WriteLine("");

            string endpoint = Inputty.GetString("Endpoint [https://api.typesafe.ai]:", TypeSafeDecisionClient.DefaultEndpoint, false);
            string? apiKey = Inputty.GetString("API key (any value for a local Ollaya server):", null, true);
            string model = Inputty.GetString("Model [jev-latest]:", "jev-latest", false);
            int timeoutMs = Inputty.GetInteger("Timeout ms [120000]:", 120000, true, false);

            _Client = new TypeSafeDecisionClient(endpoint, string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
            _Client.Model = model;
            _Client.TimeoutMs = timeoutMs;

            Console.WriteLine("");
            Console.WriteLine("Client initialized. Decision models answer typed questions about a state with probabilities. Type ? for help.");
            Console.WriteLine("");

            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);
                await ProcessCommand(userInput).ConfigureAwait(false);
            }

            _Client.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            string trimmed = input.Trim().ToLowerInvariant();

            switch (trimmed)
            {
                case "?":
                case "help":
                    PrintMenu();
                    break;

                case "c":
                case "cls":
                    Console.Clear();
                    break;

                case "q":
                case "quit":
                case "exit":
                    _RunForever = false;
                    break;

                case "de":
                case "decide":
                    await DecideInteractiveAsync().ConfigureAwait(false);
                    break;

                case "sa":
                case "sample":
                    await DecideSampleAsync().ConfigureAwait(false);
                    break;

                case "ba":
                case "batch":
                    await DecideBatchSampleAsync().ConfigureAwait(false);
                    break;

                case "model":
                    SetModel();
                    break;

                case "settings":
                    PrintSettings();
                    break;

                case "calls":
                    PrintCallDetails();
                    break;

                case "val":
                case "validate":
                    await ValidateConnectivityAsync().ConfigureAwait(false);
                    break;

                default:
                    Console.WriteLine("Unknown command. Type ? for help.");
                    break;
            }
        }

        private static void PrintMenu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            Console.WriteLine("  ?/help           Show this help menu");
            Console.WriteLine("  c/cls            Clear the screen");
            Console.WriteLine("  de/decide        Enter a state and binary, choice, or score questions, then decide");
            Console.WriteLine("  sa/sample        Run a built-in support-ticket decision (choice, binary, and score)");
            Console.WriteLine("  ba/batch         Decide three sample tickets concurrently using the batch overload");
            Console.WriteLine("  model            Change the decision model");
            Console.WriteLine("  settings         Show current settings");
            Console.WriteLine("  calls            Show recorded HTTP call details");
            Console.WriteLine("  val/validate     Validate connectivity");
            Console.WriteLine("  q/quit/exit      Exit the application");
            Console.WriteLine("");
            Console.WriteLine("Decision models do not generate text: each question is answered with a value, a confidence,");
            Console.WriteLine("and a probability distribution. Binary questions are yes/no, choice questions pick one option,");
            Console.WriteLine("and score questions rate the state along 2 to 10 ordered levels (level i scores i).");
            Console.WriteLine("");
        }

        private static void PrintSettings()
        {
            Console.WriteLine("");
            Console.WriteLine("  Endpoint        : " + _Client.Endpoint);
            Console.WriteLine("  API key         : " + (string.IsNullOrEmpty(_Client.ApiKey) ? "(none)" : "(set)"));
            Console.WriteLine("  Model           : " + _Client.Model);
            Console.WriteLine("  Timeout ms      : " + _Client.TimeoutMs);
            Console.WriteLine("  Max concurrency : " + _Client.MaxConcurrency);
            Console.WriteLine("");
        }

        private static void SetModel()
        {
            Console.WriteLine("");
            Console.WriteLine("Current model: " + _Client.Model);
            string? newModel = Inputty.GetString("Model [Enter to keep]:", null, true);
            if (!string.IsNullOrWhiteSpace(newModel))
            {
                _Client.Model = newModel.Trim();
                Console.WriteLine("Model set to " + _Client.Model + ".");
            }
            else
            {
                Console.WriteLine("Model unchanged.");
            }
            Console.WriteLine("");
        }

        private static List<string> ReadLines(string label, string prompt)
        {
            List<string> lines = new List<string>();
            Console.WriteLine("Enter " + label + " (empty line to finish):");
            while (true)
            {
                string? line = Inputty.GetString(prompt + " [Enter to finish]:", null, true);
                if (string.IsNullOrWhiteSpace(line)) break;
                lines.Add(line.Trim());
            }
            return lines;
        }

        private static DecisionQuestion? ReadQuestion(int number)
        {
            Console.WriteLine("");
            Console.WriteLine("--- Question " + number + " ---");

            string id = Inputty.GetString("Question id [q" + number + "]:", "q" + number, false);
            string instructions = Inputty.GetString("Instructions (the question to answer):", null, false);
            string type = Inputty.GetString("Type (binary/choice/score) [binary]:", "binary", false).Trim().ToLowerInvariant();

            switch (type)
            {
                case "b":
                case "binary":
                    return DecisionQuestion.Binary(id, instructions);

                case "c":
                case "choice":
                    List<string> optionLines = ReadLines("options as value or value: description (at least 2)", "Option");
                    List<DecisionOption> options = new List<DecisionOption>();
                    foreach (string line in optionLines)
                    {
                        int separator = line.IndexOf(':');
                        if (separator > 0 && separator < line.Length - 1)
                        {
                            options.Add(DecisionOption.Of(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim()));
                        }
                        else
                        {
                            options.Add(DecisionOption.Of(line.TrimEnd(':').Trim()));
                        }
                    }

                    if (options.Count < ChoiceQuestion.MinOptions)
                    {
                        Console.WriteLine("A choice question needs at least " + ChoiceQuestion.MinOptions + " options; question skipped.");
                        return null;
                    }

                    return DecisionQuestion.Choice(id, instructions, options.ToArray());

                case "s":
                case "score":
                    List<string> levels = ReadLines("score levels from lowest to highest (" + ScoreQuestion.MinLevels + " to " + ScoreQuestion.MaxLevels + ")", "Level");
                    if (levels.Count < ScoreQuestion.MinLevels || levels.Count > ScoreQuestion.MaxLevels)
                    {
                        Console.WriteLine("A score question needs " + ScoreQuestion.MinLevels + " to " + ScoreQuestion.MaxLevels + " levels; question skipped.");
                        return null;
                    }

                    return DecisionQuestion.Score(id, instructions, levels);

                default:
                    Console.WriteLine("Unknown question type '" + type + "'; question skipped.");
                    return null;
            }
        }

        private static async Task DecideInteractiveAsync()
        {
            string state = Inputty.GetString("State (the text to decide about):", null, false);

            DecisionRequest request = new DecisionRequest { State = state };

            int number = 1;
            while (true)
            {
                DecisionQuestion? question = ReadQuestion(number);
                if (question != null)
                {
                    request.Questions.Add(question);
                    number++;
                }

                if (!Inputty.GetBoolean("Add another question?", false)) break;
            }

            if (request.Questions.Count == 0)
            {
                Console.WriteLine("No questions provided.");
                return;
            }

            await SendAndPrintAsync(request).ConfigureAwait(false);
        }

        private static async Task DecideSampleAsync()
        {
            DecisionRequest request = BuildTicketRequest(
                "Customer: I was charged twice for my subscription this month and I need the duplicate refunded today. "
                + "This is the third time I have written in and nobody has answered.");

            Console.WriteLine("");
            Console.WriteLine("State: " + request.State);

            await SendAndPrintAsync(request).ConfigureAwait(false);
        }

        private static async Task DecideBatchSampleAsync()
        {
            List<string> tickets = new List<string>
            {
                "Customer: I was charged twice for my subscription this month and I need the duplicate refunded today.",
                "Customer: How do I change the email address on my account? No rush, whenever you get a chance.",
                "Customer: The app crashes every time I open the reports page. I have a board meeting in an hour and need those numbers."
            };

            List<DecisionRequest> requests = tickets.Select(BuildTicketRequest).ToList();

            Console.WriteLine("");
            Console.WriteLine("Sending " + requests.Count + " requests (max concurrency " + _Client.MaxConcurrency + ")...");
            Console.WriteLine("");

            try
            {
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                List<DecisionResponse> responses = await _Client.DecideAsync(requests).ConfigureAwait(false);
                stopwatch.Stop();

                for (int i = 0; i < responses.Count; i++)
                {
                    Console.WriteLine("=== Request " + i + " ===");
                    Console.WriteLine("State: " + requests[i].State);
                    PrintResponse(requests[i], responses[i]);
                    Console.WriteLine("");
                }

                Console.WriteLine("--- Batch ---");
                Console.WriteLine("  Succeeded     : " + responses.Count(r => r.Success) + " of " + responses.Count);
                Console.WriteLine("  Batch runtime : " + stopwatch.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static DecisionRequest BuildTicketRequest(string ticket)
        {
            return new DecisionRequest
            {
                State = ticket,
                Questions =
                {
                    DecisionQuestion.Choice(
                        "intent",
                        "What does the customer want?",
                        DecisionOption.Of("refund", "Money back for a charge"),
                        DecisionOption.Of("account", "A change to account details"),
                        DecisionOption.Of("bug", "A product defect to be fixed"),
                        DecisionOption.Of("other", "Anything else")),
                    DecisionQuestion.Binary("urgent", "Is this request time-sensitive?"),
                    DecisionQuestion.Score("tone", "How upset is the customer?", "calm", "annoyed", "angry")
                }
            };
        }

        private static async Task SendAndPrintAsync(DecisionRequest request)
        {
            Console.WriteLine("");

            try
            {
                DecisionResponse response = await _Client.DecideAsync(request).ConfigureAwait(false);
                PrintResponse(request, response);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        private static void PrintResponse(DecisionRequest request, DecisionResponse response)
        {
            if (!response.Success)
            {
                Console.WriteLine("Error: " + response.Error);
                if (response.StatusCode.HasValue) Console.WriteLine("  HTTP status : " + response.StatusCode.Value);
                Console.WriteLine("  Runtime     : " + response.OverallRuntimeMs + " ms");
                return;
            }

            foreach (DecisionQuestion question in request.Questions)
            {
                if (!response.Answers.TryGetValue(question.Id, out DecisionAnswer? answer))
                {
                    Console.WriteLine("  [" + question.Id + "] (no answer)");
                    continue;
                }

                PrintAnswer(question, answer);
            }

            Console.WriteLine("");
            Console.WriteLine("--- Metadata ---");
            Console.WriteLine("  Model         : " + response.Model);
            if (response.Usage != null)
            {
                if (response.Usage.PromptTokens.HasValue) Console.WriteLine("  Prompt tokens : " + response.Usage.PromptTokens.Value);
                if (response.Usage.CompletionTokens.HasValue) Console.WriteLine("  Compl. tokens : " + response.Usage.CompletionTokens.Value);
                if (response.Usage.TotalTokens.HasValue) Console.WriteLine("  Total tokens  : " + response.Usage.TotalTokens.Value);
            }
            Console.WriteLine("--- Timing ---");
            Console.WriteLine("  Runtime       : " + response.OverallRuntimeMs + " ms");
        }

        private static void PrintAnswer(DecisionQuestion question, DecisionAnswer answer)
        {
            Console.WriteLine("  [" + answer.Id + "] " + answer.Type.ToString().ToLowerInvariant() + " - " + question.Instructions);

            switch (answer)
            {
                case BinaryAnswer binary:
                    Console.WriteLine("      Value       : " + (binary.Value ? "true" : "false"));
                    Console.WriteLine("      Probability : " + binary.Probability.ToString("F4"));
                    break;

                case ChoiceAnswer choice:
                    Console.WriteLine("      Value       : " + choice.Value);
                    break;

                case ScoreAnswer score:
                    Console.WriteLine("      Score       : " + score.Value.ToString("F4") + DescribeScore(question, score.Value));
                    foreach (KeyValuePair<string, string> entry in score.Legend)
                    {
                        Console.WriteLine("      Legend " + entry.Key.PadRight(4) + " : " + entry.Value);
                    }
                    break;
            }

            Console.WriteLine("      Confidence  : " + (answer.Confidence?.ToString("F4") ?? "(n/a)"));

            if (answer.Probabilities.Count > 0)
            {
                Console.WriteLine("      Distribution:");
                foreach (KeyValuePair<string, double> entry in answer.Probabilities.OrderByDescending(p => p.Value))
                {
                    Console.WriteLine("        " + entry.Key.PadRight(16) + " " + entry.Value.ToString("F4") + "  " + new string('#', (int)Math.Round(entry.Value * 20)));
                }
            }
        }

        private static string DescribeScore(DecisionQuestion question, double value)
        {
            if (question is not ScoreQuestion score || score.Levels.Count == 0) return "";

            int nearest = (int)Math.Round(value);
            if (nearest < 0) nearest = 0;
            if (nearest >= score.Levels.Count) nearest = score.Levels.Count - 1;
            return " (nearest level " + nearest + ": " + score.Levels[nearest] + ")";
        }

        private static void PrintCallDetails()
        {
            Console.WriteLine("");

            List<CallDetail> details = _Client.CallDetails;
            if (details.Count == 0)
            {
                Console.WriteLine("No calls recorded.");
                Console.WriteLine("");
                return;
            }

            bool showBodies = Inputty.GetBoolean("Show request and response bodies?", false);
            Console.WriteLine("");

            int index = 0;
            foreach (CallDetail detail in details)
            {
                Console.WriteLine("  [" + index + "] " + detail.TimestampUtc.ToString("HH:mm:ss.fff") + "  " + detail.Method + " " + detail.Url);
                Console.WriteLine("      Status   : " + (detail.StatusCode?.ToString() ?? "(none)") + (detail.Success ? " (success)" : " (failed)"));
                Console.WriteLine("      Time     : " + (detail.ResponseTimeMs?.ToString() ?? "(n/a)") + " ms");
                if (!string.IsNullOrEmpty(detail.Error)) Console.WriteLine("      Error    : " + detail.Error);
                if (showBodies)
                {
                    Console.WriteLine("      Request  : " + Truncate(detail.RequestBody, 1000));
                    Console.WriteLine("      Response : " + Truncate(detail.ResponseBody, 1000));
                }
                index++;
            }

            Console.WriteLine("");
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "(none)";
            return text.Length <= max ? text : text.Substring(0, max) + "... (" + text.Length + " chars)";
        }

        private static async Task ValidateConnectivityAsync()
        {
            Console.WriteLine("");

            try
            {
                bool ok = await _Client.ValidateConnectivityAsync().ConfigureAwait(false);
                Console.WriteLine(ok ? "Connectivity: OK" : "Connectivity: FAILED");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }

            Console.WriteLine("");
        }

        #endregion
    }
}
