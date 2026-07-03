using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;

namespace NxCheck.Tests;

/// <summary>테스트용 CheckContext 빌더.</summary>
internal static class TestContext
{
    public static CheckContext Build(
        ICommandRunner runner,
        ExpectedSpec? expected = null,
        RunMode mode = RunMode.Once,
        CheckDepth depth = CheckDepth.Static,
        TimeSpan? sampleWindow = null) =>
        new()
        {
            Mode = mode,
            Depth = depth,
            Expected = expected ?? ExpectedSpec.Empty,
            Runner = runner,
            SampleWindow = sampleWindow ?? TimeSpan.Zero, // 테스트는 대기 없이
        };
}
