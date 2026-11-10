namespace Test.Shared
{
    using PolyPrompt.Clients;
    using PolyPrompt.Models;

    /// <summary>
    /// Test helper for cases that inspect the wire through <see cref="ClientBase.CallDetails"/>: records bodies and leaves
    /// credential headers unredacted. Clients default to metadata-only capture with redaction on.
    /// </summary>
    internal static class WireCapture
    {
        public static T Full<T>(T client) where T : ClientBase
        {
            client.CallDetailCapture = CallDetailCapture.Full;
            client.RedactCredentials = false;
            return client;
        }
    }
}
