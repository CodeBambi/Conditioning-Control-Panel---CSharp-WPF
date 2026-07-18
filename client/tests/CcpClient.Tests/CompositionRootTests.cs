using CcpClient.Desktop;
using Xunit;

namespace CcpClient.Tests;

public class CompositionRootTests
{
    [Fact]
    public void BuildAvaloniaApp_ReturnsConfiguredBuilder()
    {
        var builder = Program.BuildAvaloniaApp();

        Assert.NotNull(builder);
    }
}
