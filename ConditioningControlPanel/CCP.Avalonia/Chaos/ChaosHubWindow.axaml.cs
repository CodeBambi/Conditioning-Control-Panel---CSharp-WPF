using System;
using System.Collections.Generic;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using ChaosNarrativeContext = ConditioningControlPanel.Core.Services.Chaos.ChaosNarrativeContext;
namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosHubWindow : Window
{
    private readonly ILogger<ChaosHubWindow> _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;
    private readonly global::ConditioningControlPanel.Core.Platform.IAudioPlayer _audioPlayer;

    private static readonly Random _rng = new();
    private int _waves = 5;
    private bool _uiSoundsReady;
    private bool _fallingIn;
    private bool _diffAutoClamped;
    private ChaosOverlayWindow? _greetingOverlay;

    // Menu soundtrack state
    private const double MenuMusicVol = 0.5;
    private string? _musicPath;
    private DispatcherTimer? _musicFade;
    private double _fadeFrom, _fadeTo;
    private int _fadeStep, _fadeSteps;
    private Action? _fadeDone;
    private double _lastMusicVol;

    public static ChaosHubWindow? Current { get; private set; }

    public ChaosHubWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ChaosHubWindow>>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
        _audioPlayer = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Platform.IAudioPlayer>();
        _musicPath = AvaloniaChaosSfx.ResolvePath("menu_theme");
        UpdateMuteIcon(_settings.Current?.ChaosMenuMusicMuted == true);
Current = this;
        AddHandler(Button.ClickEvent, (s, e) => { if (_uiSoundsReady) AvaloniaChaosSfx.Play("ui_click", 0.3f); }, RoutingStrategies.Bubble);
        Closed += (_, _) =>
        {
            Current = null;
            DisposeMenuMusic();
            MenuArtBox?.Stop();
            AvaloniaChaosApp.Chaos?.CloseLoadoutSidebar();
            if (!_fallingIn) AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
            try { _greetingOverlay?.Close(); } catch { }
            _greetingOverlay = null;
        };

        LoadFromSettings();
        InitRevealMap();
        BuildHabits();
        BuildLifetimeBoons();
        BuildLoadoutTiles();
        BuildBench();
        BuildMantras();
        BuildDiary();
        RefreshTopBar();
        RefreshStats();
        ApplyUnlocks();
        ApplyReveals();
        LoadBanner();
        BuildDebugStrip();
        ShowTab("loadout");
        Opened += (_, _) =>
        {
            OnHubOpenedReveals();
            FireHubGreeting();
            ShowMenuView();
        };
        _uiSoundsReady = true;
    }

    private void FireHubGreeting()
    {
        try
        {
            if (!AvaloniaChaosMode.NarrativeActive) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (Current != this) return;
                _greetingOverlay = ChaosNarrativeHooks.OnHubMoment("hub_return", new ChaosNarrativeContext { RankIndex = ChaosMeta.RankIndex });
            };
            t.Start();
        }
        catch (Exception ex) { _logger?.LogInformation("ChaosHub.FireHubGreeting: {E}", ex.Message); }
    }

    #region Menu soundtrack

    private void StartMenuMusic()
    {
        try
        {
            if (string.IsNullOrEmpty(_musicPath) || !File.Exists(_musicPath)) return;
            bool muted = _settings.Current?.ChaosMenuMusicMuted == true;
            UpdateMuteIcon(muted);
            _audioPlayer.SetVolume(muted ? 0.0 : MenuMusicVol);
            _audioPlayer.PlayLoopAsync(_musicPath);
            FadeMusicTo(muted ? 0.0 : MenuMusicVol, 2.0);
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosHub.StartMenuMusic: {E}", ex.Message); }
    }

    private void StopMenuMusic()
    {
        try
        {
            FadeMusicTo(0.0, 2.0, () => { try { _audioPlayer.Stop(); } catch { } });
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosHub.StopMenuMusic: {E}", ex.Message); }
    }

    private void DisposeMenuMusic()
    {
        try { _musicFade?.Stop(); } catch { }
        try { _audioPlayer.Stop(); } catch { }
    }

    private void FadeMusicTo(double target, double secs, Action? onDone = null)
    {
        _fadeFrom = _lastMusicVol;
        _fadeTo = target;
        _fadeSteps = Math.Max(1, (int)(secs / 0.05));
        _fadeStep = 0;
        _fadeDone = onDone;
        if (_musicFade == null)
        {
            _musicFade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _musicFade.Tick += MusicFadeTick;
        }
        _musicFade.Start();
    }

    private void MusicFadeTick(object? sender, EventArgs e)
    {
        _fadeStep++;
        double t = Math.Min(1.0, (double)_fadeStep / _fadeSteps);
        double vol = Math.Clamp(_fadeFrom + (_fadeTo - _fadeFrom) * t, 0, 1);
        _lastMusicVol = vol;
        try { _audioPlayer.SetVolume(vol); }
        catch { _musicFade?.Stop(); return; }
        if (t >= 1.0)
        {
            _musicFade?.Stop();
            var d = _fadeDone; _fadeDone = null; d?.Invoke();
        }
    }

    private void UpdateMuteIcon(bool muted)
    {
        if (BtnMenuMute != null) BtnMenuMute.Content = muted ? "🔇" : "🔊";
        if (MenuMuteIcon != null) MenuMuteIcon.Text = muted ? "🔇" : "🔊";
    }

    private void BtnMenuMute_Click(object? sender, RoutedEventArgs e)
    {
        bool muted = !(_settings.Current?.ChaosMenuMusicMuted == true);
        if (_settings.Current != null) _settings.Current.ChaosMenuMusicMuted = muted;
        UpdateMuteIcon(muted);
        FadeMusicTo(muted ? 0.0 : MenuMusicVol, 0.6);
    }

    #endregion

    private void LoadBanner()
    {
        var src = ChaosArt.ResolveBanner();
        if (src != null) { BannerImage.Source = src; BannerImage.IsVisible = true; }
        var bd = ChaosArt.Resolve("hub", "backdrop");
        if (bd != null) { HubBackdrop.Source = bd; HubBackdrop.IsVisible = true; }
    }

    private void ApplyUnlocks()
    {
        TabLoadout.IsEnabled = true;
        TabEnhance.IsEnabled = true;
        TabRun.IsEnabled = true;
        TabImprove.IsEnabled = true;
        TabDiary.IsVisible = DiaryUnlocked;
    }

    private static bool DiaryUnlocked => RevealService.IsUnlocked(RevealIds.Diary);

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton tb && tb.IsEnabled == true) ShowTab(tb.Tag?.ToString() ?? "loadout");
        else if (sender is ToggleButton tb2) tb2.IsChecked = false;
    }

    public void ShowTab(string tag)
    {
        if (tag == "habits") tag = "enhance";
        if (tag == "improve" && TabImprove.IsEnabled != true) tag = "loadout";
        if (tag == "diary" && !DiaryUnlocked) tag = "loadout";

        PanelLoadout.IsVisible = tag == "loadout";
        PanelEnhance.IsVisible = tag == "enhance";
        PanelRun.IsVisible = tag == "run";
        PanelImprove.IsVisible = tag == "improve";
        PanelDiary.IsVisible = tag == "diary";

        TabLoadout.IsChecked = tag == "loadout";
        TabEnhance.IsChecked = tag == "enhance";
        TabRun.IsChecked = tag == "run";
        TabImprove.IsChecked = tag == "improve";
        TabDiary.IsChecked = tag == "diary";

        TxtHint.Text = tag switch
        {
            "loadout" => "click a tile to slip it into a pocket. + takes you where it's sold.",
            "enhance" => "spend your drops. deepen what you like.",
            "run" => "dress up the fall, then FALL IN.",
            "improve" => "the bench, the mantras, how far you've fallen.",
            "diary" => "everything you've met down there. click an entry to pop it out.",
            _ => "",
        };
    }

    public void NavigateTo(string tag)
    {
        ShowTab(tag);
        try { Activate(); } catch { }
    }

    private void TestingToggle_Click(object? sender, RoutedEventArgs e)
    {
        bool open = TglTesting.IsChecked == true;
        TestingBody.IsVisible = open;
        TglTesting.Content = open ? "🧪 testing options ▾" : "🧪 testing options ▸";
    }

    #region top bar / stats

    private int _shownSparks = -1, _shownGold = -1;

    private void RefreshTopBar()
    {
        AnimateBalance(TxtSparks, _shownSparks, ChaosMeta.State.Sparks);
        AnimateBalance(TxtGold, _shownGold, ChaosMeta.State.Gold);
        _shownSparks = ChaosMeta.State.Sparks;
        _shownGold = ChaosMeta.State.Gold;
        TxtRank.Text = ChaosMeta.Rank;
        RefreshTabBadges();
        OnRefreshTopBarPartial();
    }

    partial void OnRefreshTopBarPartial();

    private void RefreshTabBadges()
    {
        SetTabBadge(TabEnhance, "the Toybox", CountAffordableToybox());
        SetTabBadge(TabImprove, "the Looking Glass", TabImprove.IsEnabled == true ? CountAffordableBench() : 0);
    }

    private static void SetTabBadge(ToggleButton tab, string label, int count)
    {
        if (count <= 0)
        {
            tab.Content = label;
            ToolTip.SetTip(tab, null);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new Border
        {
            Background = AppBrush("PinkBrush", Brushes.HotPink),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = count.ToString(), Foreground = AppBrush("TextLightBrush", _whiteFallback), FontSize = 10.5, FontWeight = FontWeight.Bold },
        });
        tab.Content = row;
        ToolTip.SetTip(tab, count == 1 ? "1 thing you can afford right now" : $"{count} things you can afford right now");
    }

    private static int CountAffordableToybox()
    {
        int count = 0;
        foreach (var u in ChaosUpgrades.All)
        {
            if (!OnShelfNow(u.Id)) continue;
            if (ChaosMeta.IsOwned(u.Id)) continue;
            if (ChaosMeta.IsPurchaseRankLocked(u.Id)) continue;
            if (ChaosMeta.CanAfford(u.Id)) count++;
        }
        foreach (var b in ChaosLifetimeBoons.All)
        {
            int level = ChaosMeta.BoonLevel(b.Id);
            bool unlocked = level >= 1;
            if (!unlocked)
            {
                if (ChaosMeta.IsBoonRankLocked(b.Id)) continue;
                if (ChaosMeta.IsAccessoryScriptLocked(b.Id)) continue;
                if (ChaosMeta.CanAffordUnlock(b.Id)) count++;
            }
            else if (level < b.MaxLevel && ChaosMeta.CanAffordUpgrade(b.Id))
            {
                count++;
            }
        }
        return count;
    }

    private int CountAffordableBench()
    {
        int count = 0;
        foreach (var item in BenchItems)
        {
            if (ChaosMeta.State.BenchPurchases.Contains(item.Id)) continue;
            if (item.RankNeed.HasValue && !ChaosMeta.AtLeast(item.RankNeed.Value)) continue;
            if (item.RevealGate != null && !RevealService.IsUnlocked(item.RevealGate)) continue;
            if (ChaosMeta.State.Gold >= item.Cost) count++;
        }
        return count;
    }

    private static void AnimateBalance(TextBlock tb, int from, int to)
    {
        if (from < 0 || from == to)
        {
            tb.Text = to.ToString("N0");
            return;
        }
        const int DURATION_MS = 500, FRAME_MS = 33, TICK_EVERY_MS = 90;
        int elapsed = 0, lastTick = -TICK_EVERY_MS;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FRAME_MS) };
        timer.Tick += (_, _) =>
        {
            elapsed += FRAME_MS;
            if (elapsed >= DURATION_MS)
            {
                timer.Stop();
                tb.Text = to.ToString("N0");
                return;
            }
            double eased = 1 - Math.Pow(1 - elapsed / (double)DURATION_MS, 3);
            tb.Text = ((int)Math.Round(from + (to - from) * eased)).ToString("N0");
            if (elapsed - lastTick >= TICK_EVERY_MS)
            {
                lastTick = elapsed;
                AvaloniaChaosSfx.Play("count_tick", 0.4f);
            }
        };
        timer.Start();
    }

    private void RefreshStats()
    {
        var s = ChaosMeta.State;
        StSparks.Text = s.Sparks.ToString("N0");
        StRuns.Text = s.RunsCompleted.ToString("N0");
        StTimeUnder.Text = FormatPlaytime(s.TotalRunSeconds);
        StBestScore.Text = s.BestScore.ToString("N0");
        StBestCombo.Text = s.BestCombo.ToString("N0");
        StDefused.Text = s.TotalDefused.ToString("N0");
        StTimeHeld.Text = FormatPlaytime(s.TotalChannelSeconds);
    }

    private static string FormatPlaytime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
    }

    #endregion

    #region run-setup load / save

    private void LoadFromSettings()
    {
        var s = _settings?.Current;
        if (s == null) { LoadDefaults(); ApplyExtremeGate(); return; }

        SetSegment(GrpDifficulty, s.ChaosDifficulty);
        SetSegment(GrpLength, s.ChaosRunDurationSec.ToString());
        SetSegment(GrpMotion, s.ChaosMotionMode);
        _waves = s.ChaosWaveCount; TxtWaves.Text = _waves.ToString();

        var enabled = s.ChaosEnabledVariants;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = enabled == null || enabled.Contains(t.Tag?.ToString() ?? "");

        ChkShake.IsChecked = s.ChaosScreenShakeEnabled;
        SldShake.Value = s.ChaosShakeIntensity;
        ChkFlashes.IsChecked = s.ChaosColorFlashesEnabled;
        ChkSkiaFx.IsChecked = s.ChaosSkiaFxEnabled;
        ChkPinTop.IsChecked = s.ChaosPinOnTop;
        SldEffect.Value = s.ChaosEffectIntensity;
        ChkBoonDraft.IsChecked = s.ChaosBoonDraftEnabled;
        ChkCurses.IsChecked = s.ChaosAllowCurses;
        ChkDarters.IsChecked = s.ChaosDartersEnabled;
        ChkAnnouncer.IsChecked = s.ChaosAnnouncerEnabled;
        ChkNarrative.IsChecked = s.NarrativeModeEnabled;
        ChkBackdrop.IsChecked = s.BackdropEnabled;
        SldBackdropOpacity.Value = s.BackdropOpacity;

        var keyOpts = new[] { "Q", "E", "R", "F", "Z", "X", "C", "V", "1", "2", "3", "4" };
        CmbAccKey1.ItemsSource = keyOpts;
        CmbAccKey2.ItemsSource = keyOpts;
        CmbAccKey1.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey1) ? s.ChaosAccessoryKey1 : "Q";
        CmbAccKey2.SelectedItem = keyOpts.Contains(s.ChaosAccessoryKey2) ? s.ChaosAccessoryKey2 : "E";

        ApplyExtremeGate();
    }

    private void ApplyExtremeGate()
    {
        bool unlocked = ChaosMeta.State.ExtremeUnlocked;
        SegExtreme.IsEnabled = unlocked;
        SegExtreme.Content = unlocked ? "Inescapable" : "Inescapable 🔒";
        if (!unlocked)
        {
            int cost = ChaosUpgrades.ById("extreme_tier")?.Cost ?? 0;
            ToolTip.SetTip(SegExtreme, "a deeper door. she sells the key in the Toybox: "
                + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for ✦{cost}.");
        }
        if (!unlocked && SegExtreme.IsChecked == true)
            SetSegment(GrpDifficulty, "Hard");
    }

    private void LoadDefaults()
    {
        SetSegment(GrpDifficulty, "Easy");
        SetSegment(GrpLength, "180");
        SetSegment(GrpMotion, "Mixed");
        _waves = 5; TxtWaves.Text = "5";
        foreach (var t in GrpPool.Children.OfType<ToggleButton>()) t.IsChecked = true;
        ChkShake.IsChecked = true; SldShake.Value = 0.8;
        ChkFlashes.IsChecked = true; ChkSkiaFx.IsChecked = true;
        ChkPinTop.IsChecked = true;
        SldEffect.Value = 0.85;
        ChkBoonDraft.IsChecked = true; ChkCurses.IsChecked = true;
        ChkDarters.IsChecked = true;
        ChkAnnouncer.IsChecked = true;
        ChkNarrative.IsChecked = true;
        ChkBackdrop.IsChecked = true;
        SldBackdropOpacity.Value = 0.55;
    }

    private void SaveToSettings()
    {
        var s = _settings?.Current;
        if (s == null) return;

        if (!_diffAutoClamped) s.ChaosDifficulty = GetSegment(GrpDifficulty) ?? "Easy";
        if (int.TryParse(GetSegment(GrpLength), out var len)) s.ChaosRunDurationSec = len;
        s.ChaosMotionMode = GetSegment(GrpMotion) ?? "Mixed";
        s.ChaosWaveCount = _waves;

        var checkd = GrpPool.Children.OfType<ToggleButton>()
            .Where(t => t.IsChecked == true).Select(t => t.Tag?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        s.ChaosEnabledVariants = (checkd.Count == 0 || checkd.Count == GrpPool.Children.OfType<ToggleButton>().Count())
            ? null : checkd;

        s.ChaosScreenShakeEnabled = ChkShake.IsChecked == true;
        s.ChaosShakeIntensity = SldShake.Value;
        s.ChaosColorFlashesEnabled = ChkFlashes.IsChecked == true;
        s.ChaosSkiaFxEnabled = ChkSkiaFx.IsChecked == true;
        s.ChaosPinOnTop = ChkPinTop.IsChecked == true;
        s.ChaosEffectIntensity = SldEffect.Value;
        s.ChaosBoonDraftEnabled = ChkBoonDraft.IsChecked == true;
        s.ChaosAllowCurses = ChkCurses.IsChecked == true;
        s.ChaosDartersEnabled = ChkDarters.IsChecked == true;
        s.ChaosAnnouncerEnabled = ChkAnnouncer.IsChecked == true;
        s.NarrativeModeEnabled = ChkNarrative.IsChecked == true;
        s.BackdropEnabled = ChkBackdrop.IsChecked == true;
        s.BackdropOpacity = SldBackdropOpacity.Value;
    }

    private void Segment_Click(object? sender, RoutedEventArgs e)
    {
        var btn = (ToggleButton)sender!;
        if (btn.IsEnabled != true) { btn.IsChecked = false; return; }
        var grp = (Panel)btn.Parent!;
        foreach (var t in grp.Children.OfType<ToggleButton>()) t.IsChecked = ReferenceEquals(t, btn);
        if (ReferenceEquals(grp, GrpDifficulty)) _diffAutoClamped = false;
    }

    private void Stepper_Click(object? sender, RoutedEventArgs e)
    {
        switch ((sender as Button)?.Tag?.ToString())
        {
            case "waves-": _waves = Math.Max(1, _waves - 1); TxtWaves.Text = _waves.ToString(); break;
            case "waves+": _waves = Math.Min(12, _waves + 1); TxtWaves.Text = _waves.ToString(); break;
        }
    }

    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        var name = (sender as Button)?.Tag?.ToString();
        var preset = ChaosBubbleVariants.Presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        foreach (var t in GrpPool.Children.OfType<ToggleButton>())
            t.IsChecked = preset.VariantIds.Contains(t.Tag?.ToString() ?? "");
    }

    private void BtnRandomize_Click(object? sender, RoutedEventArgs e)
    {
        var diffs = new List<string> { "Easy" };
        if (SegMedium.IsVisible) diffs.Add("Medium");
        if (SegHard.IsVisible) diffs.Add("Hard");
        if (ChaosMeta.State.ExtremeUnlocked) diffs.Add("Extreme");
        SetSegment(GrpDifficulty, diffs[_rng.Next(diffs.Count)]);
        SetSegment(GrpLength, new[] { "120", "180", "300" }[_rng.Next(3)]);
        SetSegment(GrpMotion, new[] { "Mixed", "FloatUp", "RainDown", "RoamBounce" }[_rng.Next(4)]);
        var pool = GrpPool.Children.OfType<ToggleButton>().ToList();
        foreach (var t in pool) t.IsChecked = _rng.NextDouble() < 0.6;
        if (!pool.Any(t => t.IsChecked == true)) pool[0].IsChecked = true;
    }

    private void BtnDefaults_Click(object? sender, RoutedEventArgs e) { LoadDefaults(); ApplyExtremeGate(); }

    private void AccKey_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var s = _settings?.Current;
        if (s == null) return;
        if (CmbAccKey1?.SelectedItem is string k1) s.ChaosAccessoryKey1 = k1;
        if (CmbAccKey2?.SelectedItem is string k2) s.ChaosAccessoryKey2 = k2;
    }

    private void BtnBegin_Click(object? sender, RoutedEventArgs e)
    {
        var mode = (sender as Control)?.Tag?.ToString() == "FreeDesktop"
            ? ChaosPlayMode.FreeDesktop
            : ChaosPlayMode.Story;
        _fallingIn = true;
        SaveToSettings();
        Close();
        var cfg = ChaosRunConfig.FromSettings();
        cfg.PlayMode = mode;
        Dispatcher.UIThread.Post(() => AvaloniaChaosApp.Chaos?.StartRun(cfg));
    }

    public void FallIn() => BtnBegin_Click(this, new RoutedEventArgs());

    private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void BtnGuide_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var intro =
new ChaosIntroWindow();
            intro.ShowDialog(this);
        }
        catch (Exception ex) { _logger?.LogInformation("Chaos guide reshow: {E}", ex.Message); }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private static string? GetSegment(Panel grp) =>
        grp.Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true)?.Tag?.ToString();

    private static void SetSegment(Panel grp, string? tag)
    {
        foreach (var t in grp.Children.OfType<ToggleButton>())
            t.IsChecked = (t.Tag?.ToString() == tag);
    }

    private static IBrush AppBrush(string key, IBrush fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is IBrush b)
            return b;
        return fallback;
    }

    private static readonly IBrush _whiteFallback = new SolidColorBrush(Colors.White);

    private static Color AppColor(string key, Color fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
            return c;
        return fallback;
    }

    #endregion
}
