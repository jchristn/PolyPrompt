namespace PolyPrompt.Clients
{
    using System.Runtime.CompilerServices;
    using PolyPrompt.Models;

    internal static class StreamingLifecycle
    {
        internal static async IAsyncEnumerable<T> Wrap<T>(IAsyncEnumerable<T> source,
            StreamingResponseBase response, Func<T, bool> terminal, CancellationToken requestToken,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            if (response.State == StreamingState.Failed) yield break;
            if (response.State != StreamingState.Open)
                throw new InvalidOperationException("Streaming responses can only be enumerated once.");
            response.State = StreamingState.Streaming;
            bool ended = false;
            bool sawTerminal = false;
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, token);
            IAsyncEnumerator<T>? enumerator = null;
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        // Start the source even with a cancelled token so its ownership finally block runs.
                        if (enumerator != null) linked.Token.ThrowIfCancellationRequested();
                        enumerator ??= source.GetAsyncEnumerator(linked.Token);
                        more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                        response.State = StreamingState.Cancelled;
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        Fail(response, "Streaming request timed out.");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Fail(response, "Streaming response failed: " + ex.Message);
                        break;
                    }
                    if (!more)
                    {
                        ended = true;
                        if (!response.Success || response.Error != null)
                            Fail(response, response.Error ?? "Provider reported a streaming failure.");
                        else if (!sawTerminal)
                            Fail(response, "Streaming response ended before a valid terminal event.");
                        else response.State = StreamingState.Completed;
                        break;
                    }
                    T item = enumerator.Current;
                    sawTerminal |= terminal(item);
                    yield return item;
                }
            }
            finally
            {
                try
                {
                    if (enumerator != null) await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (response.State != StreamingState.Cancelled)
                        Fail(response, "Streaming cleanup failed: " + ex.Message);
                }
                finally
                {
                    if (!ended && response.State == StreamingState.Streaming)
                        response.State = StreamingState.Abandoned;
                }
            }
        }

        private static void Fail(StreamingResponseBase response, string message)
        {
            response.State = StreamingState.Failed;
            response.StreamError = message;
            response.Error = message;
        }
    }
}
