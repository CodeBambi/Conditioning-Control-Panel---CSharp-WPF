using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The app-lifetime media log both heads feed (WPF MediaHistoryService, moved to Core).</summary>
public sealed class MediaHistoryServiceTests
{
    [Fact]
    public void Records_every_kind_newest_first_skips_an_immediate_repeat_and_survives_a_restart()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-mh-").FullName;
        var file = Path.Combine(dir, "media_history.json");
        try
        {
            var added = new List<MediaLogEntry>();
            var h = new MediaHistoryService(file);
            h.EntryAdded += (_, e) => added.Add(e);
            h.RecordImages(new[] { "/lib/a.png", "/lib/b.gif" });
            h.RecordImages(new[] { "/lib/b.gif" });              // re-spawn noise within 2 s: one entry
            h.RecordVideo("/lib/c.mp4");
            h.RecordAudio("/lib/d.mp3");
            h.RecordVideo(null);                                  // nothing played: nothing logged

            var snap = h.GetSnapshot();
            Assert.Equal(new[] { "/lib/d.mp3", "/lib/c.mp4", "/lib/b.gif", "/lib/a.png" }, snap.ConvertAll(e => e.FilePath));
            Assert.Equal(new[] { MediaType.Audio, MediaType.Video, MediaType.Image, MediaType.Image }, snap.ConvertAll(e => e.Type));
            Assert.Equal("d.mp3", snap[0].DisplayName);
            Assert.Equal(4, added.Count);
            h.Dispose();                                          // final flush, as App.OnExit

            var again = new MediaHistoryService(file);
            Assert.Equal(4, again.Count);
            Assert.Equal("/lib/d.mp3", again.GetSnapshot()[0].FilePath);
            var cleared = 0;
            again.Cleared += (_, _) => cleared++;
            again.Clear();
            Assert.Equal(1, cleared);
            Assert.Equal(0, again.Count);
            again.Dispose();
            Assert.Equal(0, new MediaHistoryService(file).Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Keeps_only_the_newest_MaxEntries()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-mh-").FullName;
        try
        {
            var h = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
            for (var i = 0; i < MediaHistoryService.MaxEntries + 5; i++) h.RecordImages(new[] { $"/lib/{i}.png" });
            var snap = h.GetSnapshot();
            Assert.Equal(MediaHistoryService.MaxEntries, snap.Count);
            Assert.Equal($"/lib/{MediaHistoryService.MaxEntries + 4}.png", snap[0].FilePath);
            Assert.Equal("/lib/5.png", snap[^1].FilePath);
            h.Dispose();
        }
        finally { Directory.Delete(dir, true); }
    }
}
