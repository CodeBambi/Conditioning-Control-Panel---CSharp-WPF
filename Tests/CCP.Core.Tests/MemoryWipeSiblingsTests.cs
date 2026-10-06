using System.IO;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Forget everything must not leave the wiped text in a temp or backup sibling
/// (memory.json.tmp from an interrupted atomic save, session.json.bak, ...).</summary>
public sealed class MemoryWipeSiblingsTests
{
    [Fact]
    public void WipeDeletesTempAndBackupSiblings()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-wipe-").FullName;
        try
        {
            var path = Path.Combine(dir, "memory.json");
            using var store = new MemoryStore(path);
            store.AddFact("secret beans", MemoryFactKind.Joke);
            store.SaveNow();
            foreach (var name in new[] { "memory.json.tmp", "memory.json.bak", "session.json.tmp", "episodes.json.bak" })
                File.WriteAllText(Path.Combine(dir, name), "secret beans");
            File.WriteAllText(Path.Combine(dir, "unrelated.txt"), "keep");

            store.Wipe();

            foreach (var name in new[] { "memory.json.tmp", "memory.json.bak", "session.json.tmp", "episodes.json.bak" })
                Assert.False(File.Exists(Path.Combine(dir, name)), name);
            Assert.True(File.Exists(Path.Combine(dir, "unrelated.txt")));
            if (File.Exists(path)) Assert.DoesNotContain("secret beans", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
