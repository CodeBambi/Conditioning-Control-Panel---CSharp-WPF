using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Invites;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Vault;

namespace ConditioningControlPanel;

/// <summary>
/// The vault gate card: what a free account sees after clicking a padlocked feature, in place of
/// the old jump to Settings · Account. Three faces in one borderless window:
/// <list type="bullet">
/// <item><b>Offer</b> - the clicked feature's art and tagline, what the tier holds, the price,
/// the supporter count when the server gives one, and the ways in: open the tier, redeem an
/// invite code (only once the server has invites), or sign in as an existing supporter.</item>
/// <item><b>Compare</b> - vault against lab, side by side.</item>
/// <item><b>Ending</b> - the last-day card of an invite week.</item>
/// </list>
/// Pure decisions live in <see cref="VaultOffer"/>; this class only paints and routes.
/// </summary>
public sealed class VaultGateDialog : Window
{
    public const string PatreonUrl = "https://www.patreon.com/CodeBambi";

    /// <summary>The site's own checkout (card or PayPal, monthly). {0} is basic or prime.</summary>
    public const string CardUrlFormat = "https://app.cclabs.app/subscribe?plan={0}&from=panel";

    private static readonly Brush CardBg = Frozen("#FF151528");
    private static readonly Brush RowBg = Frozen("#252542");
    private static readonly Brush Edge = Frozen("#3D3D60");
    private static readonly Brush Gold = Frozen("#E5C76B");
    private static readonly Brush Good = Frozen("#7FD8A6");
    private static readonly Brush Warn = Frozen("#FFB347");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Muted = Frozen("#B8B1CC");
    private static readonly Brush Dim = Frozen("#8079A3");

    /// <summary>Last supporter count read this run; the card never waits on it.</summary>
    private static int? _supporters;
    private static DateTime _supportersReadUtc = DateTime.MinValue;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Whether /v2/invites/mine answered, remembered for ten minutes so a padlock click
    /// costs no round trip of its own.</summary>
    private static bool? _invitesReachable;
    private static DateTime _invitesReadUtc = DateTime.MinValue;

    /// <summary>The live first-month sale from /config/vault-sale, read at most every five minutes.
    /// Null (normal prices) when there is none or the read failed.</summary>
    private static VaultSaleInfo? _sale;
    private static DateTime _saleReadUtc = DateTime.MinValue;

    /// <summary>The card on screen, if any. One at a time: a second padlock or toast replaces it.</summary>
    private static VaultGateDialog? _open;

    private readonly Border _card = new();
    private readonly StackPanel _body = new();
    private readonly ExclusiveFeature? _feature;
    private readonly int _tier;
    private readonly Action _signIn;
    private readonly PriceCurrency _currency = VaultOffer.LocalCurrency();

    /// <summary>Every price on the face showing, so a sale that lands late can repaint them.</summary>
    private readonly List<(StackPanel Slot, int Tier, bool Compact, bool Yearly)> _priceSlots = new();

    /// <summary>The compare face's billing switch. Yearly is Patreon-only, and says so.</summary>
    private bool _yearly;

    private VaultGateDialog(ExclusiveFeature? feature, int tier, Action signIn)
    {
        _feature = feature;
        _tier = Math.Clamp(tier, 1, 2);
        _signIn = signIn;

        Title = "CC Labs";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _card.Background = CardBg;
        _card.CornerRadius = new CornerRadius(14);
        _card.BorderThickness = new Thickness(1.5);
        _card.SetResourceReference(Border.BorderBrushProperty, "PinkBrush");
        _card.Margin = new Thickness(12);
        _card.Padding = new Thickness(22, 20, 22, 18);
        _card.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x69, 0xB4), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.45 };
        _card.Child = _body;
        Content = _card;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // DragMove captures the mouse and eats the button-up, so a press on a clickable piece (every
        // link and pill wears the hand cursor) must not start a drag or its MouseLeftButtonUp never fires.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && !OnClickable(e.OriginalSource as DependencyObject)) try { DragMove(); } catch (InvalidOperationException) { } }; // swallow: DragMove refuses once the button is up
    }

    // ============================== entry points ==============================

    /// <summary>The gate card for a feature key (null = the vault as a whole) at a tier.</summary>
    public static void ShowOffer(Window? owner, string? featureKey, int tier, Action signIn)
    {
        var feature = VaultOffer.Feature(featureKey);
        var dialog = new VaultGateDialog(feature, Math.Max(tier, feature?.Tier ?? 1), signIn) { Owner = owner };
        dialog.BuildOffer();
        Present(dialog);
        _ = dialog.FillSaleAsync();
        _ = dialog.FillAsyncBits();
    }

    private static void Present(VaultGateDialog dialog)
    {
        try { _open?.Close(); }
        catch (InvalidOperationException) { } // swallow: the previous card was already closing
        _open = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(_open, dialog)) _open = null; };
        dialog.Show();
    }

    /// <summary>The last-day card of an invite week.</summary>
    public static void ShowEnding(Window? owner, DateTime grantUntilUtc, Action signIn)
    {
        var dialog = new VaultGateDialog(null, 1, signIn) { Owner = owner };
        dialog.BuildEnding(grantUntilUtc);
        Present(dialog);
        _ = dialog.FillSaleAsync();
    }

    // ============================== offer ==============================

    private StackPanel? _proofSlot;
    private StackPanel? _codeSlot;

    private void BuildOffer()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        var lab = _tier >= 2;
        _body.Children.Add(Chip(Loc.Get(lab ? "vaultgate_chip_lab" : "vaultgate_chip_vault"), lab ? Gold : null));

        if (_feature != null) _body.Children.Add(Art(_feature.ArtResource));

        var name = _feature == null ? null : Loc.Get(_feature.TitleLocKey);
        _body.Children.Add(Heading(name == null
            ? Loc.Get("vaultgate_title_generic")
            : Loc.GetF(lab ? "vaultgate_title_lab" : "vaultgate_title_vault", name)));
        _body.Children.Add(Para(_feature == null ? Loc.Get("vaultgate_generic_body") : Loc.Get(_feature.TaglineLocKey)));

        _body.Children.Add(Perks(lab, 12.5));

        _proofSlot = new StackPanel();
        _body.Children.Add(_proofSlot);
        PaintProof();

        _body.Children.Add(Price(_tier));
        _body.Children.Add(YearlyHint(_tier));

        var open = PrimaryButton(Loc.Get(lab ? "vaultgate_open_lab" : "vaultgate_open_vault"));
        open.Click += (_, _) => { OpenPatreon("gate"); Close(); };
        _body.Children.Add(open);
        _body.Children.Add(CardLink(lab, "gate"));

        var links = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        links.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        links.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var signIn = Link(Loc.Get("vaultgate_signin"));
        signIn.MouseLeftButtonUp += (_, _) => { Close(); _signIn(); };
        links.Children.Add(signIn);
        var compare = Link(Loc.Get("vaultgate_compare"));
        compare.MouseLeftButtonUp += (_, _) => BuildCompare();
        Grid.SetColumn(compare, 1);
        links.Children.Add(compare);
        _body.Children.Add(links);

        _codeSlot = new StackPanel();
        _body.Children.Add(_codeSlot);

        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
        _body.Children.Add(CloseLink());
    }

    /// <summary>The two network bits the card never waits for: the supporter count and whether
    /// invites exist server-side. Each repaints its own slot when it lands.</summary>
    private async Task FillAsyncBits()
    {
        try
        {
            if (DateTime.UtcNow - _supportersReadUtc > TimeSpan.FromHours(6))
            {
                _supportersReadUtc = DateTime.UtcNow;
                using var res = await Http.GetAsync($"{BackRoomApi.BaseUrl}/v2/public/supporters");
                if (res.IsSuccessStatusCode)
                    _supporters = VaultOffer.ParseSupporterCount(await res.Content.ReadAsStringAsync());
                PaintProof();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] supporters read failed: {E}", ex.GetType().Name); }

        try
        {
            if (BackRoomApi.AppIdentity() == null) return;
            if (_invitesReachable == null || DateTime.UtcNow - _invitesReadUtc > TimeSpan.FromMinutes(10))
            {
                _invitesReadUtc = DateTime.UtcNow;
                _invitesReachable = (await new InviteApi().MineAsync()).Reachable;
            }
            if (_invitesReachable == true && _codeSlot != null && IsLoaded)
            {
                var link = Link(Loc.Get("vaultgate_have_code"));
                link.Margin = new Thickness(0, 8, 0, 0);
                link.MouseLeftButtonUp += (_, _) =>
                {
                    var box = new InviteRedeemBox("gate") { Margin = new Thickness(0, 10, 0, 0) };
                    _codeSlot.Children.Clear();
                    _codeSlot.Children.Add(box);
                    box.FocusCode();
                };
                _codeSlot.Children.Clear();
                _codeSlot.Children.Add(link);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] invites probe failed: {E}", ex.GetType().Name); }
    }

    /// <summary>Reads the sale switch (cached five minutes) and repaints the prices when it lands.
    /// Any failure means no sale: the card falls back to the normal prices.</summary>
    private async Task FillSaleAsync()
    {
        if (DateTime.UtcNow - _saleReadUtc <= TimeSpan.FromMinutes(5)) return;
        _saleReadUtc = DateTime.UtcNow;
        VaultSaleInfo? sale = null;
        try
        {
            using var res = await Http.GetAsync($"{BackRoomApi.BaseUrl}/config/vault-sale");
            if (res.IsSuccessStatusCode) sale = VaultSale.Parse(await res.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] sale read failed: {E}", ex.GetType().Name); }
        _sale = sale;
        PaintPrices();
    }

    private void PaintPrices()
    {
        foreach (var (slot, tier, compact, yearly) in _priceSlots) PaintPrice(slot, tier, compact, yearly);
    }

    private void PaintProof()
    {
        if (_proofSlot == null) return;
        _proofSlot.Children.Clear();
        var floor = VaultOffer.SupporterFloor(_supporters);
        if (floor == null) return;
        _proofSlot.Children.Add(Para(Loc.GetF("vaultgate_proof", floor), Text, 12.5, new Thickness(0, 10, 0, 0)));
    }

    // ============================== compare ==============================

    private void BuildCompare()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        _body.Children.Add(Heading(Loc.Get("vaultgate_compare_title")));
        _body.Children.Add(BillingSwitch());

        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(TierColumn(1));
        var lab = TierColumn(2);
        Grid.SetColumn(lab, 2);
        grid.Children.Add(lab);
        _body.Children.Add(grid);
        if (_yearly) _body.Children.Add(Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 10, 0, 0)));

        var back = Link(Loc.Get("vaultgate_back"));
        back.Margin = new Thickness(0, 12, 0, 0);
        back.MouseLeftButtonUp += (_, _) => { BuildOffer(); _ = FillAsyncBits(); };
        _body.Children.Add(back);
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
    }

    private FrameworkElement TierColumn(int tier)
    {
        var lab = tier >= 2;
        var col = new StackPanel();
        col.Children.Add(Chip(Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name"), lab ? Gold : null));
        col.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"),
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
            FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = Brushes.White,
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        col.Children.Add(Price(tier, compact: true, yearly: _yearly));
        col.Children.Add(Perks(lab, 12));
        var choose = SmallButton(Loc.GetF("vaultgate_choose", Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name")));
        choose.Margin = new Thickness(0, 12, 0, 0);
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        choose.Click += (_, _) => { OpenPatreon(lab ? "compare-lab" : "compare-vault"); Close(); };
        col.Children.Add(choose);
        col.Children.Add(CardLink(lab, lab ? "compare-lab" : "compare-vault"));

        return new Border
        {
            Background = RowBg,
            BorderBrush = lab ? Gold : Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = col,
        };
    }

    // ============================== ending ==============================

    private void BuildEnding(DateTime grantUntilUtc)
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        _body.Children.Add(Chip(Loc.Get("vaultgate_ending_chip"), Warn));
        _body.Children.Add(Heading(Loc.Get("vaultgate_ending_title")));
        _body.Children.Add(new Border
        {
            Background = RowBg, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12, 12, 12, 4), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Loc.Get("vaultgate_ending_bubble"), Foreground = Text, FontSize = 13, TextWrapping = TextWrapping.Wrap },
        });
        _body.Children.Add(Para(Loc.GetF("vaultgate_ending_until", grantUntilUtc.ToLocalTime().ToString("ddd d MMM, HH:mm")),
            Warn, 12.5, new Thickness(0, 10, 0, 0)));
        _body.Children.Add(Price(1));
        _body.Children.Add(YearlyHint(1));

        var keep = PrimaryButton(Loc.Get("vaultgate_ending_keep"));
        keep.Click += (_, _) => { OpenPatreon("ending"); Close(); };
        _body.Children.Add(keep);
        _body.Children.Add(CardLink(false, "ending"));

        var let = Link(Loc.Get("vaultgate_ending_let_close"));
        let.HorizontalAlignment = HorizontalAlignment.Center;
        let.Margin = new Thickness(0, 12, 0, 0);
        let.MouseLeftButtonUp += (_, _) => Close();
        _body.Children.Add(let);
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
    }

    // ============================== bits ==============================

    private static void OpenPatreon(string from)
    {
        App.Logger?.Information("[VaultGate] open Patreon from {From}", from);
        try { Process.Start(new ProcessStartInfo { FileName = PatreonUrl, UseShellExecute = true }); }
        catch (Exception ex) { App.Logger?.Error(ex, "[VaultGate] failed to open Patreon"); }
    }

    /// <summary>The other way to pay, under every Patreon button: the site's card / PayPal checkout.</summary>
    private FrameworkElement CardLink(bool lab, string from)
    {
        var link = Link(Loc.Get("vaultgate_or_card"));
        link.HorizontalAlignment = HorizontalAlignment.Center;
        link.TextAlignment = TextAlignment.Center;
        link.Margin = new Thickness(0, 8, 0, 0);
        link.MouseLeftButtonUp += (_, _) => { OpenCard(lab ? "prime" : "basic", from); Close(); };
        return link;
    }

    private static void OpenCard(string plan, string from)
    {
        App.Logger?.Information("[VaultGate] open card checkout ({Plan}) from {From}", plan, from);
        try { Process.Start(new ProcessStartInfo { FileName = string.Format(CardUrlFormat, plan), UseShellExecute = true }); }
        catch (Exception ex) { App.Logger?.Error(ex, "[VaultGate] failed to open the card checkout"); }
    }

    private static FrameworkElement Chip(string text, Brush? tint)
    {
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var b = new Border
        {
            CornerRadius = new CornerRadius(99), Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Child = t,
        };
        if (tint != null)
        {
            t.Foreground = tint;
            b.Background = new SolidColorBrush(((SolidColorBrush)tint).Color) { Opacity = 0.16 };
        }
        else
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, "PinkBrush");
            b.Background = Frozen("#24FF69B4");
        }
        return b;
    }

    private static FrameworkElement Art(string packPath)
    {
        var host = new Border { Height = 150, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true, Background = RowBg };
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/" + packPath.TrimStart('/'));
            img.DecodePixelWidth = 840;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            host.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) { Diag.Swallowed(ex, "gate art missing"); }
        return host;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
        FontWeight = FontWeights.SemiBold, FontSize = 21, Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
    };

    private static TextBlock Para(string text, Brush? brush = null, double size = 13, Thickness? margin = null) => new()
    {
        Text = text, Foreground = brush ?? Muted, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 4, 0, 0),
    };

    /// <summary>What a tier adds, as the gate audit found it (2026-10-02): Basic over free, Prime
    /// over Basic. Keep these lines true when a gate moves; the card is the only place a free
    /// account reads the full list.</summary>
    private static readonly string[] BasicPerks =
    {
        "vaultgate_basic_1", "vaultgate_basic_2", "vaultgate_basic_3", "vaultgate_basic_4",
        "vaultgate_basic_5", "vaultgate_basic_6", "vaultgate_basic_7", "vaultgate_basic_8",
    };

    private static readonly string[] PrimePerks =
    {
        "vaultgate_prime_1", "vaultgate_prime_2", "vaultgate_prime_3", "vaultgate_prime_4",
        "vaultgate_prime_5", "vaultgate_prime_6", "vaultgate_prime_7",
    };

    /// <summary>The tier's header line and its bullets: a small dot per row, tight spacing, so
    /// eight lines cost the card little height.</summary>
    private static FrameworkElement Perks(bool lab, double size)
    {
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        list.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_prime_head" : "vaultgate_basic_head"),
            Foreground = Muted, FontSize = size - 0.5, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var key in lab ? PrimePerks : BasicPerks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 5, Height = 5, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, size * 0.47, 0, 0),
            };
            if (lab) dot.Fill = Gold; else dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PinkBrush");
            row.Children.Add(dot);
            var line = new TextBlock { Text = Loc.Get(key), Foreground = Text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            list.Children.Add(row);
        }
        return list;
    }

    private FrameworkElement Price(int tier, bool compact = false, bool yearly = false)
    {
        var slot = new StackPanel { Margin = new Thickness(0, compact ? 8 : 12, 0, compact ? 0 : 4) };
        _priceSlots.Add((slot, tier, compact, yearly));
        PaintPrice(slot, tier, compact, yearly);
        return slot;
    }

    /// <summary>The price, or during a first-month sale the sale price beside the struck normal
    /// one, the "then" line and the end date. Yearly never shows a sale.</summary>
    private void PaintPrice(StackPanel slot, int tier, bool compact, bool yearly)
    {
        slot.Children.Clear();
        var price = VaultOffer.PriceFor(tier, _currency);
        var sale = VaultSale.AppliesTo(_sale, tier, DateTime.UtcNow, yearly) ? _sale : null;
        var line = new WrapPanel();
        line.Children.Add(new TextBlock
        {
            Text = VaultOffer.Money(sale != null ? VaultSale.FirstMonthCents(price.MonthlyCents, sale.Percent)
                : yearly ? price.YearlyCents : price.MonthlyCents, _currency),
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
            FontWeight = FontWeights.SemiBold, FontSize = compact ? 22 : 28, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (sale != null)
            line.Children.Add(new TextBlock
            {
                Text = VaultOffer.Money(price.MonthlyCents, _currency),
                TextDecorations = TextDecorations.Strikethrough,
                FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
                Foreground = Dim, FontSize = compact ? 14 : 17, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, compact ? 3 : 4),
            });
        line.Children.Add(new TextBlock
        {
            Text = sale != null
                ? Loc.Get("vaultgate_sale_first")
                : yearly
                    ? Loc.GetF("vaultgate_price_year", VaultOffer.YearlyPerMonth(price))
                    : Loc.GetF("vaultgate_price", VaultOffer.PerDay(price)),
            Foreground = Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap,
        });
        slot.Children.Add(line);
        if (sale == null) return;

        slot.Children.Add(Para(Loc.GetF("vaultgate_sale_line", sale.Percent, VaultOffer.Money(price.MonthlyCents, _currency)),
            Good, compact ? 12 : 12.5, new Thickness(0, 2, 0, 0)));
        if (VaultSale.EndsText(sale) is string ends)
            slot.Children.Add(Para(Loc.GetF("vaultgate_sale_ends", ends), Dim, compact ? 11.5 : 12, new Thickness(0, 2, 0, 0)));
    }

    /// <summary>"Or €60 a year on Patreon: 2 months free." under a monthly price.</summary>
    private FrameworkElement YearlyHint(int tier)
        => Para(Loc.GetF("vaultgate_yearly_hint", VaultOffer.Money(VaultOffer.PriceFor(tier, _currency).YearlyCents, _currency)),
            Gold, 12, new Thickness(0, 0, 0, 10));

    /// <summary>Monthly / Yearly pills on the compare face. Rebuilds the face on a switch.</summary>
    private FrameworkElement BillingSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var yearly in new[] { false, true })
        {
            var on = yearly == _yearly;
            var pill = new Border
            {
                CornerRadius = new CornerRadius(99), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0),
                Background = on ? RowBg : Brushes.Transparent, BorderBrush = on ? Gold : Edge, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = Loc.Get(yearly ? "vaultgate_billing_yearly" : "vaultgate_billing_monthly"),
                    Foreground = on ? Gold : Muted, FontSize = 12, FontWeight = FontWeights.SemiBold,
                },
            };
            var choice = yearly;
            pill.MouseLeftButtonUp += (_, _) => { if (_yearly != choice) { _yearly = choice; BuildCompare(); } };
            row.Children.Add(pill);
        }
        return row;
    }

    private static Button PrimaryButton(string text)
    {
        var b = SmallButton(text);
        b.FontSize = 15;
        b.Padding = new Thickness(14, 10, 14, 10);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    private static Button SmallButton(string text)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(StyleProperty, "SmallPinkButton");
        return b;
    }

    /// <summary>True when the press landed on (or inside) something that wears the hand cursor.</summary>
    private static bool OnClickable(DependencyObject? d)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (d is Window) return false;
        }
        return false;
    }

    private static TextBlock Link(string text) => new()
    {
        Text = text, Foreground = Muted, FontSize = 12, Cursor = Cursors.Hand,
        TextDecorations = TextDecorations.Underline, TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Fine(string text) => new()
    {
        Text = text, Foreground = Dim, FontSize = 11, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
    };

    private FrameworkElement CloseLink()
    {
        var l = Link(Loc.Get("vaultgate_close"));
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.Margin = new Thickness(0, 8, 0, 0);
        l.MouseLeftButtonUp += (_, _) => Close();
        return l;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
