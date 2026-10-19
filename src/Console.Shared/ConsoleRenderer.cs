namespace PolyPrompt.ConsoleSupport;

/// <summary>Renders model reasoning and answer deltas on separate labeled channels.</summary>
public sealed class ConsoleRenderer(TextWriter output)
{
    private string? _Channel;
    public bool HasReasoning { get; private set; }
    public bool HasAnswer { get; private set; }

    public void Thinking(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        HasReasoning = true;
        Write("Thinking", text);
    }

    public void Answer(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        HasAnswer = true;
        Write("Answer", text);
    }

    public void Finish()
    {
        if (_Channel != null) output.WriteLine();
        _Channel = null;
    }

    private void Write(string channel, string text)
    {
        if (_Channel != channel)
        {
            if (_Channel != null) output.WriteLine();
            output.WriteLine("--- " + channel + " ---");
            _Channel = channel;
        }
        output.Write(text);
    }
}
