namespace Test.Shared
{
    /// <summary>
    /// Minimal assertion helpers that keep Test.Shared runner-agnostic.
    /// </summary>
    public static class SharedAssert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new TestFailureException(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new TestFailureException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailureException(message + " Expected '" + expected + "', got '" + actual + "'.");
        }

        public static void NotNull(object? value, string message)
        {
            if (value == null) throw new TestFailureException(message);
        }

        public static void NotEmpty(string? value, string message)
        {
            if (string.IsNullOrEmpty(value)) throw new TestFailureException(message);
        }

        public static async Task ThrowsAsync<TException>(Func<Task> action, string message)
            where TException : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                return;
            }

            throw new TestFailureException(message);
        }
    }
}
