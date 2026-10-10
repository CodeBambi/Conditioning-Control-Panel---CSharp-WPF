using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane c1 (W9): the Takeover wallpaper rules, WPF Services/Video/WallpaperService.cs. The original
/// is remembered in settings BEFORE the desktop changes, restored on deactivate, and put back on the next
/// launch after a crash (#692); a desktop that cannot say what it shows is never touched.</summary>
[Collection(SessionStatics.Name)]
public sealed class WallpaperServiceTests
{
    private sealed class Desk : IWallpaperBackend
    {
        public string? Current;
        public bool Refuse;
        public readonly List<string> Sets = new();
        public Func<string>? OnSetProbe;
        public string? ProbeSeen;
        public string? Read() => Current;
        public bool Set(string path)
        {
            ProbeSeen = OnSetProbe?.Invoke();
            if (Refuse) return false;
            Sets.Add(path); Current = path; return true;
        }
    }

    private static void With(Action<string, string> body)
    {
        var s = CoreSettings.Current;
        var (folder, original) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-c1-wall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mine = Path.Combine(dir, "mine-original.png");
        File.WriteAllBytes(mine, new byte[] { 1 });
        var pool = Path.Combine(dir, "pool");
        Directory.CreateDirectory(pool);
        try
        {
            s.WallpaperSourceFolder = pool;
            s.WallpaperOriginalPath = "";
            body(mine, pool);
        }
        finally
        {
            s.WallpaperSourceFolder = folder;
            s.WallpaperOriginalPath = original;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Activate_remembers_the_original_first_and_Deactivate_puts_it_back() => With((mine, pool) =>
    {
        File.WriteAllBytes(Path.Combine(pool, "a.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(pool, "notes.txt"), new byte[] { 1 });
        var desk = new Desk { Current = mine };
        desk.OnSetProbe = () => CoreSettings.Current.WallpaperOriginalPath;
        var w = new WallpaperService(desk, new Random(1));

        Assert.True(w.Activate());
        Assert.Equal(mine, desk.ProbeSeen);                       // the breadcrumb was down before the desktop changed
        Assert.Equal(Path.Combine(pool, "a.jpg"), desk.Current);  // never the .txt
        Assert.True(w.IsActive);
        Assert.Equal("a.jpg", w.CurrentFilename);

        w.Deactivate();
        Assert.Equal(mine, desk.Current);
        Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);
        Assert.False(w.IsActive);
        w.Deactivate();                                           // a second restore is nothing
        Assert.Equal(2, desk.Sets.Count);
    });

    [Fact]
    public void An_unreadable_desktop_or_an_empty_folder_changes_nothing() => With((mine, pool) =>
    {
        var blind = new Desk { Current = null };
        File.WriteAllBytes(Path.Combine(pool, "a.png"), new byte[] { 1 });
        Assert.False(new WallpaperService(blind).Activate());     // solid colour, slideshow, unknown desktop
        Assert.Empty(blind.Sets);

        File.Delete(Path.Combine(pool, "a.png"));
        var desk = new Desk { Current = mine };
        Assert.False(new WallpaperService(desk).Shuffle());       // nothing to show
        Assert.Empty(desk.Sets);
        Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);
    });

    [Fact]
    public void A_refused_set_drops_the_breadcrumb_and_a_refused_restore_keeps_it() => With((mine, pool) =>
    {
        File.WriteAllBytes(Path.Combine(pool, "a.png"), new byte[] { 1 });
        var desk = new Desk { Current = mine, Refuse = true };
        var w = new WallpaperService(desk);
        Assert.False(w.Activate());
        Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);

        desk.Refuse = false;
        Assert.True(w.Activate());
        desk.Refuse = true;
        w.Deactivate();
        Assert.Equal(mine, CoreSettings.Current.WallpaperOriginalPath);   // the next launch gets another go
    });

    [Fact]
    public void A_session_that_died_is_restored_by_the_next_launch() => With((mine, pool) =>
    {
        File.WriteAllBytes(Path.Combine(pool, "a.png"), new byte[] { 1 });
        var desk = new Desk { Current = mine };
        Assert.True(new WallpaperService(desk).Activate());       // ...and the process is killed here
        Assert.Equal(mine, CoreSettings.Current.WallpaperOriginalPath);
        Assert.NotEqual(mine, desk.Current);

        var next = new WallpaperService(desk);                    // next launch
        Assert.Equal(mine, desk.Current);
        Assert.Equal("", CoreSettings.Current.WallpaperOriginalPath);
        Assert.False(next.IsActive);
    });

    [Fact]
    public void Shuffle_moves_to_a_different_picture_and_still_restores_the_first_original() => With((mine, pool) =>
    {
        File.WriteAllBytes(Path.Combine(pool, "a.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(pool, "b.png"), new byte[] { 1 });
        var desk = new Desk { Current = mine };
        var w = new WallpaperService(desk, new Random(3));
        Assert.True(w.Shuffle());                                 // not active: activates
        var first = desk.Current;
        Assert.True(w.Shuffle());
        Assert.NotEqual(first, desk.Current);
        Assert.Equal(mine, CoreSettings.Current.WallpaperOriginalPath);   // never our own picture
        w.Dispose();
        Assert.Equal(mine, desk.Current);
    });
}
