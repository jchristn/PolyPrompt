namespace Test.Nunit
{
    using System.Collections;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    public sealed class PolyPromptNunitTests
    {
        private static IEnumerable TestCases()
        {
            return new TouchstoneTestCaseSource(PolyPromptSuites.Executable);
        }

        [TestCaseSource(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
