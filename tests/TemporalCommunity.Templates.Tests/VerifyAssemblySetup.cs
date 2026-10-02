using System.Runtime.CompilerServices;

namespace TemporalCommunity.Templates.Tests;

internal static class VerifyAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize() => DiffEngine.DiffRunner.Disabled = true;
}

public sealed class VerifyAssemblySetupTests
{
    [Xunit.Fact]
    public void DiffEngineIsDisabledForTheTestAssembly() =>
        Xunit.Assert.True(DiffEngine.DiffRunner.Disabled);
}
