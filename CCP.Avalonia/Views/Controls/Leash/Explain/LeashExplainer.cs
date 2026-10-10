// PORTED from WPF 7.1.5 Controls/Leash/Explain/LeashExplainer.cs: the doors into the leash explainer.
// Show = the "?" on any leash surface (a small glass window, four pictures, two lines, Got it).
// BeforeOffer = the holder's first offer (Offer / Not now; the offer goes only from Offer; after
// the first time it sends at once). LeashAskIntro = the leashed side's first ask card, embedded,
// holding "Put it on" for LeashIntroRule.AskReadMs. Flags live in AppSettings (LeashIntroRule).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash.Explain;

public static class LeashExplainer
{
    /// <summary>Test seam: receives each built window instead of it being shown.</summary>
    internal static Action<Window>? PresentOverride { get; set; }

    /// <summary>Where a "?" without an owner opens (the panel). Set by the shell; null = centre screen.</summary>
    internal static Func<Window?> DefaultOwner { get; set; } = () => null;

    /// <summary>Holder surfaces (the holder's card, the Offer chip) get the holder variant; the rest
    /// (leashed card, ask card, gate) the leashed one.</summary>
    public static LeashIntroSide SideFor(string? role)
        => role != null && (role.Equals("Holder", StringComparison.OrdinalIgnoreCase)
                            || role.Equals("Offer", StringComparison.OrdinalIgnoreCase))
            ? LeashIntroSide.Holder
            : LeashIntroSide.Leashed;

    /// <summary>Fills LeashExplainHost.Presenter (the "?" door) once; safe to call again.</summary>
    internal static void Install()
    {
        LeashExplainHost.Presenter ??= role => Show(null, SideFor(role.ToString()));
    }

    /// <summary>The "?" door. Returns the window, or null when it could not open.</summary>
    public static Window? Show(Window? owner, LeashIntroSide side, string? name = null)
    {
        try
        {
            var w = Build(side, name, Loc.Get("leash_explain_title"), offer: null, out _);
            Present(w, owner);
            return w;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] explainer failed to open: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>The holder's first offer: the explainer with Offer / Not now, and
    /// <paramref name="sendOffer"/> only from Offer (which records the flag and saves). When not
    /// owed, sends at once. True when the offer was sent synchronously.</summary>
    public static bool BeforeOffer(Window? owner, string friendName, Action sendOffer, global::ConditioningControlPanel.Models.AppSettings? settings = null)
    {
        settings ??= Settings();
        if (!LeashIntroRule.ShouldExplain(settings, LeashIntroSide.Holder))
        {
            sendOffer();
            return true;
        }
        try
        {
            var title = string.IsNullOrWhiteSpace(friendName)
                ? Loc.Get("leash_explain_title")
                : Loc.GetF("leash_explain_offer_title", friendName.Trim());
            var w = Build(LeashIntroSide.Holder, friendName, title, offer: () =>
            {
                if (LeashIntroRule.Record(settings, LeashIntroMoment.OfferSent)) Save();
                sendOffer();
            }, out _);
            Present(w, owner);
            return false;
        }
        catch (Exception ex)
        {
            // An explainer that cannot open must not strand the offer.
            Serilog.Log.Debug("[Leash] explainer failed before an offer: {E}", ex.Message);
            sendOffer();
            return true;
        }
    }

    internal static global::ConditioningControlPanel.Models.AppSettings? Settings()
    {
        try { return CoreSettings.Current; } catch { return null; }
    }

    internal static void Save()
    {
        try { CoreSettings.Save(); } catch { }
    }

    // ---- the window ---------------------------------------------------------------------------

    /// <summary>Builds the window without showing it.</summary>
    internal static Window Build(LeashIntroSide side, string? name, string title, Action? offer, out LeashExplainCard card)
    {
        var w = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Title = title,
            Tag = "leash-explainer",
        };

        card = new LeashExplainCard(side, name, LeashExplainLayout.Row) { Width = 600 };

        var head = new Grid { Margin = new Thickness(4, 0, 0, 12), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.SemiBold,
            FontSize = 20,
            Foreground = FriendsDrawer.Text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var x = FriendsDrawer.Pill(new TextBlock
        {
            Text = "×",
            FontSize = 16,
            Foreground = FriendsDrawer.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }, FriendsDrawer.Raised, FriendsDrawer.Muted, "leash-explain-close", FriendsDrawer.Line);
        x.Width = 26;
        x.Height = 26;
        x.Padding = new Thickness(0);
        x.CornerRadius = new CornerRadius(13);
        x.HorizontalContentAlignment = HorizontalAlignment.Center;
        x.VerticalContentAlignment = VerticalAlignment.Center;
        x.Click += (_, _) => w.Close();
        Grid.SetColumn(x, 1);
        head.Children.Add(x);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        if (offer != null)
        {
            var later = MakeButton(Loc.Get("leash_explain_not_now"), false, "leash-explain-not-now");
            later.Click += (_, _) => w.Close();
            var send = MakeButton(Loc.Get("leash_explain_offer"), true, "leash-explain-offer");
            send.Margin = new Thickness(8, 0, 0, 0);
            send.Click += (_, _) =>
            {
                w.Close();
                try { offer(); } catch (Exception ex) { Serilog.Log.Debug("[Leash] offer from explainer failed: {E}", ex.Message); }
            };
            foot.Children.Add(later);
            foot.Children.Add(send);
        }
        else
        {
            var ok = MakeButton(Loc.Get("leash_explain_got_it"), true, "leash-explain-got-it");
            ok.Click += (_, _) => w.Close();
            foot.Children.Add(ok);
        }

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(card);
        stack.Children.Add(foot);

        var shell = new Border
        {
            Child = stack,
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0x22, 0x15, 0x3E), 0), new GradientStop(Color.FromRgb(0x16, 0x0E, 0x29), 1) },
            },
            BorderBrush = FriendsDrawer.Line2,
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 4 24 0 #8C000000"),
        };
        shell.PointerPressed += (_, e) =>
        {
            if (e.Source is Button || e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed != true) return;
            try { w.BeginMoveDrag(e); } catch { }
        };
        w.Content = shell;
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; w.Close(); }
        };
        return w;
    }

    private static Button MakeButton(string text, bool primary, string tag)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var b = primary
            ? FriendsDrawer.Pill(label, FriendsDrawer.Mint, FriendsDrawer.MintInk, tag, new SolidColorBrush(Color.FromRgb(0x8C, 0xFF, 0xE0)))
            : FriendsDrawer.Pill(label, FriendsDrawer.Raised, FriendsDrawer.Text, tag, FriendsDrawer.Line2);
        b.CornerRadius = new CornerRadius(12);
        b.Padding = primary ? new Thickness(18, 8, 18, 8) : new Thickness(16, 8, 16, 8);
        b.MinWidth = 96;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        return b;
    }

    private static void Present(Window w, Window? owner)
    {
        if (PresentOverride is { } o) { o(w); return; }
        try { owner ??= DefaultOwner(); } catch { }
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, w))
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.Show(owner);
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.Show();
        }
        w.Activate();
    }
}

/// <summary>The leashed side's first ask card: the explainer embedded above the intensity switch,
/// "Put it on" held until it has been read.</summary>
public static class LeashAskIntro
{
    /// <summary>If the leashed side still owes a look, insert the explainer (2 x 2) into
    /// <paramref name="slot"/> at <paramref name="index"/>, disable <paramref name="putItOn"/> and
    /// enable it again after LeashIntroRule.AskReadMs on screen. Null when nothing was owed.</summary>
    public static LeashExplainCard? Attach(Panel slot, int index, Button putItOn, string? holderName, global::ConditioningControlPanel.Models.AppSettings? settings = null)
    {
        settings ??= LeashExplainer.Settings();
        if (!LeashIntroRule.ShouldExplain(settings, LeashIntroSide.Leashed)) return null;

        var card = new LeashExplainCard(LeashIntroSide.Leashed, holderName, LeashExplainLayout.Grid)
        {
            Margin = new Thickness(0, 4, 0, 10),
        };
        index = Math.Max(0, Math.Min(index, slot.Children.Count));
        slot.Children.Insert(index, card);

        var tip = ToolTip.GetTip(putItOn);
        putItOn.IsEnabled = false;
        ToolTip.SetTip(putItOn, Loc.Get("leash_explain_read_first"));
        card.ReadyForAnswer += () =>
        {
            putItOn.IsEnabled = true;
            ToolTip.SetTip(putItOn, tip);
            LeashFx.Pop(putItOn);   // WPF MotionFx.PressSquish in + out
        };
        card.StartReadClock(alreadySeen: false);
        return card;
    }

    /// <summary>The ask was answered (put it on or no). Records the flag and saves.</summary>
    public static void Answered(global::ConditioningControlPanel.Models.AppSettings? settings = null)
    {
        settings ??= LeashExplainer.Settings();
        if (LeashIntroRule.Record(settings, LeashIntroMoment.AskAnswered)) LeashExplainer.Save();
    }
}
