using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Platform;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosOverlayWindow : Window
{
    private readonly ILogger<ChaosOverlayWindow> _logger;
    private readonly global::ConditioningControlPanel.Core.Platform.IInputHook? _inputHook;

    private Action<ChaosBoon?>? _onBoonPick;
    private bool _clickThrough = true;

    private sealed class DraftCard
    {
        public Border Card = null!;
        public Button Pick = null!;
        public ScaleTransform Scale = null!;
        public ChaosBoon Boon = null!;
        public Border Art = null!;
        public SolidColorBrush ArtBorder = null!;
    }

    private static ChaosHubWindow? _sharedHubWindow;

    private readonly List<DraftCard> _draftCards = new();
    private DispatcherTimer? _revealTimer;
    private int _revealIndex;
    private ChaosBoon? _selectedBoon;
    private bool _selectionMade;

    private readonly ScaleTransform _countdownScale = new(1, 1);
    private readonly ScaleTransform _storyBgScale = new(1.08, 1.08);
    private readonly TranslateTransform _storyBgT = new(0, 0);
    private readonly TranslateTransform _storyPortraitT = new(-260, 0);
    private readonly ScaleTransform _storyBoxScale = new(1, 1);
    private readonly TranslateTransform _storyAdvanceT = new(0, 0);

    // Ignore low-level key events for a short window after an interactive panel
    // becomes visible. This prevents the same keypress/click that launched the
    // overlay from also skipping the countdown or advancing the first story card.
    private long _interactiveShownAt;
    private const int InputIgnoreMs = 500;

    public Action? OnRunAgain;
    public Action? OnDismissed;

    public ChaosOverlayWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosOverlayWindow>>();
        _inputHook = App.Services.GetService<global::ConditioningControlPanel.Core.Platform.IInputHook>();
        if (_inputHook != null) _inputHook.KeyPressed += OnGlobalKey;
        CountdownText.RenderTransform = _countdownScale;
        StoryBg.RenderTransform = new TransformGroup { Children = { _storyBgScale, _storyBgT } };
        StoryPortrait.RenderTransform = _storyPortraitT;
        StoryBox.RenderTransform = _storyBoxScale;
        StoryAdvance.RenderTransform = _storyAdvanceT;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        // ponytail: keep the roguelike game UI on the primary monitor; ambient overlays
        // (pink filter, spiral, bubbles, video, flash, subliminal) still respect DualMonitorEnabled.
        var bounds = AvaloniaChaosWindowZ.StageBounds(forcePrimary: true);
        Position = new PixelPoint((int)bounds.left, (int)bounds.top);
        Width = bounds.width;
        Height = bounds.height;
        // Start in click-through mode; draft/results/story cards toggle it off when they need input.
        SetClickThrough(true);
        Opened += (_, _) => ApplyExStyles();
        Closed += (_, _) =>
        {
            if (_inputHook != null) _inputHook.KeyPressed -= OnGlobalKey;
        };
        StoryCardPanel.PointerPressed += (_, e) => { e.Handled = true; AdvanceStory(); };
        KeyDown += OnStoryKey;
    }

    private void OnGlobalKey(object? sender, global::ConditioningControlPanel.Core.Platform.KeyboardHookEventArgs e)
    {
        if (!IsVisible) return;
        if (IsInputSuppressed()) return;

        if (_countdownTimer != null && CountdownBox.IsVisible)
        {
            FinishCountdown();
            return;
        }
        if (StoryCardPanel.IsVisible && IsAdvanceStoryKey(e.VirtualKeyCode))
        {
            AdvanceStory();
        }
    }

    private static bool IsAdvanceStoryKey(int vk)
    {
        // Match the window-level key handler: Space, Enter, or Right Arrow.
        // Avoids typing elsewhere from accidentally skipping the scene.
        return vk is 0x20 or 0x0D or 0x27;
    }

    private bool IsInputSuppressed()
    {
        return Environment.TickCount64 - _interactiveShownAt < InputIgnoreMs;
    }

    #region countdown

    private DispatcherTimer? _countdownTimer;
    private Action? _countdownComplete;
    private bool _countdownFinished;

    public void ShowCountdown(Action onComplete, bool shortFlash = false)
    {
        string[] steps = shortFlash ? new[] { "SINK" } : new[] { "3", "2", "1", "SINK" };
        int interval = shortFlash ? 1000 : 750;
        ShowCountdownSteps(steps, interval, onComplete);
    }

    public void ShowReadyGo(Action onComplete)
        => ShowCountdownSteps(new[] { "ready? :3", "SINK" }, 800, onComplete);

    private void ShowCountdownSteps(string[] steps, int interval, Action onComplete)
    {
        _interactiveShownAt = Environment.TickCount64;
        SetClickThrough(true);
        Backdrop.IsVisible = false;
        DraftPanel.IsVisible = false;
        ResultsPanel.IsVisible = false;
        CountdownBox.IsVisible = true;

        _countdownComplete = onComplete;
        _countdownFinished = false;

        int i = 0;
        ShowCountdownStep(steps[0]);

        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _countdownTimer.Tick += (_, _) =>
        {
            i++;
            if (i < steps.Length) ShowCountdownStep(steps[i]);
            else FinishCountdown();
        };
        _countdownTimer.Start();
    }

    private void FinishCountdown()
    {
        if (_countdownFinished) return;
        _countdownFinished = true;
        _countdownTimer?.Stop();
        _countdownTimer = null;
        CountdownBox.IsVisible = false;
        var cb = _countdownComplete;
        _countdownComplete = null;
        cb?.Invoke();
    }

    private void ShowCountdownStep(string text)
    {
        AvaloniaChaosSfx.Play(text == "SINK" ? "sink" : "countdown_tick", text == "SINK" ? 0.6f : 0.45f);
        CountdownText.Text = text;
        CountdownText.Foreground = text == "SINK" ? new SolidColorBrush(Color.FromRgb(120, 255, 160)) : AppBrush("TextLightBrush", _whiteFallback);
        _countdownScale.ScaleX = _countdownScale.ScaleY = 1.5;
        Opacity = 1;
        AnimateTransform(_countdownScale, 1.5, 1.0, 350, EaseOutBack);
    }

    #endregion

    #region boon draft

    private DispatcherTimer? _autoResumeTimer;
    private DispatcherTimer? _confirmTimer;
    private int _autoResumeRemainingSec;
    private int _draftWave;
    private Func<(List<ChaosBoon> options, int rerollsLeft)?>? _rerollFunc;
    private bool _skipAllowed = true;

    public void ShowBoonDraft(int waveJustCleared, List<ChaosBoon> options, Action<ChaosBoon?> onPick, int autoResumeSec = 0,
                              int rerollsLeft = 0, Func<(List<ChaosBoon> options, int rerollsLeft)?>? onReroll = null)
    {
        _interactiveShownAt = Environment.TickCount64;
        _onBoonPick = onPick;
        _selectedBoon = null;
        _selectionMade = false;
        _draftWave = waveJustCleared;
        _rerollFunc = onReroll;
        BtnReroll.IsVisible = rerollsLeft > 0 && onReroll != null;
        BtnReroll.Content = rerollsLeft > 1 ? $"🎲 tempt fate again ({rerollsLeft} left)" : "🎲 tempt fate again";
        _autoResumeTimer?.Stop();
        _confirmTimer?.Stop();
        _autoResumeRemainingSec = autoResumeSec;
        SetClickThrough(false);
        CountdownBox.IsVisible = false;
        ResultsPanel.IsVisible = false;
        Backdrop.IsVisible = true;
        DraftPanel.IsVisible = true;
        BringToFront();

        AvaloniaChaosSfx.Play("cards_in", 0.5f);

        bool hasSin = options.Exists(o => o.IsCurse);
        DraftTitle.Text = hasSin
            ? $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA... OR DON'T"
            : $"LOOP {waveJustCleared} CLEARED · CHOOSE A MANTRA";
        DraftCountdown.Text = "";
        _skipAllowed = RevealService.IsUnlocked(RevealIds.DraftSkip);
        BtnSkipBoon.IsVisible = _skipAllowed;
        if (_skipAllowed && !ChaosMeta.State.SeenSkipDebut)
        {
            ChaosHappyPath.OnSkipDebutAvailable();
        }

        BoonCardHost.Children.Clear();
        _draftCards.Clear();
        foreach (var boon in options)
        {
            var dc = BuildBoonCard(boon);
            dc.Card.Opacity = 0;
            dc.Scale.ScaleX = dc.Scale.ScaleY = 0.7;
            dc.Pick.IsEnabled = false;
            _draftCards.Add(dc);
            BoonCardHost.Children.Add(dc.Card);
        }

        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _revealTimer.Tick += (_, _) =>
        {
            if (_revealIndex >= _draftCards.Count)
            {
                _revealTimer?.Stop();
                if (!_selectionMade)
                {
                    if (_autoResumeRemainingSec > 0) StartAutoResume();
                    else DraftCountdown.Text = "the field holds. take your time.";
                }
                return;
            }
            RevealCard(_draftCards[_revealIndex]);
            _revealIndex++;
        };
        _revealIndex = 0;
        if (_draftCards.Count > 0) { RevealCard(_draftCards[0]); _revealIndex = 1; }
        _revealTimer?.Start();
    }

    private void RevealCard(DraftCard dc)
    {
        dc.Pick.IsEnabled = true;
        new OpacityFade(dc.Card, 0, 1, 220);
        AnimateTransform(dc.Scale, 0.7, 1.0, 300, EaseOutBack);
        if (dc.Boon.IsCurse) AvaloniaChaosSfx.Play("sin_reveal", 0.55f);
        else AvaloniaChaosSfx.PlayBoonReveal(dc.Boon.Rarity == ChaosRarity.Rare);
    }

    private void HideDraft()
    {
        _revealTimer?.Stop();
        _autoResumeTimer?.Stop();
        _confirmTimer?.Stop();
        foreach (var c in _draftCards)
        {
            try
            {
                c.Card.Effect = null;
                c.Art.Effect = null;
            }
            catch { }
        }
        _draftCards.Clear();
        _selectedBoon = null;
        _selectionMade = false;
        _rerollFunc = null;
        BtnReroll.IsVisible = false;
        DraftPanel.IsVisible = false;
        Backdrop.IsVisible = false;
        SetClickThrough(true);
    }

    private void BtnReroll_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectionMade) return;
        var pick = _onBoonPick;
        var result = _rerollFunc?.Invoke();
        if (pick == null || result == null) { BtnReroll.IsVisible = false; return; }
        AvaloniaChaosSfx.PlayBoonReveal(true);
        ShowBoonDraft(_draftWave, result.Value.options, pick, _autoResumeRemainingSec,
                      result.Value.rerollsLeft, _rerollFunc);
    }

    private void StartAutoResume()
    {
        string CountText() => _skipAllowed
            ? $"auto-resist in {_autoResumeRemainingSec}s. pick to keep playing"
            : $"it chooses in {_autoResumeRemainingSec}s. pick first if you'd rather";
        DraftCountdown.Text = CountText();
        _autoResumeTimer?.Stop();
        _autoResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoResumeTimer.Tick += (_, _) =>
        {
            _autoResumeRemainingSec--;
            if (_selectionMade) { _autoResumeTimer?.Stop(); return; }
            if (_autoResumeRemainingSec <= 0)
            {
                _autoResumeTimer?.Stop();
                if (_skipAllowed) ChooseBoon(null);
                else AutopickRandom();
                return;
            }
            DraftCountdown.Text = CountText();
        };
        _autoResumeTimer.Start();
    }

    private void AutopickRandom()
    {
        if (_selectionMade || _draftCards.Count == 0) return;
        var revealed = _draftCards.FindAll(dc => dc.Pick.IsEnabled);
        var pool = revealed.Count > 0 ? revealed : _draftCards;
        var chosen = pool[Random.Shared.Next(pool.Count)];
        try { AvaloniaChaosApp.Bark?.NotifyChaosDraftAutopick(); } catch { }
        SelectBoon(chosen);
    }

    private DraftCard BuildBoonCard(ChaosBoon boon)
    {
        var accent = boon.IsCurse ? AppColor("Danger", Color.FromRgb(255, 120, 120))
                   : boon.RequiresAny != null || boon.RequiresAll != null ? Color.FromRgb(255, 215, 0)
                   : Color.FromRgb(156, 232, 160);
        var accentBrush = new SolidColorBrush(accent);

        var panel = new StackPanel { Width = 190 };

        var artBorderBrush = new SolidColorBrush(accent);
        var artFill = ChaosArt.Resolve("boons", boon.Id);
        var art = new Border
        {
            Width = 190, Height = 190,
            BorderBrush = artBorderBrush, BorderThickness = new Thickness(4),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 10),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Background = artFill != null
                ? new ImageBrush { Source = (global::Avalonia.Media.IImageBrushSource?)artFill, Stretch = Stretch.UniformToFill }
                : new SolidColorBrush(Color.FromArgb(48, 0, 0, 0)),
        };
        if (artFill == null)
            art.Child = new TextBlock
            {
                Text = boon.IsCurse ? "☠" : "◈", FontSize = 70,
                Foreground = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        ChaosTips.Attach(art, boon.Name, boon.Desc, accent: accent, flavor: boon.Flavor);
        panel.Children.Add(art);

        panel.Children.Add(new TextBlock
        {
            Text = (boon.IsCurse ? "☠ " : "◈ ") + boon.Name.ToUpperInvariant(),
            Foreground = accentBrush, FontWeight = FontWeight.Bold, FontSize = 14,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = boon.Desc, Foreground = AppBrush("TextLightBrush", _whiteFallback), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, MinHeight = 50, Margin = new Thickness(0, 0, 0, 8)
        });
        if (!string.IsNullOrEmpty(boon.Flavor))
            panel.Children.Add(new TextBlock
            {
                Text = boon.Flavor, FontStyle = FontStyle.Italic, FontSize = 10.5,
                Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8))),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8)
            });
        panel.Children.Add(new TextBlock
        {
            Text = $"{RarityDots(boon.Rarity)} {boon.Rarity}",
            Foreground = AppBrush("TextMutedBrush", new SolidColorBrush(Color.FromRgb(180, 180, 208))), FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var pickScale = new ScaleTransform(1, 1);
        var pick = new Button
        {
            Content = boon.IsCurse ? "GIVE IN" : "ACCEPT",
            Padding = new Thickness(0, 8, 0, 8), Background = accentBrush, Foreground = Brushes.Black,
            FontWeight = FontWeight.Bold, BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = pickScale
        };
        panel.Children.Add(pick);

        var scale = new ScaleTransform(1, 1);
        var card = new Border
        {
            Background = AppBrush("TransparentWhiteBrush", new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))),
            BorderBrush = accentBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Margin = new Thickness(8), Child = panel,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RenderTransform = scale
        };

        var dc = new DraftCard { Card = card, Pick = pick, Scale = scale, Boon = boon, Art = art, ArtBorder = artBorderBrush };
        pick.Click += (_, _) => SelectBoon(dc);
        art.Cursor = new Cursor(StandardCursorType.Hand);
        art.PointerPressed += (_, _) => { if (dc.Pick.IsEnabled) SelectBoon(dc); };
        return dc;
    }

    private static string RarityDots(ChaosRarity r) => r switch
    {
        ChaosRarity.Common => "◆",
        ChaosRarity.Uncommon => "◆◆",
        ChaosRarity.Rare => "◆◆◆",
        _ => "◆"
    };

    private void SelectBoon(DraftCard chosen)
    {
        if (_selectionMade) return;
        _selectionMade = true;
        _selectedBoon = chosen.Boon;
        _revealTimer?.Stop();
        _autoResumeTimer?.Stop();
        if (!chosen.Boon.IsCurse) AvaloniaChaosSfx.PlayBoonPicked();

        foreach (var dc in _draftCards)
        {
            dc.Pick.IsEnabled = false;
            if (dc == chosen) continue;
            new OpacityFade(dc.Card, dc.Card.Opacity, 0, 260);
            AnimateTransform(dc.Scale, dc.Scale.ScaleX, 0.8, 260, EaseInQuad);
        }

        chosen.Card.Opacity = 1;
        chosen.Scale.ScaleX = chosen.Scale.ScaleY = 1;
        var hi = chosen.Boon.IsCurse ? Color.FromRgb(255, 150, 150) : Color.FromRgb(120, 255, 170);
        chosen.Card.BorderBrush = new SolidColorBrush(hi);
        chosen.Card.BorderThickness = new Thickness(3);
        chosen.Card.BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb(0xE6, hi.R, hi.G, hi.B), Blur = 28, Spread = 0 });

        chosen.Art.BorderThickness = new Thickness(5);
        chosen.Art.BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb(0xF2, hi.R, hi.G, hi.B), Blur = 24, Spread = 0 });
        var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double flashStart = Environment.TickCount64;
        flashTimer.Tick += (_, _) =>
        {
            double t = (Environment.TickCount64 - flashStart) / 480.0;
            if (t >= 1) { flashTimer.Stop(); return; }
            double wave = (Math.Sin(t * Math.PI * 4) + 1) / 2;
            var c = Blend(chosen.ArtBorder.Color, hi, wave);
            chosen.ArtBorder.Color = c;
        };
        flashTimer.Start();

        if (chosen.Pick.RenderTransform is ScaleTransform ps)
            AnimateTransform(ps, 1.15, 1.0, 360, EaseOutBack);
        chosen.Pick.Content = "✓ CHOSEN";

        BtnSkipBoon.IsVisible = false;
        BtnReroll.IsVisible = false;
        DraftCountdown.Text = "";
        _confirmTimer?.Stop();
        _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _confirmTimer.Tick += (_, _) =>
        {
            _confirmTimer?.Stop();
            _confirmTimer = null;
            ChooseBoon(_selectedBoon);
        };
        _confirmTimer.Start();
    }

    private void ChooseBoon(ChaosBoon? boon)
    {
        var cb = _onBoonPick;
        _onBoonPick = null;
        if (cb == null) return;
        HideDraft();
        cb(boon);
    }

    private void BtnSkipBoon_Click(object? sender, RoutedEventArgs e) => ChooseBoon(null);

    #endregion

    #region results

    public void ShowResults(ChaosRunState s, double baseXp, double skillMult, double finalXp, long previousBest, int sparksEarned,
                            ChaosRank? rankUp = null)
    {
        _interactiveShownAt = Environment.TickCount64;
        SetClickThrough(false);
        CountdownBox.IsVisible = false;
        DraftPanel.IsVisible = false;
        Backdrop.IsVisible = true;
        ResultsPanel.IsVisible = true;
        BringToFront();

        ResultsHero.Source = ChaosArt.ResolveRecap();

        double score = s.Score;
        double pbDelta = score - previousBest;
        bool isPb = score > previousBest;
        BtnClose.Content = isPb ? "wake up (you'll be back)" : "wake up";

        AvaloniaChaosSfx.Play("surface", 0.55f);
        if (isPb)
        {
            var pbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            pbTimer.Tick += (_, _) => { pbTimer.Stop(); AvaloniaChaosSfx.Play("pb_fanfare", 0.6f); };
            pbTimer.Start();
        }

        var dim = new SolidColorBrush(AppColor("TextMuted", Color.FromRgb(170, 170, 200)));
        var gold = new SolidColorBrush(Color.FromRgb(255, 215, 90));
        var pink = new SolidColorBrush(AppColor("PinkColor", Colors.HotPink));

        ResultsBody.Children.Clear();

        ResultsBody.Children.Add(ChipRow(
            StatChip("DEPTH", $"{Roman(s.ActIndex)} · L{s.WaveIndex}"),
            StatChip("BEST STREAK", $"x{s.BestCombo}"),
            StatChip("SURVIVED", $"{(int)s.ElapsedSec / 60:00}:{(int)s.ElapsedSec % 60:00}")));

        ResultsBody.Children.Add(new TextBlock
        {
            Text = $"snapped {s.Defused} · triggered {s.Detonated} · effects fired {s.EffectsFired}",
            FontSize = 12, Foreground = dim, FontWeight = FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap
        });

        ResultsBody.Children.Add(new Border { Height = 1, Background = AppBrush("TransparentPink40Brush", new SolidColorBrush(Color.FromArgb(70, 255, 105, 180))), Margin = new Thickness(0, 10, 0, 10) });

        var scoreLine = AddResultLine("score 0", 24, AppBrush("TextLightBrush", _whiteFallback), FontWeight.Bold);
        AnimateScoreTally(scoreLine, score);
        var verdict = isPb
            ? AddResultLine($"★ NEW BEST  (+{pbDelta:N0} over {previousBest:N0})", 14, gold, FontWeight.Bold)
            : AddResultLine($"best {previousBest:N0}   ({pbDelta:N0} vs best)", 12, dim, FontWeight.Normal);
        verdict.Opacity = 0;
        var verdictTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(820) };
        verdictTimer.Tick += (_, _) => { verdictTimer.Stop(); new OpacityFade(verdict, 0, 1, 300); };
        verdictTimer.Start();

        ResultsBody.Children.Add(new Border { Height = 1, Background = AppBrush("TransparentPink40Brush", new SolidColorBrush(Color.FromArgb(70, 255, 105, 180))), Margin = new Thickness(0, 10, 0, 10) });

        var takeHome = ChipRow(
            StatChip("XP", $"{ChaosGlyphs.Xp} {finalXp:N0}", pink, $"base {baseXp:N0} x{skillMult:0.0}"),
            StatChip("DROPS", $"{ChaosGlyphs.Drops} {sparksEarned:N0}", gold, "banked in the dollhouse"));
        ResultsBody.Children.Add(takeHome);
        PopRewardChips(takeHome, firstDelayMs: 900);

        if (ChaosMeta.State.RunsCompleted == 1)
            AddResultLine($"{ChaosGlyphs.Drops} +{ChaosMeta.FIRST_FALL_BONUS} first fall, counted in", 11, gold, FontWeight.Normal);

        if (ChaosMeta.State.RunsCompleted == 2)
            AddResultLine("she set up a small corner in the toybox.", 11, dim, FontWeight.Normal);

        if (ChaosMeta.State.RunsCompleted >= 2 && ChaosMeta.NextGoal() is { } goal)
        {
            string line = goal.Affordable
                ? $"{ChaosGlyphs.Drops} ready: {goal.Name.ToUpperInvariant()} waits in the toybox"
                : goal.LessonId != null && ChaosLessons.ById(goal.LessonId) is { } lesson
                    ? $"next: {goal.Name.ToUpperInvariant()} — {lesson.Text} ({ChaosLessons.Progress(goal.LessonId)}/{lesson.Target})"
                    : $"{ChaosGlyphs.Drops} {goal.Cost - ChaosMeta.State.Sparks:N0} more until {goal.Name.ToUpperInvariant()}";
            AddResultLine(line, 11, goal.Affordable ? gold : dim, FontWeight.Normal);
        }

        BtnDollhouse.IsVisible = ChaosMeta.State.RunsCompleted >= 1;
        BtnAdjust.IsVisible = BtnDollhouse.IsVisible;

        try { AvaloniaChaosApp.Bark?.NotifyChaosResultsShown(score, ChaosMeta.State.BestScore, pbDelta, isPb, s.Defused, s.Detonated, s.BestCombo, s.Config.Difficulty.ToString()); } catch { }

        if (rankUp.HasValue) ScheduleRankCard(rankUp.Value);
    }

    private DispatcherTimer? _rankBeatTimer;
    private DispatcherTimer? _rankCardTimer;

    private void ScheduleRankCard(ChaosRank rank)
    {
        _rankBeatTimer?.Stop();
        _rankBeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
        _rankBeatTimer.Tick += (_, _) =>
        {
            _rankBeatTimer?.Stop();
            _rankBeatTimer = null;
            if (!ResultsPanel.IsVisible) return;
            ChaosAnnouncerOverlay.Announce("it noticed.", ChaosAnnounceKind.Temptation, artKey: "it_noticed");
            _rankCardTimer?.Stop();
            _rankCardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            _rankCardTimer.Tick += (_, _) =>
            {
                _rankCardTimer?.Stop();
                _rankCardTimer = null;
                ShowRankCard(rank);
            };
            _rankCardTimer?.Start();
        };
        _rankBeatTimer.Start();
    }

    private void ShowRankCard(ChaosRank rank)
    {
        if (!ResultsPanel.IsVisible) return;
        var card = new StackPanel { Margin = new Thickness(0, 16, 0, 0), Opacity = 0 };
        card.Children.Add(new TextBlock
        {
            Text = ChaosRanks.NameLower(rank), FontSize = 46, FontWeight = FontWeight.Bold, Foreground = AppBrush("TextLightBrush", _whiteFallback),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
        });
        card.Children.Add(new TextBlock
        {
            Text = ChaosRanks.Line(rank), FontSize = 12, Foreground = new SolidColorBrush(AppColor("TextMuted", Color.FromRgb(170, 170, 200))),
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        ResultsBody.Children.Add(card);
        new OpacityFade(card, 0, 1, 700);
        AvaloniaChaosSfx.Play("rank_settle", 0.6f);
        try { AvaloniaChaosApp.Bark?.NotifyChaosRankUp(ChaosRanks.NameLower(rank)); } catch { }
        ChaosMeta.State.LastRankSeen = (int)rank;
        ChaosMeta.Save();
        RevealService.Sync("rank_up");
    }

    private TextBlock AddResultLine(string text, double size, IBrush color, FontWeight weight)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, Foreground = color, FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3), TextWrapping = TextWrapping.Wrap
        };
        ResultsBody.Children.Add(tb);
        return tb;
    }

    private void AnimateScoreTally(TextBlock line, double score)
    {
        const int DURATION_MS = 800, FRAME_MS = 33, TICK_EVERY_MS = 90;
        if (score <= 0) { line.Text = $"score {score:N0}"; return; }

        int elapsed = 0, lastTick = -TICK_EVERY_MS;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
        timer.Tick += (_, _) =>
        {
            elapsed += FRAME_MS;
            if (elapsed >= DURATION_MS || !ResultsPanel.IsVisible)
            {
                timer.Stop();
                line.Text = $"score {score:N0}";
                return;
            }
            double p = elapsed / (double)DURATION_MS;
            double eased = 1 - Math.Pow(1 - p, 3);
            line.Text = $"score {score * eased:N0}";
            if (elapsed - lastTick >= TICK_EVERY_MS)
            {
                lastTick = elapsed;
                AvaloniaChaosSfx.Play("count_tick", 0.45f);
            }
        };
        timer.Start();
    }

    private void PopRewardChips(Grid row, int firstDelayMs)
    {
        int i = 0;
        foreach (var child in row.Children)
        {
            if (child is not Border chip) continue;
            int delay = firstDelayMs + i * 150;
            i++;

            var sc = new ScaleTransform(0.6, 0.6);
            chip.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            chip.RenderTransform = sc;
            chip.Opacity = 0;

            var opTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            opTimer.Tick += (_, _) =>
            {
                opTimer.Stop();
                new OpacityFade(chip, 0, 1, 180);
            };
            opTimer.Start();

            var scaleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
            double startMs = Environment.TickCount64 + delay;
            scaleTimer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (Environment.TickCount64 - startMs) / 320.0);
                double v = 0.6 + 0.4 * EaseOutBack(t);
                sc.ScaleX = sc.ScaleY = v;
                if (t >= 1) scaleTimer.Stop();
            };
            scaleTimer.Start();

            var cue = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay + 60) };
            cue.Tick += (_, _) =>
            {
                cue.Stop();
                if (ResultsPanel.IsVisible) AvaloniaChaosSfx.Play("chip_pop", 0.5f);
            };
            cue.Start();
        }
    }

    private static Grid ChipRow(params Border[] chips)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        for (int i = 0; i < chips.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            chips[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
            Grid.SetColumn(chips[i], i);
            row.Children.Add(chips[i]);
        }
        return row;
    }

    private static Border StatChip(string label, string value, IBrush? valueBrush = null, string? sub = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 138, 178)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 19, FontWeight = FontWeight.Bold,
            Foreground = valueBrush ?? AppBrush("TextLightBrush", _whiteFallback),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });
        if (sub != null)
            stack.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 148, 186)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 0x22, 0x1E, 0x3E)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = stack,
        };
    }

    private static string Roman(int n) => n switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => n.ToString()
    };

    private void BtnRunAgain_Click(object? sender, RoutedEventArgs e) => OnRunAgain?.Invoke();
    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();
    private void BtnDollhouse_Click(object? sender, RoutedEventArgs e) => OpenHubAt(null);
    private void BtnAdjust_Click(object? sender, RoutedEventArgs e) => OpenHubAt("run");

    private void OpenHubAt(string? tab)
    {
        Close();
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (AvaloniaChaosApp.Chaos == null || AvaloniaChaosApp.Chaos.IsRunning) return;

                if (_sharedHubWindow?.IsVisible != true)
                {
                    _sharedHubWindow?.Close();
                    _sharedHubWindow = new ChaosHubWindow();
                    _sharedHubWindow.Closed += (_, _) => _sharedHubWindow = null;
                    var owner = AvaloniaChaosApp.MainWindowRef;
                    if (owner is null)
                        _sharedHubWindow.Show();
                    else
                        _sharedHubWindow.Show(owner);
                }

                if (tab != null) _sharedHubWindow?.NavigateTo(tab);
                _sharedHubWindow?.Activate();
            }
            catch (Exception ex) { _logger?.LogWarning("Recap dollhouse door failed ({E})", ex.Message); }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Stop every 16ms/interval DispatcherTimer this window owns so none keep ticking against a
        // closed window (WPF stops these implicitly when the visual tree tears down).
        _countdownTimer?.Stop();
        _rankBeatTimer?.Stop();
        _rankCardTimer?.Stop();
        _revealTimer?.Stop();
        _autoResumeTimer?.Stop();
        _confirmTimer?.Stop();
        _bgPanTimer?.Stop();
        _storyAdvanceBounceTimer?.Stop();
        OnDismissed?.Invoke();
    }

    #endregion

    #region story card

    private List<ChaosConversationLine>? _storyLines;
    private int _storyIndex;
    private Action? _onConversationComplete;
    private bool _storyClosing;
    private DispatcherTimer? _bgPanTimer;
    /// <summary>16ms drift on the advance chevron. Stored so it is stopped on close / teardown
    /// (mirrors WPF killing StoryAdvanceT's animation in CloseConversation).</summary>
    private DispatcherTimer? _storyAdvanceBounceTimer;

    public void ShowConversation(ChaosConversation convo, IImage? backdrop, Action? onComplete)
    {
        if (convo == null || convo.Lines.Count == 0) { onComplete?.Invoke(); return; }
        _interactiveShownAt = Environment.TickCount64;
        _onConversationComplete = onComplete;
        _storyLines = convo.Lines;
        _storyIndex = 0;
        _storyClosing = false;

        StoryBg.Source = backdrop;
        StoryBg.IsVisible = backdrop != null;

        var portrait = ChaosArt.Resolve("portraits", convo.PortraitId);
        StoryPortrait.Source = portrait;
        StoryPortrait.IsVisible = portrait != null;
        StoryPortrait.HorizontalAlignment = convo.PortraitOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        double fromX = convo.PortraitOnLeft ? -260 : 260;

        StoryName.Text = SpeakerName(convo.Speaker);
        StoryTitle.Text = convo.Title ?? "";
        StoryTitle.IsVisible = !string.IsNullOrEmpty(convo.Title);

        SetClickThrough(false);
        CountdownBox.IsVisible = false;
        DraftPanel.IsVisible = false;
        ResultsPanel.IsVisible = false;
        Backdrop.IsVisible = false;
        StoryCardPanel.IsVisible = true;
        StoryCardPanel.Opacity = 0;
        new OpacityFade(StoryCardPanel, 0, 1, 180);
        BringToFront();

        if (portrait != null)
        {
            StoryPortrait.Opacity = 0;
            new OpacityFade(StoryPortrait, 0, 1, 220);
            _storyPortraitT.X = fromX;
            AnimateDouble(_storyPortraitT, TranslateTransform.XProperty, fromX, 0, 290, EaseOutBack);
        }

        _storyAdvanceBounceTimer?.Stop();
        var bounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double bounceStart = Environment.TickCount64;
        bounce.Tick += (_, _) =>
        {
            double t = (Environment.TickCount64 - bounceStart) / 520.0;
            _storyAdvanceT.X = (Math.Sin(t * Math.PI * 2) + 1) / 2 * 6;
        };
        _storyAdvanceBounceTimer = bounce;
        bounce.Start();

        StartBgPan();
        AvaloniaChaosSfx.Play("cards_in", 0.4f);
        ShowStoryLine(0);
    }

    private void ShowStoryLine(int i)
    {
        if (_storyLines == null || i >= _storyLines.Count) { CloseConversation(); return; }
        var line = _storyLines[i];
        StoryText.FontStyle = line.Emphasis ? FontStyle.Italic : FontStyle.Normal;
        StoryText.Text = line.Text;

        StoryBox.Opacity = 0;
        new OpacityFade(StoryBox, 0, 1, 150);
        _storyBoxScale.ScaleX = _storyBoxScale.ScaleY = 0.97;
        AnimateTransform(_storyBoxScale, 0.97, 1.0, 180, EaseOutBack);

        ChaosNarrator.PlayCardLine(line.AudioKey, line.Text);
    }

    private void StartBgPan()
    {
        _bgPanTimer?.Stop();
        _bgPanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        _bgPanTimer.Tick += (_, _) =>
        {
            double t = (Environment.TickCount64 - startMs) / 16000.0;
            double s = 1.08 + 0.08 * ((Math.Sin(t * Math.PI * 2) + 1) / 2);
            _storyBgScale.ScaleX = _storyBgScale.ScaleY = s;
            _storyBgT.X = Math.Sin(t * Math.PI * 2) * 26;
        };
        _bgPanTimer.Start();
    }

    private void StopBgPan()
    {
        _bgPanTimer?.Stop();
    }

    private void AdvanceStory()
    {
        if (!StoryCardPanel.IsVisible || _storyClosing) return;
        _storyIndex++;
        if (_storyLines == null || _storyIndex >= _storyLines.Count) { CloseConversation(); return; }
        AvaloniaChaosSfx.Play("ui_click", 0.3f);
        ShowStoryLine(_storyIndex);
    }

    private void CloseConversation()
    {
        if (_storyClosing) return;
        _storyClosing = true;
        _storyAdvanceBounceTimer?.Stop();
        StopBgPan();
        ChaosNarrator.EndCard();

        var fade = new OpacityFade(StoryCardPanel, 1, 0, 220, () =>
        {
            StoryCardPanel.IsVisible = false;
            StoryCardPanel.Opacity = 1;
            StoryBg.Source = null;
            StoryPortrait.Source = null;
            _storyLines = null;
            SetClickThrough(true);
            var cb = _onConversationComplete;
            _onConversationComplete = null;
            try { cb?.Invoke(); } catch (Exception ex) { _logger?.LogInformation("Story onComplete: {E}", ex.Message); }
        });
    }

    private void OnStoryKey(object? sender, KeyEventArgs e)
    {
        if (!StoryCardPanel.IsVisible) return;
        if (e.Key is Key.Space or Key.Enter or Key.Right)
        {
            e.Handled = true;
            AdvanceStory();
        }
    }

    private static string SpeakerName(ChaosSpeaker s) => s switch
    {
        ChaosSpeaker.Madam => "The Madam",
        ChaosSpeaker.Rabbit => "The Rabbit",
        ChaosSpeaker.Hatter => "The Hatter",
        ChaosSpeaker.Doll => "The Doll",
        ChaosSpeaker.Enemy => "???",
        _ => "",
    };

    #endregion

    #region helpers

    private void SetClickThrough(bool on)
    {
        _clickThrough = on;
        // Windows-level pass-through is handled by WS_EX_TRANSPARENT in ApplyExStyles.
        // Toggle Avalonia hit-testing so the overlay does not intercept bubble/gif clicks
        // while click-through, but interactive cards/panels can still receive pointer events.
        IsHitTestVisible = !on;
        Focusable = !on;
        ApplyExStyles();
    }

    private void BringToFront()
    {
        try
        {
            Topmost = false;
            Topmost = AvaloniaChaosWindowZ.BornTopmost;
        }
        catch { }
    }

    private void ApplyExStyles()
    {
        if (TryGetPlatformHandle() is not { } handle) return;
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var ex = GetWindowLong(handle.Handle, GWL_EXSTYLE);
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            if (_clickThrough) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(handle.Handle, GWL_EXSTYLE, ex);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("ChaosOverlayWindow.ApplyExStyles failed: {E}", ex.Message);
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    private static void AnimateTransform(Transform target, double from, double to, int ms, Func<double, double> ease)
    {
        if (target is ScaleTransform st) { st.ScaleX = st.ScaleY = from; }
        else if (target is TranslateTransform tt)
        {
            // caller sets property via AnimateDouble; this helper only for uniform scale.
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            double v = from + (to - from) * ease(t);
            if (target is ScaleTransform s) s.ScaleX = s.ScaleY = v;
            if (target is TranslateTransform tr) { }
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static void AnimateDouble(TranslateTransform target, AvaloniaProperty property, double from, double to, int ms, Func<double, double> ease)
    {
        target.X = from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t =
Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            target.X = from + (to - from) * ease(t);
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return t >= 1 ? 1 : 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static double EaseInQuad(double t) => t * t;

    private static Color Blend(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    #endregion

    private static readonly IBrush _whiteFallback = new SolidColorBrush(Colors.White);

    private static IBrush AppBrush(string key, IBrush fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is IBrush b)
            return b;
        return fallback;
    }

    private static Color AppColor(string key, Color fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
            return c;
        return fallback;
    }
}
