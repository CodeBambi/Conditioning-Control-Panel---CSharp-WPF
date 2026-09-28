using System;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AssetImportAndSessionLogCoreTests
{
    [Fact]
    public async System.Threading.Tasks.Task ImportAsync_CreatesCustomAssetsDirectoriesBeforeFallbackResolves()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-asset-import-" + Guid.NewGuid().ToString("N"));
        var custom = Path.Combine(root, "custom");
        var fallback = Path.Combine(root, "default");
        var source = Path.Combine(root, "pic.png");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
        var oldPath = CoreSettings.Current.CustomAssetsPath;
        var oldProvider = CorePaths.EffectiveAssetsProvider;
        try
        {
            // Same shape as the WPF head's EffectiveAssetsPath: custom only when its folder exists (#391).
            CoreSettings.Current.CustomAssetsPath = custom;
            CorePaths.EffectiveAssetsProvider = () => Directory.Exists(custom) ? custom : fallback;

            var result = await new AssetImportService().ImportAsync(new[] { source });

            Assert.Equal(1, result.ImagesImported);
            foreach (var sub in new[] { "images", "videos", "audio", "wallpapers" })
                Assert.True(Directory.Exists(Path.Combine(custom, sub)), sub);
            Assert.True(File.Exists(Path.Combine(custom, "images", "pic.png")));
            Assert.False(Directory.Exists(fallback));
        }
        finally
        {
            CoreSettings.Current.CustomAssetsPath = oldPath;
            CorePaths.EffectiveAssetsProvider = oldProvider;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SessionLog_RoundTripsPreMoveFixtureIdentically()
    {
        var fixture = File.ReadAllText(FixturePath()).Replace("\r\n", "\n");
        var log = JsonConvert.DeserializeObject<SessionLog>(fixture)!;

        Assert.Equal(SessionDifficulty.Hard, log.SessionDifficulty);
        Assert.Equal(TimeSpan.FromMinutes(30), log.Duration);
        Assert.Equal(MediaType.Video, log.Media[1].Type);
        Assert.Equal(fixture, JsonConvert.SerializeObject(log, Formatting.Indented).Replace("\r\n", "\n"));
    }

    [Fact]
    public void RecordMedia_AppendsOnlyWhileSessionActive()
    {
        using var service = new SessionLogService();
        // CorePaths.UserData points at a temp dir via RoadmapTestProfile's [ModuleInitializer];
        // refuse to run (and prune) against a real session_logs folder if that ever moves.
        Assert.StartsWith(Path.GetTempPath(), service.LogsFolder);
        var id = "core-test-" + Guid.NewGuid().ToString("N");
        SessionLog? ready = null;
        service.LogReady += (_, e) => ready = e.Log;

        service.RecordVideo("/before/start.mp4");
        service.BeginSession(new Session { Id = id, Name = "Test" });
        service.RecordImages(new[] { "/a/one.png", "", "/a/two.jpg" });
        service.RecordVideo("/a/clip.mp4");
        service.RecordVideo(null);
        service.EndSession(true, TimeSpan.FromSeconds(5), 10);
        service.RecordImages(new[] { "/after/end.png" });

        try
        {
            Assert.NotNull(ready);
            Assert.Collection(ready!.Media,
                m => { Assert.Equal(MediaType.Image, m.Type); Assert.Equal("one.png", m.DisplayName); },
                m => { Assert.Equal(MediaType.Image, m.Type); Assert.Equal("/a/two.jpg", m.FilePath); },
                m => { Assert.Equal(MediaType.Video, m.Type); Assert.Equal("clip.mp4", m.DisplayName); });
        }
        finally
        {
            foreach (var f in Directory.GetFiles(service.LogsFolder, "*_" + id + ".json")) File.Delete(f);
        }
    }

    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "session_log_premove.json");
}
