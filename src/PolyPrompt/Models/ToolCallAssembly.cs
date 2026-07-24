namespace PolyPrompt.Models
{
    using System.Text;

    internal sealed class ToolCallAssembly
    {
        private readonly StringBuilder _ArgumentsBuilder = new StringBuilder();
        private string? _FullArgumentsJson = null;

        internal int Index { get; }
        internal string? Id { get; private set; }
        internal string? Type { get; private set; }
        internal string? Name { get; private set; }

        internal ToolCallAssembly(int index)
        {
            Index = index;
        }

        internal void Apply(ToolCallDelta delta)
        {
            if (!string.IsNullOrEmpty(delta.Id)) Id = delta.Id;
            if (!string.IsNullOrEmpty(delta.Type)) Type = delta.Type;
            if (!string.IsNullOrEmpty(delta.Name)) Name = delta.Name;

            if (delta.ArgumentsJson != null)
            {
                _FullArgumentsJson = delta.ArgumentsJson;
            }

            if (delta.ArgumentsJsonDelta != null)
            {
                _FullArgumentsJson = null;
                _ArgumentsBuilder.Append(delta.ArgumentsJsonDelta);
            }
        }

        internal ToolCall ToToolCall()
        {
            string argumentsJson = _FullArgumentsJson ?? _ArgumentsBuilder.ToString();
            if (string.IsNullOrWhiteSpace(argumentsJson)) argumentsJson = "{}";

            return new ToolCall
            {
                Id = Id,
                Name = Name ?? string.Empty,
                ArgumentsJson = argumentsJson
            };
        }
    }
}
