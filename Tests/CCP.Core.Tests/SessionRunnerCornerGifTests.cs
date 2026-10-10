using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane c1 (U13): the session-scoped corner GIF, WPF SessionEngine.cs 7.1.5. The template asks, the
/// user's master and the user's own corner slots decide, the start / end minutes are honoured, a pause hides
/// it without giving the corner up, and the terminal close hands the corner back.</summary>
[Collection(SessionStatics.Name)]
public sealed class SessionRunnerCornerGifTests : IDisposable
{
    private readonly SessionRunner _runner = new(new SessionLogService());
    private readonly List<string> _calls = new();
    private bool _standalone, _spiralUp;
    private readonly bool _savedMaster, _savedSpiral;
    private readonly string _art;

    public SessionRunnerCornerGifTests()
    {
        PhrasePoolCustody.Seed();
        CoreBouncingText.StartAction = CoreBouncingText.StopAction = null;
        var s = CoreSettings.Current;
        (_savedMaster, _savedSpiral) = (s.SessionCornerGifAllowed, s.SpiralEnabled);
        s.SessionCornerGifAllowed = true;
        s.SpiralEnabled = true;
        _art = Path.Combine(Path.GetTempPath(), "ccp-c1-corner-" + Guid.NewGuid().ToString("N") + ".gif");
        File.WriteAllBytes(_art, new byte[] { 1 });
        CoreCornerGif.ShowSessionHandler = (path, pos, size, opacity) => _calls.Add($"show {Path.GetFileName(path)} {pos} {size} {opacity}");
        CoreCornerGif.HideSessionHandler = handBack => _calls.Add(handBack ? "hide+handback" : "hide");
        CoreCornerGif.StandaloneActiveProvider = () => _standalone;
        CoreCornerGif.SpiralVisibleProvider = () => _spiralUp;
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        CoreSession.IsSessionRunningProvider = null;
        CoreCornerGif.ShowSessionHandler = null;
        CoreCornerGif.HideSessionHandler = null;
        CoreCornerGif.StandaloneActiveProvider = null;
        CoreCornerGif.SpiralVisibleProvider = null;
        CoreCornerGif.SessionActive = false;
        var s = CoreSettings.Current;
        (s.SessionCornerGifAllowed, s.SpiralEnabled) = (_savedMaster, _savedSpiral);
        try { File.Delete(_art); } catch { }
    }

    private Session Make(Action<SessionSettings> set)
    {
        var ss = new SessionSettings { CornerGifEnabled = true, CornerGifPath = _art, CornerGifPosition = CornerPosition.TopRight, CornerGifSize = 120, CornerGifOpacity = 40 };
        set(ss);
        return new Session { Id = "corner-test", Name = "Corner", DurationMinutes = 30, Settings = ss };
    }

    [Fact]
    public void MinuteZero_ShowsAtStart_PauseHidesWithoutHandback_ResumeBringsItBack_StopHandsTheCornerBack()
    {
        _runner.Start(Make(_ => { }));
        Assert.Equal(new[] { $"show {Path.GetFileName(_art)} TopRight 120 40" }, _calls);
        Assert.True(_runner.IsCornerGifShown);
        Assert.True(CoreCornerGif.SessionActive);

        _runner.Pause();
        Assert.Equal("hide", _calls[^1]);
        Assert.False(CoreCornerGif.SessionActive);
        _runner.RefreshCornerGifPolicy();          // a standalone change during a pause never raises it
        Assert.Equal(2, _calls.Count);

        _runner.Resume();
        Assert.StartsWith("show", _calls[^1]);

        _runner.PanicCloseCornerGif();             // off the screen, the claim stays
        Assert.Equal("hide", _calls[^1]);

        _runner.Stop();
        Assert.Equal("hide+handback", _calls[^1]); // the debt is paid by the terminal close
        Assert.False(CoreCornerGif.SessionActive);
    }

    [Fact]
    public void StartAndEndMinutes_AreHonoured()
    {
        _runner.Start(Make(ss => { ss.CornerGifStartMinute = 5; ss.CornerGifEndMinute = 10; }));
        Assert.Empty(_calls);
        _runner.Tick(TimeSpan.FromMinutes(4.9));
        Assert.Empty(_calls);
        _runner.Tick(TimeSpan.FromMinutes(5.1));
        Assert.Single(_calls);
        _runner.Tick(TimeSpan.FromMinutes(10.1));
        Assert.Equal("hide+handback", _calls[^1]);
        _runner.Tick(TimeSpan.FromMinutes(11));
        Assert.Equal(2, _calls.Count);             // past its end: it does not come back
    }

    [Fact]
    public void TheUserMasterAndTheUsersOwnSlots_DecideLive()
    {
        var s = CoreSettings.Current;
        s.SessionCornerGifAllowed = false;
        _runner.Start(Make(_ => { }));
        Assert.Empty(_calls);                      // the Spiral card's switch is off: nothing

        s.SessionCornerGifAllowed = true;
        _runner.RefreshCornerGifPolicy();          // ticked mid-session: it appears
        Assert.Single(_calls);

        _standalone = true;                        // the user switches a corner slot of their own on
        _runner.Tick(TimeSpan.FromMinutes(1));
        Assert.Equal("hide+handback", _calls[^1]); // the session yields, it never stacks a second one

        _standalone = false;
        CoreCornerGif.RaiseStandaloneChanged();    // the slot goes off again: the session takes the corner back
        Assert.StartsWith("show", _calls[^1]);

        s.SessionCornerGifAllowed = false;
        _runner.RefreshCornerGifPolicy();
        Assert.Equal("hide+handback", _calls[^1]);
    }

    [Fact]
    public void SpiralArt_FollowsTheUsersSpiralMaster_AndNeverDoublesAFullscreenSpiral()
    {
        CoreSettings.Current.SpiralEnabled = false;            // the user's own Spiral switch, before the session
        _runner.Start(Make(ss => ss.CornerGifPath = ""));      // no art of its own = the spiral
        Assert.Empty(_calls);
        _runner.Stop();

        CoreSettings.Current.SpiralEnabled = true;
        _spiralUp = true;
        _runner.Start(Make(ss => ss.CornerGifPath = ""));
        Assert.Empty(_calls);                                  // a spiral already fills the screen
        _spiralUp = false;
        _runner.RefreshCornerGifPolicy();
        Assert.Equal("show  TopRight 120 40", _calls[^1]);

        _runner.Stop();
        _calls.Clear();
        _spiralUp = true;
        _runner.Start(Make(_ => { }));                         // its own art is neither of those things
        Assert.Single(_calls);
    }

    [Fact]
    public void LiveEdits_RecreateAShownGif_AndAreKeptForOneThatHasNotStarted()
    {
        _runner.Start(Make(ss => ss.CornerGifStartMinute = 5));
        _runner.UpdateCornerGif(size: 200, opacity: 60);       // not on screen yet: kept, nothing drawn
        Assert.Empty(_calls);
        _runner.Tick(TimeSpan.FromMinutes(5.5));
        Assert.Equal($"show {Path.GetFileName(_art)} TopRight 200 60", _calls[^1]);

        _runner.UpdateCornerGif(position: CornerPosition.BottomLeft);
        Assert.Equal($"show {Path.GetFileName(_art)} BottomLeft 200 60", _calls[^1]);   // recreated in place
        Assert.DoesNotContain("hide+handback", _calls);                                   // the corner stays the session's
    }

    [Fact]
    public void AdmissionRule_TruthTable()
    {
        Assert.True(CornerGifMedia.AllowSessionCornerGif(true, true, false, false, false, true));
        Assert.False(CornerGifMedia.AllowSessionCornerGif(false, true, false, false, true, false));
        Assert.False(CornerGifMedia.AllowSessionCornerGif(true, false, false, false, true, false));
        Assert.False(CornerGifMedia.AllowSessionCornerGif(true, true, true, false, true, false));
        Assert.True(CornerGifMedia.AllowSessionCornerGif(true, true, false, true, true, false));
        Assert.False(CornerGifMedia.AllowSessionCornerGif(true, true, false, true, false, false));
        Assert.False(CornerGifMedia.AllowSessionCornerGif(true, true, false, true, true, true));
        Assert.True(CornerGifMedia.AllowStandaloneCornerGif(true, false));
        Assert.False(CornerGifMedia.AllowStandaloneCornerGif(true, true));
        Assert.False(CornerGifMedia.AllowStandaloneCornerGif(false, false));
        Assert.True(CornerGifMedia.SessionCornerArtIsSpiral(""));
        Assert.True(CornerGifMedia.SessionCornerArtIsSpiral(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".gif")));
        Assert.False(CornerGifMedia.SessionCornerArtIsSpiral(_art));
    }
}
