namespace Test.Shared
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using PolyPrompt.Telemetry;

    /// <summary>
    /// In-memory listener for PolyPrompt's meter and activity source. Starting a capture opens a root span on a
    /// test-only source; spans are filtered to that root's trace so concurrent tests cannot interfere, and metrics are
    /// filtered by the caller (for example by the local server's port or a unique model name).
    /// </summary>
    public sealed class TelemetryCapture : IDisposable
    {
        #region Private-Members

        private const string TestSourceName = "Test.Shared.Telemetry";
        private static readonly ActivitySource _TestSource = new ActivitySource(TestSourceName);

        private readonly MeterListener _MeterListener = new MeterListener();
        private readonly ActivityListener _ActivityListener;
        private readonly ConcurrentQueue<CapturedMeasurement> _Measurements = new ConcurrentQueue<CapturedMeasurement>();
        private readonly ConcurrentQueue<Activity> _Activities = new ConcurrentQueue<Activity>();
        private readonly ConcurrentQueue<string> _Instruments = new ConcurrentQueue<string>();
        private readonly Activity _Root;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The test's root span. PolyPrompt spans started while it is current belong to its trace.
        /// </summary>
        public Activity Root
        {
            get { return _Root; }
        }

        /// <summary>
        /// Names of every PolyPrompt instrument published to this listener.
        /// </summary>
        public List<string> Instruments
        {
            get { return _Instruments.Distinct().ToList(); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start listening and open the root span, which becomes <see cref="Activity.Current"/> for the caller.
        /// </summary>
        /// <param name="name">Root span name.</param>
        public TelemetryCapture(string name)
        {
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name != PolyPromptTelemetryNames.MeterName) return;
                _Instruments.Enqueue(instrument.Name);
                listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
                _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, value, tags)));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
                _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, value, tags)));
            _MeterListener.SetMeasurementEventCallback<int>((instrument, value, tags, state) =>
                _Measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, value, tags)));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PolyPromptTelemetryNames.ActivitySourceName || source.Name == TestSourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => _Activities.Enqueue(activity),
            };
            ActivitySource.AddActivityListener(_ActivityListener);

            _Root = _TestSource.StartActivity(name, ActivityKind.Internal)
                ?? throw new InvalidOperationException("The test activity source has no listener.");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stopped PolyPrompt spans in this capture's trace.
        /// </summary>
        /// <returns>The spans, in stop order.</returns>
        public List<Activity> Spans()
        {
            return _Activities
                .Where(a => a.Source.Name == PolyPromptTelemetryNames.ActivitySourceName && a.TraceId == _Root.TraceId)
                .ToList();
        }

        /// <summary>
        /// Stopped PolyPrompt spans with the given name in this capture's trace.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <returns>The matching spans.</returns>
        public List<Activity> Spans(string name)
        {
            return Spans().Where(a => a.DisplayName == name).ToList();
        }

        /// <summary>
        /// The single stopped span with the given name, failing the test when there is not exactly one.
        /// </summary>
        /// <param name="name">Span name.</param>
        /// <returns>The span.</returns>
        public Activity Span(string name)
        {
            List<Activity> spans = Spans(name);
            SharedAssert.Equal(1, spans.Count, "Expected exactly one span named '" + name + "'. Spans: " + string.Join(", ", Spans().Select(s => s.DisplayName)) + ".");
            return spans[0];
        }

        /// <summary>
        /// Measurements of an instrument that carry every given label.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="tags">Expected labels as alternating key and value strings.</param>
        /// <returns>The matching measurements.</returns>
        public List<CapturedMeasurement> Measurements(string instrument, params string[] tags)
        {
            return _Measurements.Where(m => m.Instrument == instrument && m.Has(tags)).ToList();
        }

        /// <summary>
        /// Assert that at least one measurement of an instrument carries every given label, and return the first.
        /// </summary>
        /// <param name="instrument">Instrument name.</param>
        /// <param name="tags">Expected labels as alternating key and value strings.</param>
        /// <returns>The first matching measurement.</returns>
        public CapturedMeasurement Require(string instrument, params string[] tags)
        {
            List<CapturedMeasurement> matches = Measurements(instrument, tags);
            if (matches.Count == 0)
            {
                string seen = string.Join("; ", _Measurements.Where(m => m.Instrument == instrument).Take(20).Select(m => m.ToString()));
                throw new TestFailureException("No '" + instrument + "' measurement with " + string.Join(",", tags) + ". Seen: " + seen);
            }
            return matches[0];
        }

        /// <summary>
        /// Collect the current value of every observable instrument.
        /// </summary>
        public void Observe()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Stop listening and close the root span.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Root.Stop();
            _ActivityListener.Dispose();
            _MeterListener.Dispose();
        }

        #endregion
    }
}
