namespace PolyPrompt.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provider-neutral reasoning effort for reasoning-capable models. A <see cref="Level"/> supplies
    /// defaults; the per-provider properties override them individually; the projection methods return the
    /// value each provider expects. Null on a request means "do not send" — the provider default is used.
    /// </summary>
    public class ReasoningEffort
    {
        #region Presets

        /// <summary>A <see cref="ReasoningEffortLevel.Minimal"/> effort with default provider values.</summary>
        public static ReasoningEffort Minimal => new ReasoningEffort(ReasoningEffortLevel.Minimal);

        /// <summary>A <see cref="ReasoningEffortLevel.Low"/> effort with default provider values.</summary>
        public static ReasoningEffort Low => new ReasoningEffort(ReasoningEffortLevel.Low);

        /// <summary>A <see cref="ReasoningEffortLevel.Medium"/> effort with default provider values.</summary>
        public static ReasoningEffort Medium => new ReasoningEffort(ReasoningEffortLevel.Medium);

        /// <summary>A <see cref="ReasoningEffortLevel.High"/> effort with default provider values.</summary>
        public static ReasoningEffort High => new ReasoningEffort(ReasoningEffortLevel.High);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Create a reasoning effort defaulting to <see cref="ReasoningEffortLevel.Medium"/>.</summary>
        public ReasoningEffort()
        {
        }

        /// <summary>Create a reasoning effort at the given level.</summary>
        /// <param name="level">The semantic effort level.</param>
        public ReasoningEffort(ReasoningEffortLevel level)
        {
            _Level = level;
        }

        /// <summary>Implicitly build a default <see cref="ReasoningEffort"/> from a level.</summary>
        /// <param name="level">The semantic effort level.</param>
        public static implicit operator ReasoningEffort(ReasoningEffortLevel level)
        {
            return new ReasoningEffort(level);
        }

        #endregion

        #region Private-Members

        private ReasoningEffortLevel _Level = ReasoningEffortLevel.Medium;
        private string? _OpenAiValue = null;
        private int? _GeminiThinkingBudget = null;
        private string? _OllamaThink = null;
        private string? _AnthropicEffort = null;

        // Accepted override tokens. A value outside its set is rejected (reverts to null) so the projection
        // falls back to the Level-derived default — the same "silently clamp to a valid value" idiom the
        // existing Temperature/TopP setters use, rather than throwing.
        private static readonly HashSet<string> _OpenAiValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "minimal", "low", "medium", "high" };

        private static readonly HashSet<string> _OllamaValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high", "true", "false" };

        private static readonly HashSet<string> _AnthropicValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "low", "medium", "high", "xhigh", "max" };

        private const int GeminiThinkingBudgetFloor = -1;      // -1 = dynamic budget, 0 = off
        private const int GeminiThinkingBudgetCeiling = 32768; // generous upper bound across 2.5 models

        #endregion

        #region Public-Members

        /// <summary>The semantic effort level. Drives every default that is not explicitly overridden.</summary>
        public ReasoningEffortLevel Level
        {
            get { return _Level; }
            set { _Level = value; }
        }

        /// <summary>
        /// OpenAI reasoning_effort override. Null derives from <see cref="Level"/>. Set values are
        /// normalized (trimmed, lower-cased) and clamped to the accepted set
        /// ("minimal"/"low"/"medium"/"high"); an unrecognized value reverts to null.
        /// </summary>
        public string? OpenAiValue
        {
            get { return _OpenAiValue; }
            set { _OpenAiValue = NormalizeToken(value, _OpenAiValues); }
        }

        /// <summary>
        /// Gemini thinking-token budget override (thinkingConfig.thinkingBudget). Null derives from
        /// <see cref="Level"/>. -1 selects the model's dynamic budget, 0 disables thinking, positive is an
        /// explicit token budget. Clamped to -1..32768.
        /// </summary>
        public int? GeminiThinkingBudget
        {
            get { return _GeminiThinkingBudget; }
            set
            {
                _GeminiThinkingBudget = value.HasValue
                    ? Math.Clamp(value.Value, GeminiThinkingBudgetFloor, GeminiThinkingBudgetCeiling)
                    : null;
            }
        }

        /// <summary>
        /// Ollama think override. Null derives from <see cref="Level"/>. Set values are normalized
        /// (trimmed, lower-cased) and clamped to the accepted set ("low"/"medium"/"high"/"true"/"false");
        /// an unrecognized value reverts to null. "true"/"false" are emitted as JSON booleans.
        /// </summary>
        public string? OllamaThink
        {
            get { return _OllamaThink; }
            set { _OllamaThink = NormalizeToken(value, _OllamaValues); }
        }

        /// <summary>
        /// Anthropic output_config.effort override. Null derives from <see cref="Level"/>. Set values are
        /// normalized (trimmed, lower-cased) and clamped to the accepted set
        /// ("low"/"medium"/"high"/"xhigh"/"max"); an unrecognized value reverts to null. "xhigh" and "max"
        /// have no level preset and are reachable only through this override.
        /// </summary>
        public string? AnthropicEffort
        {
            get { return _AnthropicEffort; }
            set { _AnthropicEffort = NormalizeToken(value, _AnthropicValues); }
        }

        #endregion

        #region Public-Methods

        /// <summary>Returns the OpenAI reasoning_effort wire value (override if set, else derived from Level).</summary>
        /// <returns>One of "minimal"/"low"/"medium"/"high", or the configured override.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public string ToOpenAiWireValue()
        {
            if (_OpenAiValue != null) return _OpenAiValue;

            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return "minimal";
                case ReasoningEffortLevel.Low:     return "low";
                case ReasoningEffortLevel.Medium:  return "medium";
                case ReasoningEffortLevel.High:    return "high";
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>Returns the Gemini thinkingConfig.thinkingBudget (override if set, else derived from Level).</summary>
        /// <returns>-1 (dynamic), 0 (off), or a positive token budget.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public int ToGeminiThinkingBudget()
        {
            if (_GeminiThinkingBudget.HasValue) return _GeminiThinkingBudget.Value;

            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return 0;      // thinking off
                case ReasoningEffortLevel.Low:     return 1024;
                case ReasoningEffortLevel.Medium:  return 8192;
                case ReasoningEffortLevel.High:    return -1;     // dynamic budget
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>Returns the Ollama think value as a bool or string (override if set, else derived from Level).</summary>
        /// <returns>A boolean (true/false) or a level string ("low"/"medium"/"high").</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public object ToOllamaThink()
        {
            if (_OllamaThink != null)
            {
                // Already normalized by the setter to one of low/medium/high/true/false.
                if (string.Equals(_OllamaThink, "true", StringComparison.Ordinal)) return true;
                if (string.Equals(_OllamaThink, "false", StringComparison.Ordinal)) return false;
                return _OllamaThink;
            }

            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return false;  // disable thinking
                case ReasoningEffortLevel.Low:     return "low";
                case ReasoningEffortLevel.Medium:  return "medium";
                case ReasoningEffortLevel.High:    return "high";
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>Returns the Anthropic output_config.effort wire value (override if set, else derived from Level).</summary>
        /// <returns>One of "low"/"medium"/"high", or the configured override ("xhigh"/"max" included).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public string ToAnthropicEffort()
        {
            if (_AnthropicEffort != null) return _AnthropicEffort;

            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return "low";
                case ReasoningEffortLevel.Low:     return "low";
                case ReasoningEffortLevel.Medium:  return "medium";
                case ReasoningEffortLevel.High:    return "high";
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        /// <summary>
        /// Whether the Anthropic projection sends an adaptive thinking field alongside the effort.
        /// <see cref="ReasoningEffortLevel.Minimal"/> omits the thinking field entirely (an explicit disable
        /// is rejected by some current Claude models while omission is accepted everywhere); every other
        /// level sends adaptive thinking with a summarized display so reasoning capture returns text.
        /// </summary>
        /// <returns>True when the projection includes a thinking field, false for Minimal.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined <see cref="Level"/>.</exception>
        public bool SendsAnthropicThinking()
        {
            switch (_Level)
            {
                case ReasoningEffortLevel.Minimal: return false;
                case ReasoningEffortLevel.Low:
                case ReasoningEffortLevel.Medium:
                case ReasoningEffortLevel.High:    return true;
                default: throw new ArgumentOutOfRangeException(nameof(Level), _Level, "Unknown reasoning effort level.");
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>Trim + lower-case a candidate override and clamp it to the accepted set, else null.</summary>
        private static string? NormalizeToken(string? value, HashSet<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().ToLowerInvariant();
            return allowed.Contains(normalized) ? normalized : null;
        }

        #endregion
    }
}
