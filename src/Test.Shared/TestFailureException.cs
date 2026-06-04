namespace Test.Shared
{
    /// <summary>
    /// Exception thrown by shared Touchstone tests when an assertion fails.
    /// </summary>
    public sealed class TestFailureException : Exception
    {
        /// <summary>
        /// Initialize a new test failure exception.
        /// </summary>
        /// <param name="message">Failure message.</param>
        public TestFailureException(string message) : base(message)
        {
        }
    }
}
