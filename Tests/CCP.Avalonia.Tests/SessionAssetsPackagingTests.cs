using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SessionAssetsPackagingTests
{
    private static readonly string[] CanonicalFiles =
    {
        "distant_doll.session.json",
        "gamer_girl.session.json",
        "good_girls_dont_cum.session.json",
        "morning_drift.session.json"
    };

    [Fact]
    public void CanonicalSessionsAreCopiedAndLoadableFromTestOutput()
    {
        var outputFolder = Path.Combine(AppContext.BaseDirectory, "assets", "sessions");
        var customFolder = Path.Combine(Path.GetTempPath(), "ccp-session-assets-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            Assert.True(Directory.Exists(outputFolder), "session output folder was not copied");
            var copiedFiles = Directory.GetFiles(outputFolder, "*.session.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(CanonicalFiles.OrderBy(name => name, StringComparer.Ordinal), copiedFiles);

            var service = new SessionFileService(customFolder, outputFolder);
            var loaded = service.LoadBuiltInSessions();

            Assert.Equal(CanonicalFiles.Length, loaded.Count);
            Assert.All(loaded, session => Assert.Equal(SessionSource.BuiltIn, session.Source));
            Assert.All(CanonicalFiles, file =>
                Assert.Contains(loaded, session =>
                    string.Equals(Path.GetFileName(session.SourceFilePath), file,
                        StringComparison.Ordinal)));
            Assert.False(Directory.Exists(customFolder));
        }
        finally
        {
            if (Directory.Exists(customFolder))
                Directory.Delete(customFolder, recursive: true);
        }
    }
}
