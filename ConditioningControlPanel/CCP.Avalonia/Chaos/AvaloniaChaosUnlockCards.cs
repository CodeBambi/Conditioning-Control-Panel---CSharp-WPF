using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>The three lifetime-boon shelves. Mirrors WPF ChaosBoonCategory.</summary>
public enum ChaosBoonCategory { Skill, Accessory, Utility }

/// <summary>Everything one unlock card renders: ribbon + accent, the item, and a context line.</summary>
public sealed class ChaosUnlockCardData
{
    public string Ribbon = "";
    public Color Accent = Color.FromRgb(0xE8, 0x43, 0x93);
    public string Title = "";
    public string Desc = "";
    public string? Flavor;
    public string? Context;
    public string Glyph = "◈";
    public IImage? Icon;
}

/// <summary>
/// Avalonia port of ChaosUnlockCards: data builders + shared card visual.
/// The data builders are stubbed because the WPF catalogue services
/// (ChaosLifetimeBoons, ChaosUpgrades, ChaosMeta) live outside CCP.Core
/// and are not reachable from CCP.Avalonia. The shared BuildCardVisual
/// is fully functional and used by the Avalonia unlock overlay.
/// </summary>
public static class ChaosUnlockCards
{
    private static readonly Color ToyAccent = Color.FromRgb(0x7A, 0xFF, 0xD2);
    private static readonly Color AccessoryAccent = Color.FromRgb(0xFF, 0xD2, 0x7A);
    private static readonly Color CharmAccent = Color.FromRgb(0x7A, 0xE0, 0xFF);
    private static readonly Color HabitAccent = Color.FromRgb(0x9C, 0xE8, 0xA0);
    private static readonly Color PocketAccent = Color.FromRgb(0xE8, 0x43, 0x93);
    private static readonly Color CapstoneAccent = Color.FromRgb(0xFF, 0xC8, 0x3C);

    private static (string ribbon, Color accent) ByCategory(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Skill => ("NEW TOY UNLOCKED", ToyAccent),
        ChaosBoonCategory.Accessory => ("NEW ACCESSORY UNLOCKED", AccessoryAccent),
        _ => ("NEW CHARM UNLOCKED", CharmAccent),
    };

    /// <summary>A lifetime boon's level 1 just bought (call AFTER the unlock).</summary>
    public static ChaosUnlockCardData? ForBoonUnlock(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        var (ribbon, accent) = ByCategory(b.Category);
        string context =
            b.Category == ChaosBoonCategory.Utility ? "switched on — it works every descent."
            : ChaosMeta.IsBoonActive(id) ? "slipped straight into a pocket — it rides with you next descent."
            : ChaosMeta.SlotsFor(b.Category) == 0 ? "no pocket to carry it yet — she sells one at her bench."
            : "your pockets are full — swap it in from the BAG.";
        return new ChaosUnlockCardData
        {
            Ribbon = ribbon,
            Accent = accent,
            Title = b.Name,
            Desc = b.Desc,
            Flavor = b.Flavor,
            Context = context,
            Glyph = b.Glyph,
            Icon = AvaloniaChaosArt.Resolve("boons", id),
        };
    }

    /// <summary>A boon just deepened to its final level.</summary>
    public static ChaosUnlockCardData? ForCapstone(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null || string.IsNullOrWhiteSpace(b.CapstoneDesc)) return null;
        return new ChaosUnlockCardData
        {
            Ribbon = "CAPSTONE REACHED",
            Accent = CapstoneAccent,
            Title = b.Name,
            Desc = b.CapstoneDesc,
            Flavor = b.Flavor,
            Context = "fully deepened — its final gift is yours.",
            Glyph = b.Glyph,
            Icon = AvaloniaChaosArt.Resolve("boons", id),
        };
    }

    /// <summary>A trained habit just bought.</summary>
    public static ChaosUnlockCardData? ForHabit(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return null;
        return new ChaosUnlockCardData
        {
            Ribbon = "HABIT TRAINED",
            Accent = HabitAccent,
            Title = u.Name,
            Desc = u.Desc,
            Flavor = u.Flavor,
            Context = "always on from your next descent — switch it off in the toybox anytime.",
            Glyph = u.Glyph,
            Icon = u.IconPath != null ? AvaloniaChaosArt.TryLoad(u.IconPath) : AvaloniaChaosArt.Resolve("upgrades", id),
        };
    }

    /// <summary>Stub: returns a minimal pocket card.</summary>
    public static ChaosUnlockCardData ForPocket(bool isToy, string label, string line)
    {
        int n = isToy ? ChaosMeta.State.ToyPockets : ChaosMeta.State.AccessoryPockets;
        string kind = isToy ? "toy" : "accessory";
        string desc = n == 1
            ? $"you can now carry one {kind} into the descent. unlocked {kind}s equip from the BAG."
            : $"you can now carry {n} {kind}s into the descent at once. pick yours from the BAG.";
        return new ChaosUnlockCardData
        {
            Ribbon = "POCKET SEWN",
            Accent = PocketAccent,
            Title = label,
            Desc = desc,
            Flavor = line,
            Glyph = "👝",
        };
    }

    /// <summary>A lesson just completed mid-run: the gated item is now buyable.</summary>
    public static ChaosUnlockCardData? ForLesson(string id)
    {
        const string context = "now for sale in the toybox — drops will do the rest.";
        var b = ChaosLifetimeBoons.ById(id);
        if (b != null)
        {
            var (_, accent) = ByCategory(b.Category);
            return new ChaosUnlockCardData
            {
                Ribbon = "LESSON LEARNED",
                Accent = accent,
                Title = b.Name,
                Desc = b.Desc,
                Flavor = b.Flavor,
                Context = context,
                Glyph = b.Glyph,
                Icon = AvaloniaChaosArt.Resolve("boons", id),
            };
        }
        var u = ChaosUpgrades.ById(id);
        if (u != null)
        {
            return new ChaosUnlockCardData
            {
                Ribbon = "LESSON LEARNED",
                Accent = HabitAccent,
                Title = u.Name,
                Desc = u.Desc,
                Flavor = u.Flavor,
                Context = context,
                Glyph = u.Glyph,
                Icon = u.IconPath != null ? AvaloniaChaosArt.TryLoad(u.IconPath) : AvaloniaChaosArt.Resolve("upgrades", id),
            };
        }
        return null;
    }

    private static (string cue, float vol) CueFor(ChaosUnlockCardData d) => d.Ribbon switch
    {
        "CAPSTONE REACHED" => ("capstone_reached", 0.7f),
        "POCKET SEWN" => ("pocket_sewn", 0.7f),
        _ => ("unlock_card", 0.65f),
    };

    /// <summary>The card itself — shared by the Hub layer and the mid-run overlay.</summary>
    public static Border BuildCardVisual(ChaosUnlockCardData d, double width = 400)
    {
        var accent = new SolidColorBrush(d.Accent);
        var accentDim = new SolidColorBrush(Color.FromArgb(0xCC, d.Accent.R, d.Accent.G, d.Accent.B));

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = d.Ribbon,
            Foreground = accent,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeight.Bold,
            FontSize = 11.5,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition());

        Control icon;
        if (d.Icon != null)
        {
            icon = new Image { Source = d.Icon, Width = 64, Height = 64, Stretch = Stretch.Uniform };
        }
        else
        {
            icon = new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(0x28, d.Accent.R, d.Accent.G, d.Accent.B)),
                Child = new TextBlock
                {
                    Text = d.Glyph,
                    FontSize = 30,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }
        icon.VerticalAlignment = VerticalAlignment.Top;
        var iconScale = new ScaleTransform(0.4, 0.4);
        icon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        icon.RenderTransform = iconScale;
        row.Children.Add(icon);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = d.Title,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = d.Desc,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xEE)),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        if (!string.IsNullOrWhiteSpace(d.Flavor))
        {
            text.Children.Add(new TextBlock
            {
                Text = d.Flavor,
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xA0, 0xC0)),
                FontStyle = FontStyle.Italic,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0),
            });
        }
        if (!string.IsNullOrWhiteSpace(d.Context))
        {
            text.Children.Add(new TextBlock
            {
                Text = "→ " + d.Context,
                Foreground = accentDim,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 9, 0, 0),
            });
        }
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        stack.Children.Add(row);

        var border = new Border
        {
            Width = width,
            Child = stack,
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x1C, 0x1A, 0x36)),
            BorderBrush = accentDim,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14, 18, 16),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(0x80, d.Accent.R, d.Accent.G, d.Accent.B),
                Blur = 24,
                Spread = 0,
            }),
        };

        bool played = false;
        border.Loaded += (_, _) =>
        {
            if (played) return;
            played = true;
            try
            {
                var (cue, vol) = CueFor(d);
                AvaloniaChaosSfx.Play(cue, vol);

                // The icon pops in a breath after the card body (WPF BackEase EaseOut 0.4→1.0,
                // 380ms, 100ms delay), and the accent glow flares once (WPF DropShadow blur 24→46 /
                // opacity 0.5→0.9) — mirrored here on the Border's BoxShadow.
                AvaloniaChaosAnim.ScalePop(iconScale, 0.4, 1.0, 380, startDelayMs: 100);
                FlareBoxShadow(border, d.Accent, baseBlur: 24, peakBlur: 46, durationMs: 380);
            }
            catch (Exception ex) { App.Services?.GetRequiredService<ILogger<object>>().LogInformation("Unlock card flair failed: {E}", ex.Message); }
        };
        return border;
    }

    /// <summary>Flare the card's accent glow once: the Border's BoxShadow blur + alpha swell then
    /// settle back to the resting shadow (WPF DropShadowEffect blur 24→46 / opacity 0.5→0.9,
    /// AutoReverse SineEase over the duration).</summary>
    private static void FlareBoxShadow(Border border, Color color, double baseBlur, double peakBlur, double durationMs)
    {
        double startMs = Environment.TickCount64;
        double half = Math.Max(1, durationMs / 2.0);
        var timer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = Environment.TickCount64 - startMs;
            double phase = Math.Min(2, elapsed / half);          // 0..2 (swell then settle)
            double t = phase <= 1 ? phase : 2 - phase;
            double eased = (1 - Math.Cos(t * Math.PI)) / 2.0;    // sine ease in-out
            double blur = baseBlur + (peakBlur - baseBlur) * eased;
            byte a = (byte)(0x80 + (0xE6 - 0x80) * eased);       // ~0.5 -> ~0.9 alpha
            border.BoxShadow = new BoxShadows(new BoxShadow
            {
                Color = Color.FromArgb(a, color.R, color.G, color.B),
                Blur = blur,
                Spread = 0,
            });
            if (elapsed >= durationMs)
            {
                timer.Stop();
                border.BoxShadow = new BoxShadows(new BoxShadow
                {
                    Color = Color.FromArgb(0x80, color.R, color.G, color.B),
                    Blur = baseBlur,
                    Spread = 0,
                });
            }
        };
        timer.Start();
    }
}
