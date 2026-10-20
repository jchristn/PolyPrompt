namespace Test.Shared
{
    using Touchstone.Core;
    public static partial class LocalToolTurnCases
    {
        public static IEnumerable<TestCaseDescriptor> Create(string suite)
        {
            foreach (string provider in Providers.Concat(new[] { "ollamacloud", "azure", "vertex" }))
                foreach (bool stream in new[] { false, true })
                {
                    string p = provider; bool s = stream;
                    yield return new(suite, "turn_state_" + p + (s ? "_stream" : ""), "Native continuation " + p + " stream=" + s,
                        t => RunAsync(p, s, t), new[] { "local", "continuation" });
                }
            yield return new(suite, "turn_state_protocol_isolation", "Native state stays in its own protocol", IsolationAsync, new[] { "local", "continuation" });
        }
    }
}
