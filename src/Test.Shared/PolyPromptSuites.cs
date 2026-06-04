namespace Test.Shared
{
    using Touchstone.Core;

    /// <summary>
    /// Shared Touchstone suites for PolyPrompt.
    /// </summary>
    public static class PolyPromptSuites
    {
        public static IReadOnlyList<TestSuiteDescriptor> LocalOnly
        {
            get { return new List<TestSuiteDescriptor> { LocalBehaviorSuite.Create() }; }
        }

        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get { return FromConfiguration(ProviderTestConfiguration.FromEnvironment(), true, true); }
        }

        public static IReadOnlyList<TestSuiteDescriptor> Executable
        {
            get { return FilterSkipped(All); }
        }

        public static IReadOnlyList<TestSuiteDescriptor> FromConfiguration(
            ProviderTestConfiguration? configuration,
            bool includeLocal,
            bool includeSkippedProviderPlaceholder)
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();

            if (includeLocal)
                suites.Add(LocalBehaviorSuite.Create());

            if (configuration != null)
                suites.Add(ProviderLiveSuite.Create(configuration));
            else if (includeSkippedProviderPlaceholder)
                suites.Add(ProviderLiveSuite.CreateSkipped());

            return suites;
        }

        public static IReadOnlyList<TestSuiteDescriptor> FilterSkipped(IReadOnlyList<TestSuiteDescriptor> suites)
        {
            List<TestSuiteDescriptor> filtered = new List<TestSuiteDescriptor>();

            foreach (TestSuiteDescriptor suite in suites)
            {
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                foreach (TestCaseDescriptor testCase in suite.Cases)
                {
                    if (!testCase.Skip) cases.Add(testCase);
                }

                if (cases.Count > 0)
                    filtered.Add(new TestSuiteDescriptor(suite.SuiteId, suite.DisplayName, cases));
            }

            return filtered;
        }
    }
}
