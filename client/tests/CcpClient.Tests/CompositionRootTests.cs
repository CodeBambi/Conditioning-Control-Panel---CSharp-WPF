using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

// Same co-location as CompositionRootValidationTests — this class
// builds a real CompositionRoot, and that read of the process-wide CCP_DATA_ROOT must be
// serialized against the suite's only mutator of the variable.
[Collection(nameof(ProcessEnvCollection))]
public class CompositionRootTests
{
    [Fact]
    public void BuildAvaloniaApp_ReturnsConfiguredBuilder()
    {
        var host = new CompositionRoot().Build(new StartupTrace());

        var builder = Program.BuildAvaloniaApp(host);

        Assert.NotNull(builder);
    }
}
