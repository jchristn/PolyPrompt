namespace Test.Xunit
{
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.XunitAdapter;

    public sealed class PolyPromptXunitTests
    {
        public static TouchstoneTheoryData TestCases
        {
            get { return new TouchstoneTheoryData(PolyPromptSuites.Executable); }
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public async Task Run(TestCaseDescriptor testCase)
        {
            await testCase.ExecuteAsync(CancellationToken.None);
        }
    }
}
