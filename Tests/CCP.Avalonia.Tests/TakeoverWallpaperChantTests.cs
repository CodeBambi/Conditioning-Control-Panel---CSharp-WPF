using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane c1 (W9, Bambi Takeover): the wallpaper pulse reverts after WallpaperPulseSeconds unless the
/// user keeps it, a new change replaces the pending revert, stop and panic put the desktop back; the Linux
/// readers; and the Mantra Chant loop (WPF Services/MantraChantService.cs): the pair then the gap, live
/// volume, mute parks it, panic disarms the saved switch, recorded clips only.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TakeoverWallpaperChantTests
{
    private sealed class Desk : IWallpaperBackend
    {
        public string? Current;
        public readonly List<string> Sets = new();
        public string? Read() => Current;
        public bool Set(string path) { Sets.Add(path); Current = path; return true; }
    }

    private sealed class Timer : IDisposable
    {
        public Action Act = () => { };
        public TimeSpan Wait;
        public bool Dead;
        public void Dispose() => Dead = true;
        public void Fire() { if (!Dead) { Dead = true; Act(); } }
    }

    /// <summary>Lane p1: the remote wallpaper verbs. The picture is one of the subject's own folder, the
    /// stop puts the subject's desktop back, and a system that cannot change the wallpaper refuses.</summary>
    [Fact]
    public void RemoteWallpaper_UsesTheSubjectsOwnFolder_AndStopPutsTheDesktopBack()
    {
        var s = CoreSettings.Current;
        var (folder, original, keep) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-p1-remotewall-" + Guid.NewGuid().ToString("N"));
        var pool = Path.Combine(dir, "pool");
        Directory.CreateDirectory(pool);
        var mine = Path.Combine(dir, "mine.png");
        File.WriteAllBytes(mine, new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(pool, "a.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(pool, "b.png"), new byte[] { 1 });
        var desk = new Desk { Current = mine };
        try
        {
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled) = (pool, "", true);   // "keep" never holds a controller's change
            WallpaperHead.ResetForTest();
            WallpaperHead.Backend = () => desk;

            Assert.Null(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(true));
            var first = desk.Current!;
            Assert.Equal(pool, Path.GetDirectoryName(first));
            Assert.Null(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(true));   // again: another of the same folder
            Assert.Equal(pool, Path.GetDirectoryName(desk.Current!));
            Assert.NotEqual(first, desk.Current);

            Assert.Null(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(false));
            Assert.Equal(mine, desk.Current);
            Assert.Null(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(false));  // nothing up: nothing touched
            Assert.Equal(3, desk.Sets.Count);

            // An empty folder: refused, the desktop untouched.
            File.Delete(Path.Combine(pool, "a.png")); File.Delete(Path.Combine(pool, "b.png"));
            Assert.Equal(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.NoWallpapers,
                ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(true));
            Assert.Equal(mine, desk.Current);

            // A desktop this head cannot drive: refused with the reason, never a silent ok.
            WallpaperHead.ResetForTest();
            WallpaperHead.Backend = () => NoDesk();
            if (!WallpaperHead.Supported)
                Assert.Equal(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.NoWallpaperHere,
                    ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.RemoteWallpaper(true));
        }
        finally
        {
            WallpaperHead.ResetForTest();
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled) = (folder, original, keep);
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>WallpaperHead's own "cannot drive this desktop" backend (private there).</summary>
    private static IWallpaperBackend NoDesk() =>
        (IWallpaperBackend)Activator.CreateInstance(typeof(WallpaperHead).GetNestedType("NoBackend", System.Reflection.BindingFlags.NonPublic)!)!;

    [Fact]
    public void WallpaperPulse_Reverts_UnlessKept_AndStopAndPanicPutTheDesktopBack()
    {
        var s = CoreSettings.Current;
        var (folder, original, keep, pulse) = (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-c1-wallhead-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "pool"));
        var mine = Path.Combine(dir, "mine.png");
        File.WriteAllBytes(mine, new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir, "pool", "a.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir, "pool", "b.png"), new byte[] { 1 });
        var desk = new Desk { Current = mine };
        var timers = new List<Timer>();
        IDisposable After(Action act, TimeSpan wait) { var t = new Timer { Act = act, Wait = wait }; timers.Add(t); return t; }
        try
        {
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds) = (Path.Combine(dir, "pool"), "", false, 45);
            WallpaperHead.ResetForTest();
            WallpaperHead.Backend = () => desk;
            Assert.True(WallpaperHead.Supported);

            WallpaperHead.TriggerChange(After);
            Assert.NotEqual(mine, desk.Current);
            Assert.Equal(TimeSpan.FromSeconds(45), Assert.Single(timers).Wait);

            WallpaperHead.TriggerChange(After);                 // a second change REPLACES the pending revert (#694)
            Assert.True(timers[0].Dead);
            timers[1].Fire();
            Assert.Equal(mine, desk.Current);

            s.WallpaperEnabled = true;                          // "Keep the wallpaper"
            WallpaperHead.TriggerChange(After);
            Assert.Equal(2, timers.Count);                      // no revert armed
            WallpaperHead.OnTakeoverStopped();                  // stopping Takeover leaves a kept change alone
            Assert.NotEqual(mine, desk.Current);
            WallpaperHead.Restore();                            // panic / app close: always back
            Assert.Equal(mine, desk.Current);

            s.WallpaperEnabled = false;
            WallpaperHead.TriggerChange(After);
            WallpaperHead.OnTakeoverStopped();                  // not kept: back at once, and the revert is dropped
            Assert.Equal(mine, desk.Current);
            Assert.True(timers[^1].Dead);
        }
        finally
        {
            WallpaperHead.ResetForTest();
            (s.WallpaperSourceFolder, s.WallpaperOriginalPath, s.WallpaperEnabled, s.WallpaperPulseSeconds) = (folder, original, keep, pulse);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LinuxReaders_TakeOnlyAFileThatExists()
    {
        var file = Path.Combine(Path.GetTempPath(), "ccp c1 " + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(file, new byte[] { 1 });
        try
        {
            var uri = new Uri(file).AbsoluteUri;
            Assert.Equal(file, WallpaperHead.PathFromGsettings("'" + uri + "'\n"));
            Assert.Null(WallpaperHead.PathFromGsettings("''"));
            Assert.Null(WallpaperHead.PathFromGsettings("'file:///no/such/picture.png'"));
            Assert.Equal(file, WallpaperHead.PathFromPlasmaConfig(new[]
            {
                "[Containments][1][Wallpaper][org.kde.slideshow][General]", "Image=file:///no/such.png",
                "[Containments][2][Wallpaper][org.kde.image][General]", "Image=" + uri,
            }));
            Assert.Null(WallpaperHead.PathFromPlasmaConfig(new[] { "[Other]", "Image=" + uri }));
        }
        finally { File.Delete(file); }
    }

    private sealed class Clip : MantraChantService.IClip
    {
        public double Level;
        public bool Stopped;
        public string Path = "";
        public double Volume { set => Level = value; }
        public void Dispose() => Stopped = true;
    }

    [Fact]
    public void Chant_PlaysThePairThenTheGap_FollowsVolumeAndMute_AndPanicDisarms()
    {
        var s = CoreSettings.Current;
        var (on, vol, gap, master, muted) = (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.MasterVolume, s.AvatarMuted);
        var oldMuted = MantraChantService.CompanionMuted;
        var clips = new List<Clip>();
        var timers = new List<Timer>();
        bool isMuted = false;
        try
        {
            (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.MasterVolume) = (true, 50, 7, 100);
            MantraChantService.CompanionMuted = () => isMuted;
            var voiced = true;
            var chant = new MantraChantService
            {
                HasVoiced = () => voiced,
                Next = () => new MantraEntry { PromptAudio = "ask.mp3", ResponseAudio = "yes.mp3" },
                Resolve = f => f,
                Play = (path, level, ended) => { var c = new Clip { Path = path, Level = level }; clips.Add(c); return c; },
                After = (act, wait) => { var t = new Timer { Act = act, Wait = wait }; timers.Add(t); return t; },
            };
            Timer LastGap() => timers.FindLast(t => t.Wait != TimeSpan.FromSeconds(0.5))!;

            voiced = false;
            chant.Start();
            Assert.False(chant.IsRunning);                       // no recorded clips: it never loops silence
            voiced = true;

            chant.Start();
            Assert.True(chant.IsRunning);
            Assert.Equal("ask.mp3", Assert.Single(clips).Path);
            Assert.Equal(0.5, clips[0].Level, 3);                // chant 50% x master 100%

            s.MantraChantVolume = 20;
            chant.ApplyVolume();                                 // live, on the clip that is playing
            Assert.Equal(0.2, clips[0].Level, 3);

            chant.OnClipEndedForTest();
            Assert.Equal(MantraChantService.PairBeat, LastGap().Wait);   // a beat to say it into
            LastGap().Fire();
            Assert.Equal("yes.mp3", clips[1].Path);              // never the ask on its own (#685)
            chant.OnClipEndedForTest();
            Assert.Equal(TimeSpan.FromSeconds(7), LastGap().Wait);       // then the user's gap
            LastGap().Fire();
            Assert.Equal("ask.mp3", clips[2].Path);

            isMuted = true;                                      // "Mute avatar" silences her
            chant.OnMuteWatchTick();
            Assert.True(clips[2].Stopped);
            isMuted = false;
            chant.OnMuteWatchTick();
            Assert.Equal("ask.mp3", clips[3].Path);              // back on a whole mantra

            chant.StopAndDisarm();                               // panic ENDS it
            Assert.True(clips[3].Stopped);
            Assert.False(chant.IsRunning);
            Assert.False(s.MantraChantEnabled);
            int played = clips.Count;
            foreach (var t in timers.ToArray()) t.Fire();
            Assert.Equal(played, clips.Count);                   // nothing comes back after a stop

            Assert.Equal(0f, MantraChantService.ResolveVolume(true, 100, 100));
            Assert.Equal(0.25f, MantraChantService.ResolveVolume(false, 50, 50));
            Assert.Single(MantraChantService.ResolveClipSequence("ask.mp3", null));
        }
        finally
        {
            MantraChantService.CompanionMuted = oldMuted;
            (s.MantraChantEnabled, s.MantraChantVolume, s.MantraChantGapSeconds, s.MasterVolume, s.AvatarMuted) = (on, vol, gap, master, muted);
        }
    }
}
