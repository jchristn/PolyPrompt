namespace Test.Shared
{
    internal static class LocalRequestParser
    {
        private static readonly SerializationHelper.Serializer _Serializer = new SerializationHelper.Serializer();

        public static LocalOpenAiChatRequest? DeserializeOpenAiChatRequest(string requestBody)
        {
            Dictionary<string, object>? data = DeserializeDictionary(requestBody);
            if (data == null) return null;

            LocalOpenAiChatRequest request = new LocalOpenAiChatRequest();
            request.Model = GetString(data, "model");
            request.ToolChoice = GetString(data, "tool_choice");
            request.Stream = GetBool(data, "stream");
            request.Messages = ParseOpenAiMessages(GetList(data, "messages"));
            request.Tools = ParseOpenAiTools(GetList(data, "tools"));
            request.ReasoningEffort = GetString(data, "reasoning_effort");
            request.Think = GetString(data, "think");
            request.ThinkBool = GetBool(data, "think");
            return request;
        }

        public static LocalGeminiRequest? DeserializeGeminiRequest(string requestBody)
        {
            Dictionary<string, object>? data = DeserializeDictionary(requestBody);
            if (data == null) return null;

            LocalGeminiRequest request = new LocalGeminiRequest();
            request.Contents = ParseGeminiContents(GetList(data, "contents"));
            request.Tools = ParseGeminiTools(GetList(data, "tools"));

            Dictionary<string, object>? systemInstruction = GetDictionary(data, "systemInstruction");
            if (systemInstruction != null)
            {
                request.SystemInstruction = ParseGeminiContent(systemInstruction);
            }

            request.GenerationConfig = ParseGeminiGenerationConfig(GetDictionary(data, "generationConfig"));
            return request;
        }

        public static LocalAnthropicRequest? DeserializeAnthropicRequest(string requestBody)
        {
            Dictionary<string, object>? data = DeserializeDictionary(requestBody);
            if (data == null) return null;

            LocalAnthropicRequest request = new LocalAnthropicRequest();
            request.Model = GetString(data, "model");
            request.MaxTokens = GetInt(data, "max_tokens");
            request.Stream = GetBool(data, "stream");
            request.System = GetString(data, "system");
            request.Temperature = GetDouble(data, "temperature");
            request.TopP = GetDouble(data, "top_p");
            request.TopK = GetInt(data, "top_k");
            request.StopSequences = GetStringList(data, "stop_sequences");
            request.Messages = ParseAnthropicMessages(GetList(data, "messages"));
            request.Tools = ParseAnthropicTools(GetList(data, "tools"));
            request.ToolChoice = ParseAnthropicToolChoice(GetDictionary(data, "tool_choice"));
            request.Thinking = ParseAnthropicThinking(GetDictionary(data, "thinking"));
            request.OutputConfig = ParseAnthropicOutputConfig(GetDictionary(data, "output_config"));
            return request;
        }

        public static LocalEmbeddingRequest? DeserializeEmbeddingRequest(string requestBody)
        {
            Dictionary<string, object>? data = DeserializeDictionary(requestBody);
            if (data == null) return null;

            LocalEmbeddingRequest request = new LocalEmbeddingRequest();
            request.Model = GetString(data, "model");
            request.Input = GetStringList(data, "input");
            request.Dimensions = GetInt(data, "dimensions");
            request.EncodingFormat = GetString(data, "encoding_format");
            request.Truncate = GetInt(data, "truncate");
            request.Options = GetDictionary(data, "options");
            return request;
        }

        public static LocalGenerateRequest? DeserializeGenerateRequest(string requestBody)
        {
            Dictionary<string, object>? data = DeserializeDictionary(requestBody);
            if (data == null) return null;

            LocalGenerateRequest request = new LocalGenerateRequest();
            request.Model = GetString(data, "model");
            request.Prompt = GetString(data, "prompt");
            request.MaxTokens = GetInt(data, "max_tokens");
            request.Stream = GetBool(data, "stream");
            request.Temperature = GetDouble(data, "temperature");
            request.TopP = GetDouble(data, "top_p");
            request.Options = GetDictionary(data, "options");

            if (request.Options != null)
            {
                int? numPredict = GetInt(request.Options, "num_predict");
                if (numPredict.HasValue) request.MaxTokens = numPredict.Value;
                request.Temperature = GetDouble(request.Options, "temperature") ?? request.Temperature;
                request.TopP = GetDouble(request.Options, "top_p") ?? request.TopP;
            }

            return request;
        }

        public static Dictionary<string, string> DeserializeStringDictionary(string json)
        {
            Dictionary<string, string>? result = _Serializer.DeserializeJson<Dictionary<string, string>>(json);
            return result ?? new Dictionary<string, string>();
        }

        private static Dictionary<string, object>? DeserializeDictionary(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return _Serializer.DeserializeJson<Dictionary<string, object>>(json);
        }

        private static List<LocalOpenAiChatMessage>? ParseOpenAiMessages(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalOpenAiChatMessage> result = new List<LocalOpenAiChatMessage>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalOpenAiChatMessage message = new LocalOpenAiChatMessage();
                message.Role = GetString(item, "role");
                message.Content = GetString(item, "content");
                message.ToolCallId = GetString(item, "tool_call_id");
                message.ToolCalls = ParseOpenAiToolCalls(GetList(item, "tool_calls"));
                result.Add(message);
            }

            return result;
        }

        private static List<LocalOpenAiTool>? ParseOpenAiTools(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalOpenAiTool> result = new List<LocalOpenAiTool>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalOpenAiTool tool = new LocalOpenAiTool();
                tool.Type = GetString(item, "type");
                tool.Function = ParseOpenAiToolFunction(GetDictionary(item, "function"));
                result.Add(tool);
            }

            return result;
        }

        private static List<LocalOpenAiToolCall>? ParseOpenAiToolCalls(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalOpenAiToolCall> result = new List<LocalOpenAiToolCall>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalOpenAiToolCall toolCall = new LocalOpenAiToolCall();
                toolCall.Id = GetString(item, "id");
                toolCall.Type = GetString(item, "type");
                toolCall.Function = ParseOpenAiToolFunction(GetDictionary(item, "function"));
                result.Add(toolCall);
            }

            return result;
        }

        private static LocalOpenAiToolFunction? ParseOpenAiToolFunction(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalOpenAiToolFunction function = new LocalOpenAiToolFunction();
            function.Name = GetString(item, "name");
            function.Description = GetString(item, "description");
            function.Parameters = GetDictionary(item, "parameters");
            function.ArgumentsJson = GetArgumentsJson(item);
            return function;
        }

        private static List<LocalAnthropicMessage>? ParseAnthropicMessages(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalAnthropicMessage> result = new List<LocalAnthropicMessage>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalAnthropicMessage message = new LocalAnthropicMessage();
                message.Role = GetString(item, "role");

                // Anthropic message content is either a plain string or a list of content blocks;
                // inspect the serialized shape rather than assuming one or the other.
                if (item.ContainsKey("content") && item["content"] != null)
                {
                    string contentJson = _Serializer.SerializeJson(item["content"], false);
                    if (contentJson.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    {
                        List<Dictionary<string, object>>? blocks = _Serializer.DeserializeJson<List<Dictionary<string, object>>>(contentJson);
                        message.Blocks = blocks != null ? ParseAnthropicContentBlocks(blocks) : new List<LocalAnthropicContentBlock>();
                    }
                    else
                    {
                        message.Text = GetString(item, "content");
                    }
                }

                result.Add(message);
            }

            return result;
        }

        private static List<LocalAnthropicContentBlock> ParseAnthropicContentBlocks(List<Dictionary<string, object>> items)
        {
            List<LocalAnthropicContentBlock> result = new List<LocalAnthropicContentBlock>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalAnthropicContentBlock block = new LocalAnthropicContentBlock();
                block.Type = GetString(item, "type");
                block.Text = GetString(item, "text");
                block.Id = GetString(item, "id");
                block.Name = GetString(item, "name");
                block.ToolUseId = GetString(item, "tool_use_id");
                block.Content = GetString(item, "content");

                if (item.ContainsKey("input") && item["input"] != null)
                {
                    block.InputJson = _Serializer.SerializeJson(item["input"], false);
                }

                result.Add(block);
            }

            return result;
        }

        private static List<LocalAnthropicTool>? ParseAnthropicTools(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalAnthropicTool> result = new List<LocalAnthropicTool>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalAnthropicTool tool = new LocalAnthropicTool();
                tool.Name = GetString(item, "name");
                tool.Description = GetString(item, "description");
                tool.InputSchema = GetDictionary(item, "input_schema");
                result.Add(tool);
            }

            return result;
        }

        private static LocalAnthropicToolChoice? ParseAnthropicToolChoice(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalAnthropicToolChoice toolChoice = new LocalAnthropicToolChoice();
            toolChoice.Type = GetString(item, "type");
            toolChoice.Name = GetString(item, "name");
            return toolChoice;
        }

        private static LocalAnthropicThinking? ParseAnthropicThinking(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalAnthropicThinking thinking = new LocalAnthropicThinking();
            thinking.Type = GetString(item, "type");
            thinking.Display = GetString(item, "display");
            return thinking;
        }

        private static LocalAnthropicOutputConfig? ParseAnthropicOutputConfig(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalAnthropicOutputConfig outputConfig = new LocalAnthropicOutputConfig();
            outputConfig.Effort = GetString(item, "effort");
            return outputConfig;
        }

        private static List<LocalGeminiContent>? ParseGeminiContents(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalGeminiContent> result = new List<LocalGeminiContent>();
            foreach (Dictionary<string, object> item in items)
            {
                result.Add(ParseGeminiContent(item));
            }

            return result;
        }

        private static LocalGeminiContent ParseGeminiContent(Dictionary<string, object> item)
        {
            LocalGeminiContent content = new LocalGeminiContent();
            content.Role = GetString(item, "role");
            content.Parts = ParseGeminiParts(GetList(item, "parts"));
            return content;
        }

        private static List<LocalGeminiPart>? ParseGeminiParts(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalGeminiPart> result = new List<LocalGeminiPart>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalGeminiPart part = new LocalGeminiPart();
                part.Text = GetString(item, "text");
                part.FunctionCall = ParseGeminiFunctionCall(GetDictionary(item, "functionCall"));
                part.FunctionResponse = ParseGeminiFunctionResponse(GetDictionary(item, "functionResponse"));
                result.Add(part);
            }

            return result;
        }

        private static LocalGeminiFunctionCall? ParseGeminiFunctionCall(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalGeminiFunctionCall functionCall = new LocalGeminiFunctionCall();
            functionCall.Name = GetString(item, "name");
            functionCall.Args = GetDictionary(item, "args");
            return functionCall;
        }

        private static LocalGeminiFunctionResponse? ParseGeminiFunctionResponse(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalGeminiFunctionResponse functionResponse = new LocalGeminiFunctionResponse();
            functionResponse.Name = GetString(item, "name");
            functionResponse.Response = GetDictionary(item, "response");
            return functionResponse;
        }

        private static List<LocalGeminiTool>? ParseGeminiTools(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalGeminiTool> result = new List<LocalGeminiTool>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalGeminiTool tool = new LocalGeminiTool();
                tool.FunctionDeclarations = ParseGeminiFunctionDeclarations(GetList(item, "functionDeclarations"));
                result.Add(tool);
            }

            return result;
        }

        private static LocalGeminiGenerationConfig? ParseGeminiGenerationConfig(Dictionary<string, object>? item)
        {
            if (item == null) return null;

            LocalGeminiGenerationConfig config = new LocalGeminiGenerationConfig();
            config.MaxOutputTokens = GetInt(item, "maxOutputTokens");
            config.Temperature = GetDouble(item, "temperature");
            config.TopP = GetDouble(item, "topP");

            Dictionary<string, object>? thinkingConfig = GetDictionary(item, "thinkingConfig");
            if (thinkingConfig != null)
            {
                config.ThinkingBudget = GetInt(thinkingConfig, "thinkingBudget");
            }

            return config;
        }

        private static List<LocalGeminiFunctionDeclaration>? ParseGeminiFunctionDeclarations(List<Dictionary<string, object>>? items)
        {
            if (items == null) return null;

            List<LocalGeminiFunctionDeclaration> result = new List<LocalGeminiFunctionDeclaration>();
            foreach (Dictionary<string, object> item in items)
            {
                LocalGeminiFunctionDeclaration declaration = new LocalGeminiFunctionDeclaration();
                declaration.Name = GetString(item, "name");
                declaration.Description = GetString(item, "description");
                declaration.Parameters = GetDictionary(item, "parameters");
                result.Add(declaration);
            }

            return result;
        }

        private static string? GetString(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;
            return data[key].ToString();
        }

        private static bool? GetBool(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            object value = data[key];
            if (value is bool boolValue) return boolValue;

            string? stringValue = value.ToString();
            if (bool.TryParse(stringValue, out bool parsed)) return parsed;
            return null;
        }

        private static int? GetInt(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            string? stringValue = data[key].ToString();
            if (int.TryParse(stringValue, out int parsed)) return parsed;
            return null;
        }

        private static double? GetDouble(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            string? stringValue = data[key].ToString();
            if (double.TryParse(stringValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return null;
        }

        private static Dictionary<string, object>? GetDictionary(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            string json = _Serializer.SerializeJson(data[key], false);
            return _Serializer.DeserializeJson<Dictionary<string, object>>(json);
        }

        private static List<Dictionary<string, object>>? GetList(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            string json = _Serializer.SerializeJson(data[key], false);
            return _Serializer.DeserializeJson<List<Dictionary<string, object>>>(json);
        }

        private static List<string>? GetStringList(Dictionary<string, object> data, string key)
        {
            if (!data.ContainsKey(key) || data[key] == null) return null;

            string json = _Serializer.SerializeJson(data[key], false);
            List<string>? values = _Serializer.DeserializeJson<List<string>>(json);
            if (values != null) return values;

            string? singleValue = data[key].ToString();
            if (singleValue == null) return null;

            return new List<string> { singleValue };
        }

        private static string? GetArgumentsJson(Dictionary<string, object> data)
        {
            if (!data.ContainsKey("arguments") || data["arguments"] == null) return null;

            object arguments = data["arguments"];
            if (arguments is string stringArguments) return stringArguments;
            return _Serializer.SerializeJson(arguments, false);
        }
    }
}
