using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane w2: the screen reader's service rules (WPF ScreenOcrService) with a fake reader, the
/// highlight box geometry (WPF KeywordHighlightService) and the X11 key translation. No test reads the
/// real screen or keyboard (TestUserDataProfile sets both Disabled flags).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ScreenOcrServiceTests
{
    private sealed class FakeReader : IScreenTextReader
    {
        public int Reads;
        public List<OcrWordHit> Words = new();
        public List<(int, int, int, int)> Own = new();
        public bool IsAvailable => true;
        public Task<List<OcrWordHit>> ReadAllScreensAsync() { Reads++; return Task.FromResult(new List<OcrWordHit>(Words)); }
        public IReadOnlyList<(int X, int Y, int Width, int Height)> OwnWindowRects() => Own;
    }

    private static async Task WithService(AppSettings s, FakeReader reader, Func<KeywordTriggerEngine, List<KeywordFire>, Task> body)
    {
        var engine = new KeywordTriggerEngine { HasAccess = () => true, Settings = () => s, Now = () => new DateTime(2026, 10, 10, 12, 0, 0) };
        var fires = new List<KeywordFire>();
        engine.Dispatch += fires.Add;
        engine.Start();
        var service = new ConditioningControlPanel.Services.SettingsService();
        var (oldEngine, oldUi, oldReader) = (ScreenOcrService.Engine, ScreenOcrService.OnUi, ScreenOcrService.ReaderForTest);
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var live = ConditioningControlPanel.CoreSettings.Current;
        var (wasOcr, wasMaster, wasOwn) = (live.ScreenOcrEnabled, live.KeywordTriggersEnabled, live.AwarenessIgnoreOwnUi);
        live.ScreenOcrEnabled = s.ScreenOcrEnabled;
        live.KeywordTriggersEnabled = s.KeywordTriggersEnabled;
        live.AwarenessIgnoreOwnUi = s.AwarenessIgnoreOwnUi;
        ScreenOcrService.Engine = engine;
        ScreenOcrService.OnUi = a => { a(); return Task.CompletedTask; };
        ScreenOcrService.ReaderForTest = reader;
        try { await body(engine, fires); }
        finally
        {
            ScreenOcrService.Engine = oldEngine;
            ScreenOcrService.OnUi = oldUi;
            ScreenOcrService.ReaderForTest = oldReader;
            live.ScreenOcrEnabled = wasOcr;
            live.KeywordTriggersEnabled = wasMaster;
            live.AwarenessIgnoreOwnUi = wasOwn;
            ConditioningControlPanel.CoreSettings.ServiceProvider = null;
        }
    }

    private static AppSettings Settings(int confirm)
    {
        var t = KeywordTriggerEngine.NewCustomTrigger("obey");
        t.CooldownSeconds = 0;
        return new AppSettings
        {
            KeywordTriggersEnabled = true, ScreenOcrEnabled = true, OcrConfirmationScans = confirm, OcrHighlightAll = true,
            AwarenessLoopProtectionEnabled = false, KeywordPerKeywordCooldownSeconds = 0,
            KeywordGlobalCooldownSeconds = 0, AwarenessIgnoreOwnUi = true, KeywordTriggers = new List<KeywordTrigger> { t },
        };
    }

    [Fact]
    public void ShouldRun_Needs_The_Master_The_Switch_And_Access()
    {
        var s = new AppSettings { KeywordTriggersEnabled = true, ScreenOcrEnabled = true };
        Assert.True(ScreenOcrService.ShouldRun(s, true));
        Assert.False(ScreenOcrService.ShouldRun(s, false));
        s.ScreenOcrEnabled = false;
        Assert.False(ScreenOcrService.ShouldRun(s, true));
        s.ScreenOcrEnabled = true;
        s.KeywordTriggersEnabled = false;
        Assert.False(ScreenOcrService.ShouldRun(s, true));
        Assert.False(ScreenOcrService.ShouldRun(null, true));
    }

    [Fact]
    public Task A_Tick_Confirms_With_Quick_Follow_Up_Scans_And_Fires() => WithService(Settings(3), new FakeReader
    {
        Words = { new OcrWordHit("obey", 500, 500, 40, 16) },
    }, async (engine, fires) =>
    {
        var reader = (FakeReader)ScreenOcrService.ReaderForTest!;
        await ScreenOcrService.TickAsync();
        Assert.Equal(3, reader.Reads);   // one discovery scan + two quick confirms (never a fourth)
        Assert.Single(fires);
        Assert.False(engine.NeedsOcrConfirmation);
    });

    [Fact]
    public Task Words_Inside_The_Apps_Own_Windows_Never_Reach_The_Matcher() => WithService(Settings(1), new FakeReader
    {
        Words = { new OcrWordHit("obey", 120, 120, 40, 16) },
        Own = { (100, 100, 400, 300) },
    }, async (_, fires) =>
    {
        await ScreenOcrService.TickAsync();
        Assert.Empty(fires);
        ConditioningControlPanel.CoreSettings.Current.AwarenessIgnoreOwnUi = false;
        await ScreenOcrService.TickAsync();
        Assert.Single(fires);
    });

    [Fact]
    public Task A_Tick_With_The_Switch_Off_Reads_Nothing() => WithService(Settings(1), new FakeReader
    {
        Words = { new OcrWordHit("obey", 500, 500, 40, 16) },
    }, async (_, fires) =>
    {
        ConditioningControlPanel.CoreSettings.Current.ScreenOcrEnabled = false;
        await ScreenOcrService.TickAsync();
        Assert.Equal(0, ((FakeReader)ScreenOcrService.ReaderForTest!).Reads);
        Assert.Empty(fires);
        Assert.False(ScreenOcrService.IsRunning);
    });

    [Fact]
    public void Disabled_In_Tests_Start_Never_Opens_A_Timer()
    {
        Assert.True(ScreenOcrService.Disabled);
        ScreenOcrService.Start();
        Assert.False(ScreenOcrService.IsRunning);
    }

    // ------------------------------------------------------------------ highlight

    [Fact]
    public void Highlight_Box_Is_The_Word_Scaled_To_The_Canvas_With_Ten_Of_Pad()
    {
        // A 150% screen at (1920, 0): 2880x1620 pixels drawn on a 1920x1080 canvas.
        var screen = new PixelRect(1920, 0, 2880, 1620);
        var box = KeywordHighlightOverlay.LocalBox(new OcrWordHit("obey", 1920 + 300, 150, 90, 30), screen, 1920, 1080);
        Assert.Equal(new Rect(200 - 10, 100 - 10, 60 + 20, 20 + 20), box);
    }

    [Fact]
    public void Highlight_Envelope_Holds_Sixty_Percent_Then_Fades()
    {
        Assert.Equal((900, 600), KeywordHighlightOverlay.Envelope(1500));
        Assert.Equal((0, 1), KeywordHighlightOverlay.Envelope(0));
    }

    [Fact]
    public void Highlight_Colour_Falls_Back_To_Neon_Pink()
    {
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(0, 0xFF, 0xFF), KeywordHighlightOverlay.ParseColor("#00FFFF"));
        Assert.Equal(KeywordHighlightOverlay.DefaultColor, KeywordHighlightOverlay.ParseColor("not a colour"));
        Assert.Equal(KeywordHighlightOverlay.DefaultColor, KeywordHighlightOverlay.ParseColor(null));
    }

    [Fact]
    public void Highlight_Picks_The_Screen_Holding_The_Words_Centre()
    {
        var screens = new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(1920, 0, 2560, 1440) };
        Assert.Equal(0, KeywordHighlightOverlay.ScreenIndexOf(new OcrWordHit("a", 100, 100, 40, 16), screens));
        Assert.Equal(1, KeywordHighlightOverlay.ScreenIndexOf(new OcrWordHit("a", 2000, 100, 40, 16), screens));
        Assert.Equal(0, KeywordHighlightOverlay.ScreenIndexOf(new OcrWordHit("a", -5000, 100, 40, 16), screens));
    }

    // ------------------------------------------------------------------ X11 keys

    [Fact]
    public void X11_Keys_Translate_To_Buffer_Keys()
    {
        const int Shift = 1, Lock = 2, Ctrl = 4, Alt = 8;
        uint A(int level) => level == 0 ? 0x61u : 0x41u;   // a / A
        Assert.Equal((X11KeyListener.Key.Char, 'a'), X11KeyListener.Translate(0, A));
        Assert.Equal((X11KeyListener.Key.Char, 'A'), X11KeyListener.Translate(Shift, A));
        Assert.Equal((X11KeyListener.Key.Char, 'A'), X11KeyListener.Translate(Lock, A));
        Assert.Equal((X11KeyListener.Key.Char, 'a'), X11KeyListener.Translate(Lock | Shift, A));
        Assert.Null(X11KeyListener.Translate(Ctrl, A));    // a chord is not text
        Assert.Null(X11KeyListener.Translate(Alt, A));
        Assert.Equal(X11KeyListener.Key.Backspace, X11KeyListener.Translate(0, _ => 0xFF08)!.Value.Key);
        Assert.Equal(X11KeyListener.Key.Clear, X11KeyListener.Translate(0, _ => 0xFF0D)!.Value.Key);
        Assert.Equal(X11KeyListener.Key.Clear, X11KeyListener.Translate(0, _ => 0xFF1B)!.Value.Key);
        Assert.Equal(X11KeyListener.Key.Space, X11KeyListener.Translate(0, _ => 0x20)!.Value.Key);
        Assert.Equal((X11KeyListener.Key.Char, 'é'), X11KeyListener.Translate(0, _ => 0xE9));
        Assert.Equal((X11KeyListener.Key.Char, 'ż'), X11KeyListener.Translate(0, _ => 0x0100017C));
        Assert.Equal((X11KeyListener.Key.Char, '7'), X11KeyListener.Translate(0, _ => 0xFFB7));
        Assert.Null(X11KeyListener.Translate(0, _ => 0xFFE1));   // Shift_L alone
    }

    [Fact]
    public void X11_NumberPad_Follows_NumLock()
    {
        const int Shift = 1, NumLock = 0x10;
        uint Pad7(int level) => level == 0 ? 0xFF95u : level == 1 ? 0xFFB7u : 0u;   // KP_Home / KP_7
        Assert.Null(X11KeyListener.Translate(0, Pad7));                                             // NumLock off: Home
        Assert.Equal((X11KeyListener.Key.Char, '7'), X11KeyListener.Translate(NumLock, Pad7));      // on: the digit
        Assert.Null(X11KeyListener.Translate(NumLock | Shift, Pad7));                               // Shift undoes it
        // A one-level pad key (KP_Enter) keeps its meaning with NumLock on.
        Assert.Equal(X11KeyListener.Key.Clear, X11KeyListener.Translate(NumLock, l => l == 0 ? 0xFF8Du : 0u)!.Value.Key);
        // NumLock never changes a letter.
        Assert.Equal((X11KeyListener.Key.Char, 'a'), X11KeyListener.Translate(NumLock, l => l == 0 ? 0x61u : 0x41u));
    }

    [Fact]
    public void X11_Idle_Wobble_Is_Not_New_Input()
    {
        long first = HeadInputProbe.StableInputMoment(5_000_000);
        Assert.Equal(first, HeadInputProbe.StableInputMoment(5_000_000 + HeadInputProbe.InputMomentSlackMs));   // same moment, read late
        Assert.Equal(first, HeadInputProbe.StableInputMoment(5_000_000 - 7));
        long next = HeadInputProbe.StableInputMoment(5_000_000 + 400);                                          // a real key press
        Assert.NotEqual(first, next);
        Assert.Equal(next, HeadInputProbe.StableInputMoment(5_000_000 + 395));
    }

    [Fact]
    public void Linux_Font_Standins_Cover_The_Windows_Faces_And_Stay_Off_Windows()
    {
        foreach (var name in new[] { "Consolas", "Courier New", "monospace", "Segoe UI" })
            Assert.True(AppFonts.Substitutes.ContainsKey(name), name);
        Assert.Equal(AppFonts.BundledMono, AppFonts.Substitutes["Consolas"]);
        if (System.OperatingSystem.IsWindows()) Assert.Null(AppFonts.Options());
        else Assert.Equal(AppFonts.Substitutes.Count, AppFonts.Options()!.FontFamilyMappings!.Count);
    }

    [Fact]
    public void X11_Listener_Never_Starts_Off_Linux_Or_In_Tests()
    {
        X11KeyListener.Sync(true, (_, _) => { });
        Assert.False(X11KeyListener.IsRunning);
    }
}
