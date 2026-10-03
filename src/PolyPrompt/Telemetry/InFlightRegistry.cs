namespace PolyPrompt.Telemetry
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Process-wide labeled counts read by an observable up-down counter. Counting with an observable instrument (rather
    /// than a synchronous up-down counter) keeps the reported value correct when a listener attaches while work is
    /// already in flight. Thread safety: all members are safe for concurrent use.
    /// </summary>
    internal sealed class InFlightRegistry
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, InFlightEntry> _Entries = new ConcurrentDictionary<string, InFlightEntry>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get (or create) the entry for a pair of labels.
        /// </summary>
        /// <param name="key1">First label key.</param>
        /// <param name="value1">First label value.</param>
        /// <param name="key2">Second label key, or null for a single label.</param>
        /// <param name="value2">Second label value.</param>
        /// <returns>The entry.</returns>
        public InFlightEntry Get(string key1, string value1, string? key2 = null, string? value2 = null)
        {
            string key = key2 == null ? value1 : value1 + "\u0001" + value2;
            return _Entries.GetOrAdd(key, _ =>
            {
                KeyValuePair<string, object?>[] tags = key2 == null
                    ? new[] { new KeyValuePair<string, object?>(key1, value1) }
                    : new[] { new KeyValuePair<string, object?>(key1, value1), new KeyValuePair<string, object?>(key2, value2) };
                return new InFlightEntry(tags);
            });
        }

        /// <summary>
        /// Report every entry as a measurement.
        /// </summary>
        /// <returns>One measurement per label set.</returns>
        public IEnumerable<Measurement<long>> Observe()
        {
            List<Measurement<long>> measurements = new List<Measurement<long>>(_Entries.Count);
            foreach (InFlightEntry entry in _Entries.Values)
            {
                measurements.Add(new Measurement<long>(entry.Value, entry.Tags));
            }
            return measurements;
        }

        #endregion
    }
}
