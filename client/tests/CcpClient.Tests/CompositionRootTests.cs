using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

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
