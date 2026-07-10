using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>Everything one unlock card renders: ribbon + accent, the item, and a context line.</summary>
public sealed class ChaosUnlockCardData
{
    public string Ribbon = "";                 // "NEW TOY UNLOCKED" — small accent header
    public Color Accent = Color.FromRgb(0xE8, 0x43, 0x93);
    public string Title = "";
    public string Desc = "";
    public string? Flavor;                     // grey italic line under the mechanics
    public string? Context;                    // "slipped into a pocket" / "equip it from the BAG"
    public string Glyph = "◈";                 // placeholder icon when no art resolves
    public ImageSource? Icon;
}

/// <summary>
/// Builders + the shared card visual for unlock announcements. The Hub shows these cards
/// inline (its <c>UnlockCardLayer</c>); mid-run lesson completions show them through
/// <see cref="ChaosUnlockCardOverlay"/> with the field paused underneath.
/// </summary>
public static class ChaosUnlockCards
{
    // Accents follow the announcer palette: toys mint, accessories warm gold, charms cyan.
    private static readonly Color ToyAccent      = Color.FromRgb(0x7A, 0xFF, 0xD2);
    private static readonly Color AccessoryAccent = Color.FromRgb(0xFF, 0xD2, 0x7A);
    private static readonly Color CharmAccent    = Color.FromRgb(0x7A, 0xE0, 0xFF);
    private static readonly Color HabitAccent    = Color.FromRgb(0x9C, 0xE8, 0xA0);
    private static readonly Color PocketAccent   = Color.FromRgb(0xE8, 0x43, 0x93);
    private static readonly Color CapstoneAccent = Color.FromRgb(0xFF, 0xC8, 0x3C);

    private static (string ribbon, Color accent) ByCategory(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Skill     => ("NEW TOY UNLOCKED", ToyAccent),
        ChaosBoonCategory.Accessory => ("NEW ACCESSORY UNLOCKED", AccessoryAccent),
        _                           => ("NEW CHARM UNLOCKED", CharmAccent),
    };

    /// <summary>A lifetime boon's level 1 just bought (call AFTER the unlock so the
    /// auto-equip state reads true). Null when the id is unknown.</summary>
    public static ChaosUnlockCardData? ForBoonUnlock(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        var (ribbon, accent) = ByCategory(b.Category);
        string context =
            b.Category == ChaosBoonCategory.Utility ? "switched on — it works every descent."
            : ChaosMeta.IsBoonActive(id)            ? "slipped straight into a pocket — it rides with you next descent."
            : ChaosMeta.SlotsFor(b.Category) == 0   ? "no pocket to carry it yet — she sells one at her bench."
            :                                         "your pockets are full — swap it in from the BAG.";
        return new ChaosUnlockCardData
        {
            Ribbon = ribbon, Accent = accent, Title = b.Name, Desc = b.Desc,
            Flavor = b.Flavor, Context = context, Glyph = b.Glyph,
            Icon = ChaosArt.Resolve("boons", id),
        };
    }

    /// <summary>A boon just deepened to its final level — the card shows what the capstone adds.
    /// Null when there's no capstone text (nothing new to explain).</summary>
    public static ChaosUnlockCardData? ForCapstone(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null || string.IsNullOrWhiteSpace(b.CapstoneDesc)) return null;
        return new ChaosUnlockCardData
        {
            Ribbon = "CAPSTONE REACHED", Accent = CapstoneAccent, Title = b.Name,
            Desc = b.CapstoneDesc, Flavor = b.Flavor,
            Context = "fully deepened — its final gift is yours.",
            Glyph = b.Glyph, Icon = ChaosArt.Resolve("boons", id),
        };
    }

    /// <summary>A trained habit just bought.</summary>
    public static ChaosUnlockCardData? ForHabit(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return null;
        return new ChaosUnlockCardData
        {
            Ribbon = "HABIT TRAINED", Accent = HabitAccent, Title = u.Name, Desc = u.Desc,
            Flavor = u.Flavor,
            Context = "always on from your next descent — switch it off in the toybox anytime.",
            Glyph = u.Glyph,
            Icon = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", id),
        };
    }

    /// <summary>A pocket just sewn at her bench — the card explains what the slot does.</summary>
    public static ChaosUnlockCardData ForPocket(bool isToy, string label, string line)
    {
        int n = isToy ? ChaosMeta.State.ToyPockets : ChaosMeta.State.AccessoryPockets;
        string kind = isToy ? "toy" : "accessory";
        string desc = n == 1
            ? $"you can now carry one {kind} into the descent. unlocked {kind}s equip from the BAG."
            : $"you can now carry {n} {kind}s into the descent at once. pick yours from the BAG.";
        return new ChaosUnlockCardData
        {
            Ribbon = "POCKET SEWN", Accent = PocketAccent, Title = label, Desc = desc,
            Flavor = line, Glyph = "👝",
        };
    }

    /// <summary>A lesson just completed mid-run: the gated item is now buyable. The id is the
    /// purchasable's id — a lifetime boon or a trained habit. Null when neither resolves.</summary>
    public static ChaosUnlockCardData? ForLesson(string id)
    {
        const string context = "now for sale in the toybox — emotes will do the rest.";
        var b = ChaosLifetimeBoons.ById(id);
        if (b != null)
        {
            var (_, accent) = ByCategory(b.Category);
            return new ChaosUnlockCardData
            {
                Ribbon = "LESSON LEARNED", Accent = accent, Title = b.Name, Desc = b.Desc,
                Flavor = b.Flavor, Context = context, Glyph = b.Glyph,
                Icon = ChaosArt.Resolve("boons", id),
            };
        }
        var u = ChaosUpgrades.ById(id);
        if (u != null)
        {
            return new ChaosUnlockCardData
            {
                Ribbon = "LESSON LEARNED", Accent = HabitAccent, Title = u.Name, Desc = u.Desc,
                Flavor = u.Flavor, Context = context, Glyph = u.Glyph,
                Icon = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", id),
            };
        }
        return null;
    }

    /// <summary>The reward cue for a card, by ribbon: pockets stitch, capstones bloom
    /// deeper, everything else gets the soft unlock shimmer.</summary>
    private static (string cue, float vol) CueFor(ChaosUnlockCardData d) => d.Ribbon switch
    {
        "CAPSTONE REACHED" => ("capstone_reached", 0.7f),
        "POCKET SEWN"      => ("pocket_sewn", 0.7f),
        _                  => ("unlock_card", 0.65f),
    };

    /// <summary>The card itself — shared by the Hub layer and the mid-run overlay.</summary>
    public static Border BuildCardVisual(ChaosUnlockCardData d, double width = 400)
    {
        var accent = new SolidColorBrush(d.Accent); accent.Freeze();
        var accentDim = new SolidColorBrush(Color.FromArgb(0xCC, d.Accent.R, d.Accent.G, d.Accent.B)); accentDim.Freeze();

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = d.Ribbon,
            Foreground = accent,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        FrameworkElement icon;
        if (d.Icon != null)
            icon = new Image { Source = d.Icon, Width = 64, Height = 64, Stretch = Stretch.Uniform };
        else
            icon = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x28, d.Accent.R, d.Accent.G, d.Accent.B)),
                Child = new TextBlock
                {
                    Text = d.Glyph, FontSize = 30, Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        icon.VerticalAlignment = VerticalAlignment.Top;
        var iconScale = new ScaleTransform(0.4, 0.4);
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = iconScale;
        row.Children.Add(icon);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = d.Title, Foreground = Brushes.White,
            FontWeight = FontWeights.Bold, FontSize = 17, TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = d.Desc, Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xEE)),
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        if (!string.IsNullOrWhiteSpace(d.Flavor))
            text.Children.Add(new TextBlock
            {
                Text = d.Flavor, Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0)),
                FontStyle = FontStyles.Italic, FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0),
            });
        if (!string.IsNullOrWhiteSpace(d.Context))
            text.Children.Add(new TextBlock
            {
                Text = "→ " + d.Context, Foreground = accentDim, FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0),
            });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        stack.Children.Add(row);

        var glow = new DropShadowEffect { Color = d.Accent, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.5 };
        var border = new Border
        {
            Width = width,
            Child = stack,
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1C, 0x1A, 0x36)),
            BorderBrush = accentDim,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14, 18, 16),
            Effect = glow,
        };

        // The dopamine beat, fired once when the card actually hits the screen (both the
        // hub layer and the mid-run overlay route through here): the reward cue, one
        // accent-glow flare, and the icon popping in a breath after the card body.
        bool played = false;
        border.Loaded += (_, _) =>
        {
            if (played) return;
            played = true;
            try
            {
                var (cue, vol) = CueFor(d);
                ChaosSfx.Play(cue, vol);

                var flare = new DoubleAnimation(24, 46, TimeSpan.FromMilliseconds(380))
                { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, flare);
                var flareOp = new DoubleAnimation(0.5, 0.9, TimeSpan.FromMilliseconds(380))
                { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, flareOp);

                var pop = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(380))
                {
                    BeginTime = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut },
                };
                iconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                iconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            catch (Exception ex) { App.Logger?.Debug("Unlock card flair failed: {E}", ex.Message); }
        };
        return border;
    }
}

/// <summary>
/// Mid-run unlock card: a dim scrim over the whole screen with the card centered and a
/// "click to continue" hint — shown while <c>ChaosModeService</c> holds the field paused,
/// so the player can actually read what they just earned. Click (anywhere) dismisses;
/// queued cards play in sequence and the pause lifts after the last one. Cards that are
/// NOT holding a pause (over the recap, over the pause menu) also auto-dismiss on a timer
/// so the scrim never lingers — but a pause-holding card waits for the click: timing out
/// would resume the run exactly when the player ISN'T there to see it.
/// ONE window is created on first use and KEPT ALIVE between cards (creating/closing a
/// layered window mid-run can wedge the shared WPF render thread — see
/// ChaosAnnouncerOverlay). Closed only at run teardown via <see cref="CloseActive"/>.
/// Unlike the announcer this window IS hit-testable while visible — it must catch the
/// dismiss click — which is safe only because the field underneath is frozen + input-locked.
/// </summary>
public sealed class ChaosUnlockCardOverlay : Window
{
    private const int IN_MS = 160;
    private const int OUT_MS = 150;
    private const int AUTO_DISMISS_MS = 12000;   // pause-free cards only: don't let the scrim linger

    private static ChaosUnlockCardOverlay? _active;
    private static readonly Queue<(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)> _queue = new();
    private static bool _showing;

    /// <summary>True while a card is up or queued — the service resumes only when this clears.</summary>
    public static bool IsShowing => _showing || _queue.Count > 0;

    /// <summary>Queue a card. <paramref name="onDismissed"/> fires on the UI thread after
    /// THIS card leaves — the service uses it to lift the pause. Pass
    /// <paramref name="autoDismiss"/> = false when the card holds the field paused: only a
    /// click may dismiss it (a timeout would resume the run with nobody watching).</summary>
    public static void Show(ChaosUnlockCardData data, Action? onDismissed = null, bool autoDismiss = true)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Enqueue((data, onDismissed, autoDismiss));
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosUnlockCard.Show: {E}", ex.Message); }
    }

    /// <summary>Drop queued/visible cards and tear the window down (run teardown). Pending
    /// dismiss callbacks are dropped too — teardown resets the pause flags itself.</summary>
    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (_queue.Count == 0) { _showing = false; return; }
        _showing = true;
        var (data, onDismissed, autoDismiss) = _queue.Dequeue();
        try
        {
            if (_active == null) { _active = new ChaosUnlockCardOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }
            _active.Display(data, onDismissed, autoDismiss);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosUnlockCard.ShowNext: {E}", ex.Message);
            _showing = false;
            try { onDismissed?.Invoke(); } catch { }
        }
    }

    private readonly Grid _host;
    private readonly DispatcherTimer _auto;
    private Action? _onDismissed;
    private bool _dismissing;
    private (ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)? _pending;   // first Display can land before Loaded

    private ChaosUnlockCardOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;   // non-topmost during a Free Desktop run; topmost at the hub
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Opacity = 0;

        // Scrim carries the hit-test (a fully transparent surface wouldn't take the click).
        _host = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)) };
        Content = _host;
        MouseLeftButtonDown += (_, _) => Dismiss();

        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.data, p.onDismissed, p.autoDismiss); }
        };

        _auto = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AUTO_DISMISS_MS) };
        _auto.Tick += (_, _) => Dismiss();
    }

    private void Display(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)
    {
        if (!IsLoaded) { _pending = (data, onDismissed, autoDismiss); return; }
        DisplayCore(data, onDismissed, autoDismiss);
    }

    private void DisplayCore(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)
    {
        _auto.Stop();
        _onDismissed = onDismissed;
        _dismissing = false;

        var card = ChaosUnlockCards.BuildCardVisual(data, 420);
        var hint = new TextBlock
        {
            Text = "click to continue",
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(card);
        panel.Children.Add(hint);

        // Center on the PRIMARY work area (this window spans the whole virtual screen).
        var wa = SystemParameters.WorkArea;
        var area = new Grid
        {
            Width = wa.Width,
            Height = wa.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(wa.Left - SystemParameters.VirtualScreenLeft,
                                   wa.Top - SystemParameters.VirtualScreenTop, 0, 0),
        };
        area.Children.Add(panel);

        var scale = new ScaleTransform(0.9, 0.9);
        panel.RenderTransformOrigin = new Point(0.5, 0.5);
        panel.RenderTransform = scale;

        _host.Children.Clear();
        _host.Children.Add(area);

        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 1, TimeSpan.FromMilliseconds(IN_MS)));
        var pop = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(IN_MS + 80))
        { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        if (autoDismiss) _auto.Start();   // pause-holding cards wait for the click
    }

    private void Dismiss()
    {
        if (_dismissing || !_showing) return;
        _dismissing = true;
        _auto.Stop();
        var done = _onDismissed; _onDismissed = null;

        // Next card queued → swap content directly (no fade-out/in churn between cards).
        if (_queue.Count > 0)
        {
            try { done?.Invoke(); } catch { }
            ShowNext();
            return;
        }

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
        fade.Completed += (_, _) =>
        {
            try { _host.Children.Clear(); } catch { }
            _showing = false;
            try { Hide(); } catch { }   // idle full-screen layered surface taxes DWM — hide between cards
            try { done?.Invoke(); } catch { }
            if (_queue.Count > 0) ShowNext();   // a card landed during the fade
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void CloseNow()
    {
        try { _auto.Stop(); } catch { }
        try { _host.Children.Clear(); } catch { }
        _onDismissed = null;
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private void ApplyExStyles()
    {
        try
        {
            // TOOLWINDOW + NOACTIVATE but NOT TRANSPARENT — this overlay must take the click.
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
