namespace PolyPrompt.Telemetry
{
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// One labeled in-flight count reported by an observable up-down counter.
    /// Thread safety: all members are safe for concurrent use.
    /// </summary>
    internal sealed class InFlightEntry
    {
        #region Private-Members

        private long _Value = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// Labels reported with the count.
        /// </summary>
        public KeyValuePair<string, object?>[] Tags { get; }

        /// <summary>
        /// Current count.
        /// </summary>
        public long Value
        {
            get { return Interlocked.Read(ref _Value); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create an entry with the given labels.
        /// </summary>
        /// <param name="tags">Labels reported with the count.</param>
        public InFlightEntry(KeyValuePair<string, object?>[] tags)
        {
            Tags = tags;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add one to the count.
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _Value);
        }

        /// <summary>
        /// Subtract one from the count.
        /// </summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref _Value);
        }

        #endregion
    }
}
