using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash.Explain;

/// <summary>
/// The doors into the leash explainer.
///
/// <list type="bullet">
/// <item><see cref="Show"/>: the "?" on any leash surface. A small glass window with the four
/// pictures, the two lines and Got it.</item>
/// <item><see cref="BeforeOffer"/>: the holder's first offer. Opens the explainer with Offer and
/// Not now; the offer is sent only from its Offer button. After the first time it sends at once.</item>
/// <item><see cref="LeashAskIntro"/>: the leashed side's first ask card, embedded, holding
/// "Put it on" for a moment.</item>
/// </list>
/// </summary>
public static class LeashExplainer
{
    /// <summary>
    /// Map a surface role name to the side the explainer is written for. Holder surfaces (the
    /// holder's card, the Offer chip) get the holder variant; everything else (the leashed card,
    /// the ask card, the gate) gets the leashed one. Takes the name so callers can pass any enum
    /// (<c>role.ToString()</c>) without this lane depending on it.
    /// </summary>
    public static LeashIntroSide SideFor(string? role)
        => role != null && (role.Equals("Holder", StringComparison.OrdinalIgnoreCase)
                            || role.Equals("Offer", StringComparison.OrdinalIgnoreCase))
            ? LeashIntroSide.Holder
            : LeashIntroSide.Leashed;

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
            App.Logger?.Debug("[Leash] explainer failed to open: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The holder's first offer. When the explainer is still owed it opens with Offer / Not now and
    /// <paramref name="sendOffer"/> runs only from Offer (which also records the flag and saves).
    /// When it is not owed, <paramref name="sendOffer"/> runs at once. Returns true when the offer
    /// was sent synchronously (no explainer shown).
    /// </summary>
    public static bool BeforeOffer(Window? owner, string friendName, Action sendOffer, AppSettings? settings = null)
    {
        settings ??= App.Settings?.Current;
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
            App.Logger?.Debug("[Leash] explainer failed before an offer: {E}", ex.Message);
            sendOffer();
            return true;
        }
    }

    internal static void Save()
    {
        try { App.Settings?.Save(); } catch { }
    }

    // ---- the window ---------------------------------------------------------------------------

    /// <summary>Builds the window without showing it (the render tests use this).</summary>
    internal static Window Build(LeashIntroSide side, string? name, string title, Action? offer, out LeashExplainCard card)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Title = title,
            Tag = "leash-explainer",
        };

        card = new LeashExplainCard(side, name, LeashExplainLayout.Row) { Width = 600 };

        var head = new Grid { Margin = new Thickness(4, 0, 0, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock
        {
            Text = title,
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 20,
            Foreground = FriendsLook.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        head.Children.Add(t);
        var x = FriendsLook.Pill(new TextBlock
        {
            Text = "×",
            FontSize = 16,
            Foreground = FriendsLook.MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }, FriendsLook.ButtonBrush, FriendsLook.MutedBrush, FriendsLook.LineBrush, 13, new Thickness(0));
        x.Width = 26;
        x.Height = 26;
        x.Tag = "leash-explain-close";
        x.Click += (_, _) => w.Close();
        Grid.SetColumn(x, 1);
        head.Children.Add(x);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        if (offer != null)
        {
            var later = MakeButton(Loc.Get("leash_explain_not_now"), primary: false);
            later.Tag = "leash-explain-not-now";
            later.Click += (_, _) => w.Close();
            var send = MakeButton(Loc.Get("leash_explain_offer"), primary: true);
            send.Tag = "leash-explain-offer";
            send.Margin = new Thickness(8, 0, 0, 0);
            send.Click += (_, _) =>
            {
                w.Close();
                try { offer(); } catch (Exception ex) { App.Logger?.Debug("[Leash] offer from explainer failed: {E}", ex.Message); }
            };
            foot.Children.Add(later);
            foot.Children.Add(send);
        }
        else
        {
            var ok = MakeButton(Loc.Get("leash_explain_got_it"), primary: true);
            ok.Tag = "leash-explain-got-it";
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
            Background = new LinearGradientBrush(FriendsLook.Rgb(0x22, 0x15, 0x3E), FriendsLook.Ground, 90),
            BorderBrush = FriendsLook.Line2Brush,
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55 },
        };
        shell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && w.IsLoaded)
                try { w.DragMove(); } catch { }
        };
        w.Content = shell;
        w.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; w.Close(); }
        };
        return w;
    }

    private static Button MakeButton(string text, bool primary)
    {
        var label = new TextBlock
        {
            Text = text,
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = primary ? FriendsLook.MintInkBrush : FriendsLook.TextBrush,
        };
        var b = primary
            ? FriendsLook.Pill(label, FriendsLook.MintBrush, FriendsLook.MintInkBrush, FriendsLook.MintBrush, 12,
                new Thickness(18, 8, 18, 8), FriendsLook.Frozen(FriendsLook.Rgb(0x8C, 0xFF, 0xE0)))
            : FriendsLook.Pill(label, FriendsLook.ButtonBrush, FriendsLook.TextBrush, FriendsLook.Line2Brush, 12,
                new Thickness(16, 8, 16, 8));
        b.MinWidth = 96;
        return b;
    }

    private static void Present(Window w, Window? owner)
    {
        owner ??= App.MainWindowRef is { IsVisible: true } m ? m : null;
        if (owner != null && owner.IsVisible && !ReferenceEquals(owner, w))
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        w.Show();
        w.Activate();
    }
}

/// <summary>
/// The leashed side's first ask card. The UI lane's ask card calls <see cref="Attach"/> with the
/// slot above its intensity switch and its "Put it on" button, and <see cref="Answered"/> when the
/// answer (yes or no) goes out.
/// </summary>
public static class LeashAskIntro
{
    /// <summary>
    /// If the leashed side still owes a look, insert the explainer (2 x 2, drawer width) into
    /// <paramref name="slot"/> at <paramref name="index"/>, disable <paramref name="putItOn"/> and
    /// enable it again once the card has been on screen for <see cref="LeashIntroRule.AskReadMs"/>.
    /// Returns the card, or null when nothing was owed (the ask card is then left untouched and
    /// can show its own "?").
    /// </summary>
    public static LeashExplainCard? Attach(Panel slot, int index, ButtonBase putItOn, string? holderName, AppSettings? settings = null)
    {
        settings ??= App.Settings?.Current;
        if (!LeashIntroRule.ShouldExplain(settings, LeashIntroSide.Leashed)) return null;

        var card = new LeashExplainCard(LeashIntroSide.Leashed, holderName, LeashExplainLayout.Grid)
        {
            Margin = new Thickness(0, 4, 0, 10),
        };
        index = Math.Max(0, Math.Min(index, slot.Children.Count));
        slot.Children.Insert(index, card);

        var tip = putItOn.ToolTip;
        putItOn.IsEnabled = false;
        putItOn.ToolTip = Loc.Get("leash_explain_read_first");
        card.ReadyForAnswer += () =>
        {
            putItOn.IsEnabled = true;
            putItOn.ToolTip = tip;
            ConditioningControlPanel.Services.MotionFx.PressSquish(putItOn, true);
            ConditioningControlPanel.Services.MotionFx.PressSquish(putItOn, false);
        };
        card.StartReadClock(alreadySeen: false);
        return card;
    }

    /// <summary>The ask was answered (put it on or no). Records the flag and saves.</summary>
    public static void Answered(AppSettings? settings = null)
    {
        settings ??= App.Settings?.Current;
        if (LeashIntroRule.Record(settings, LeashIntroMoment.AskAnswered)) LeashExplainer.Save();
    }
}
