using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>This host is a CCP_USERDATA_DIR sandbox (TestUserDataProfile): a real site is never handed to the shell,
/// loopback and local folders still are. The shell seam records instead of launching anything.</summary>
[Collection("ExternalOpener")]
public sealed class ExternalOpenerTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/policies/prohibited-content", false)]
    [InlineData("https://discord.gg/example", false)]
    [InlineData("http://loopback:3001/", false)]
    [InlineData("http://127.0.0.1:3001/auth", true)]
    [InlineData("http://localhost:3001/", true)]
    public async Task OnlyLoopbackOrLocalReachesTheShell(string target, bool opened)
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try
        {
            Assert.Equal(opened, ExternalOpener.Open(target));
            Assert.Equal(opened, await ExternalOpener.OpenAsync(null, target));
            Assert.Equal(opened ? 2 : 0, launched.Count);
        }
        finally { ExternalOpener.Shell = previous; }
    }

    [Fact]
    public void LocalFoldersStillOpen()
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try { Assert.True(ExternalOpener.Open(Path.GetTempPath())); }
        finally { ExternalOpener.Shell = previous; }
        Assert.Equal(new[] { Path.GetTempPath() }, launched);
    }
}
