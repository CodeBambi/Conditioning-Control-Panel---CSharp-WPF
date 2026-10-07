using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Vault;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs;

/// <summary>
/// PORTED from ConditioningControlPanel/Dialogs/VaultGateDialog.cs (main fbe161de2, c8fff36a0, 1200f1723,
/// a39bbfc75, 99f48bc22, a88a74569, d9a6d972f): the card a padlock opens. Offer, Compare and the invite
/// week's last-day Ending face; pure decisions are Core <see cref="VaultOffer"/> / <see cref="VaultSale"/>.
/// Every link and pill is a Button, so a press on it is handled and never starts the window drag
/// (99f48bc22's bug cannot happen) and each is keyboard-reachable.
/// Network bits ride the invites proxy (<see cref="InviteApi.DefaultBaseUrl"/>): a sandbox without a
/// loopback url reads nothing and the card shows normal prices and no supporter line.
/// </summary>
public sealed class VaultGateDialog : Window
{
    public const string PatreonUrl = "https://www.patreon.com/CodeBambi";

    private static readonly IBrush CardBg = B("#FF151528"), RowBg = B("#252542"), Edge = B("#3D3D60"), Pink = B("#FF69B4"),
        Gold = B("#E5C76B"), Good = B("#7FD8A6"), Warn = B("#FFB347"), Text = B("#E0E0E0"), Muted = B("#B8B1CC"), Dim = B("#8079A3");

    private static int? _supporters;
    private static DateTime _supportersReadUtc = DateTime.MinValue;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static bool? _invitesReachable;
    private static DateTime _invitesReadUtc = DateTime.MinValue;
    /// <summary>The live first-month sale (tests set it). Null = normal prices.</summary>
    internal static VaultSaleInfo? Sale;
    private static DateTime _saleReadUtc = DateTime.MinValue;

    /// <summary>The card on screen, if any. One at a time: a second padlock or toast replaces it.</summary>
    internal static VaultGateDialog? Open { get; private set; }

    private readonly StackPanel _body = new();
    private readonly ExclusiveFeature? _feature;
    private readonly int _tier;
    private readonly Action _signIn;
    private readonly PriceCurrency _currency = VaultOffer.LocalCurrency();
    private readonly List<(StackPanel Slot, int Tier, bool Compact, bool Yearly)> _priceSlots = new();
    private bool _yearly;
    private StackPanel? _proofSlot, _codeSlot;

    /// <summary>Render proof (--render-all): the offer face for a Basic feature, no network.</summary>
    internal VaultGateDialog() : this(VaultOffer.Feature("remotecontrol"), 1, () => { }) => BuildOffer();

    private VaultGateDialog(ExclusiveFeature? feature, int tier, Action signIn)
    {
        _feature = feature;
        _tier = Math.Clamp(tier, 1, 2);
        _signIn = signIn;
        Title = "CC Labs";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new Border
        {
            Background = CardBg, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), BorderBrush = Pink,
            Margin = new Thickness(12), Padding = new Thickness(22, 20, 22, 18),
            BoxShadow = BoxShadows.Parse("0 0 22 0 #73FF69B4"), Child = _body,
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // Buttons handle their own press, so only the card's empty surface drags.
        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
    }

    // ============================== entry points ==============================

    /// <summary>The gate card for a feature key (null = the vault as a whole) at a tier.</summary>
    public static VaultGateDialog ShowOffer(Window? owner, string? featureKey, int tier, Action signIn)
    {
        var feature = VaultOffer.Feature(featureKey);
        var dialog = new VaultGateDialog(feature, Math.Max(tier, feature?.Tier ?? 1), signIn);
        dialog.BuildOffer();
        Present(dialog, owner);
        _ = dialog.FillSaleAsync();
        _ = dialog.FillAsyncBits();
        return dialog;
    }

    /// <summary>The last-day card of an invite week.</summary>
    public static VaultGateDialog ShowEnding(Window? owner, DateTime grantUntilUtc, Action signIn)
    {
        var dialog = new VaultGateDialog(null, 1, signIn);
        dialog.BuildEnding(grantUntilUtc);
        Present(dialog, owner);
        _ = dialog.FillSaleAsync();
        return dialog;
    }

    private static void Present(VaultGateDialog dialog, Window? owner)
    {
        Open?.Close();
        Open = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(Open, dialog)) Open = null; };
        if (owner is { IsVisible: true }) dialog.Show(owner); else dialog.Show();
    }

    // ============================== offer ==============================

    private void BuildOffer()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        var lab = _tier >= 2;
        _body.Children.Add(Chip(Loc.Get(lab ? "vaultgate_chip_lab" : "vaultgate_chip_vault"), lab ? Gold : null));
        if (_feature != null) _body.Children.Add(Art(_feature.ArtResource));
        var name = _feature == null ? null : Loc.Get(_feature.TitleLocKey);
        _body.Children.Add(Heading(name == null ? Loc.Get("vaultgate_title_generic")
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

        var links = new Grid { Margin = new Thickness(0, 10, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var signIn = Link(Loc.Get("vaultgate_signin"));
        signIn.HorizontalAlignment = HorizontalAlignment.Left;
        signIn.Click += (_, _) => { Close(); _signIn(); };
        links.Children.Add(signIn);
        var compare = Link(Loc.Get("vaultgate_compare"));
        compare.Click += (_, _) => BuildCompare();
        Grid.SetColumn(compare, 1);
        links.Children.Add(compare);
        _body.Children.Add(links);

        _codeSlot = new StackPanel();
        _body.Children.Add(_codeSlot);
        PaintCodeLink();
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
        _body.Children.Add(CloseLink());
    }

    /// <summary>The supporter count and whether invites exist server-side; the card never waits on either.</summary>
    private async Task FillAsyncBits()
    {
        var url = InviteApi.DefaultBaseUrl();
        try
        {
            if (url != null && DateTime.UtcNow - _supportersReadUtc > TimeSpan.FromHours(6))
            {
                _supportersReadUtc = DateTime.UtcNow;
                using var res = await Http.GetAsync($"{url}/v2/public/supporters");
                if (res.IsSuccessStatusCode) _supporters = VaultOffer.ParseSupporterCount(await res.Content.ReadAsStringAsync());
                PaintProof();
            }
        }
        catch (Exception ex) { Log.Debug("[VaultGate] supporters read failed: {E}", ex.GetType().Name); }

        try
        {
            if (InviteApi.DefaultIdentity() == null) return;
            if (_invitesReachable == null || DateTime.UtcNow - _invitesReadUtc > TimeSpan.FromMinutes(10))
            {
                _invitesReadUtc = DateTime.UtcNow;
                _invitesReachable = (await new InviteApi().MineAsync()).Reachable;
            }
            PaintCodeLink();
        }
        catch (Exception ex) { Log.Debug("[VaultGate] invites probe failed: {E}", ex.GetType().Name); }
    }

    /// <summary>"Have an invite code?" only once the server has invites; it opens the redeem box in place.</summary>
    private void PaintCodeLink()
    {
        if (_invitesReachable != true || _codeSlot == null || !IsVisible || _codeSlot.Children.Count > 0) return;
        var link = Link(Loc.Get("vaultgate_have_code"));
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.Margin = new Thickness(0, 8, 0, 0);
        link.Click += (_, _) =>
        {
            var box = new InvitePanel { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            _codeSlot.Children.Clear();
            _codeSlot.Children.Add(box);
            box.ShowRedeemFace();
        };
        _codeSlot.Children.Add(link);
    }

    /// <summary>Reads the sale switch (cached five minutes); any failure means no sale.</summary>
    private async Task FillSaleAsync()
    {
        var url = InviteApi.DefaultBaseUrl();
        if (url == null || DateTime.UtcNow - _saleReadUtc <= TimeSpan.FromMinutes(5)) return;
        _saleReadUtc = DateTime.UtcNow;
        VaultSaleInfo? sale = null;
        try
        {
            using var res = await Http.GetAsync($"{url}/config/vault-sale");
            if (res.IsSuccessStatusCode) sale = VaultSale.Parse(await res.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { Log.Debug("[VaultGate] sale read failed: {E}", ex.GetType().Name); }
        Sale = sale;
        foreach (var (slot, tier, compact, yearly) in _priceSlots) PaintPrice(slot, tier, compact, yearly);
    }

    private void PaintProof()
    {
        if (_proofSlot == null) return;
        _proofSlot.Children.Clear();
        if (VaultOffer.SupporterFloor(_supporters) is { } floor)
            _proofSlot.Children.Add(Para(Loc.GetF("vaultgate_proof", floor), Text, 12.5, new Thickness(0, 10, 0, 0)));
    }

    // ============================== compare ==============================

    private void BuildCompare()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        _body.Children.Add(Heading(Loc.Get("vaultgate_compare_title")));
        _body.Children.Add(BillingSwitch());
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        grid.Children.Add(TierColumn(1));
        var lab = TierColumn(2);
        Grid.SetColumn(lab, 2);
        grid.Children.Add(lab);
        _body.Children.Add(grid);
        if (_yearly) _body.Children.Add(Para(Loc.Get("vaultgate_yearly_note"), Gold, 12, new Thickness(0, 10, 0, 0)));
        var back = Link(Loc.Get("vaultgate_back"));
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 12, 0, 0);
        back.Click += (_, _) => { BuildOffer(); _ = FillAsyncBits(); };
        _body.Children.Add(back);
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
    }

    private Control TierColumn(int tier)
    {
        var lab = tier >= 2;
        var tierName = Loc.Get(lab ? "vaultgate_tier_lab_name" : "vaultgate_tier_vault_name");
        var col = new StackPanel();
        col.Children.Add(Chip(tierName, lab ? Gold : null));
        col.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_tier_lab_tag" : "vaultgate_tier_vault_tag"), FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.SemiBold, FontSize = 16, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        col.Children.Add(Price(tier, compact: true, yearly: _yearly));
        col.Children.Add(Perks(lab, 12));
        var choose = SmallButton(Loc.GetF("vaultgate_choose", tierName));
        choose.Margin = new Thickness(0, 12, 0, 0);
        choose.HorizontalAlignment = HorizontalAlignment.Stretch;
        choose.Click += (_, _) => { OpenPatreon(lab ? "compare-lab" : "compare-vault"); Close(); };
        col.Children.Add(choose);
        return new Border { Background = RowBg, BorderBrush = lab ? Gold : Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = col };
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
            Background = RowBg, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12, 12, 12, 4),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Loc.Get("vaultgate_ending_bubble"), Foreground = Text, FontSize = 13, TextWrapping = TextWrapping.Wrap },
        });
        _body.Children.Add(Para(Loc.GetF("vaultgate_ending_until", grantUntilUtc.ToLocalTime().ToString("ddd d MMM, HH:mm")),
            Warn, 12.5, new Thickness(0, 10, 0, 0)));
        _body.Children.Add(Price(1));
        _body.Children.Add(YearlyHint(1));
        var keep = PrimaryButton(Loc.Get("vaultgate_ending_keep"));
        keep.Click += (_, _) => { OpenPatreon("ending"); Close(); };
        _body.Children.Add(keep);
        var let = Link(Loc.Get("vaultgate_ending_let_close"));
        let.Margin = new Thickness(0, 12, 0, 0);
        let.Click += (_, _) => Close();
        _body.Children.Add(let);
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
    }

    // ============================== bits ==============================

    private static void OpenPatreon(string from)
    {
        Log.Information("[VaultGate] open Patreon from {From}", from);
        if (!ExternalOpener.Open(PatreonUrl)) Log.Warning("[VaultGate] Patreon link refused or failed");
    }

    private static Control Chip(string text, IBrush? tint) => new Border
    {
        CornerRadius = new CornerRadius(99), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left,
        Background = tint is ISolidColorBrush s ? new SolidColorBrush(s.Color, 0.16) : B("#24FF69B4"),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = tint ?? Pink },
    };

    private static Control Art(string? resource)
    {
        var host = new Border { Height = 150, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true, Background = RowBg };
        if (Helpers.ModArt.TryLoad(Tabs.ExclusivesTabView.ArtName(resource), 840) is { } art)
            host.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        return host;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text, FontFamily = FriendsDrawer.Display, FontWeight = FontWeight.SemiBold, FontSize = 21, Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
    };

    private static TextBlock Para(string text, IBrush? brush = null, double size = 13, Thickness? margin = null) => new()
    {
        Text = text, Foreground = brush ?? Muted, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 4, 0, 0),
    };

    /// <summary>What each tier really adds (d9a6d972f's audit): Basic over free, Prime over Basic.</summary>
    private static readonly string[] BasicPerks =
        { "vaultgate_basic_1", "vaultgate_basic_2", "vaultgate_basic_3", "vaultgate_basic_4", "vaultgate_basic_5", "vaultgate_basic_6", "vaultgate_basic_7", "vaultgate_basic_8" };
    private static readonly string[] PrimePerks =
        { "vaultgate_prime_1", "vaultgate_prime_2", "vaultgate_prime_3", "vaultgate_prime_4", "vaultgate_prime_5", "vaultgate_prime_6", "vaultgate_prime_7" };

    private static Control Perks(bool lab, double size)
    {
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        list.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_prime_head" : "vaultgate_basic_head"), Foreground = Muted, FontSize = size - 0.5,
            FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var key in lab ? PrimePerks : BasicPerks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2), ColumnDefinitions = new ColumnDefinitions("13,*") };
            row.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse
            {
                Width = 5, Height = 5, Fill = lab ? Gold : Pink, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, size * 0.47, 0, 0),
            });
            var line = new TextBlock { Text = Loc.Get(key), Foreground = Text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            list.Children.Add(row);
        }
        return list;
    }

    private Control Price(int tier, bool compact = false, bool yearly = false)
    {
        var slot = new StackPanel { Margin = new Thickness(0, compact ? 8 : 12, 0, compact ? 0 : 4) };
        _priceSlots.Add((slot, tier, compact, yearly));
        PaintPrice(slot, tier, compact, yearly);
        return slot;
    }

    /// <summary>The price, or during a first-month sale the sale price beside the struck normal one,
    /// the "then" line and the end date. Yearly never shows a sale.</summary>
    private void PaintPrice(StackPanel slot, int tier, bool compact, bool yearly)
    {
        slot.Children.Clear();
        var price = VaultOffer.PriceFor(tier, _currency);
        var sale = VaultSale.AppliesTo(Sale, tier, DateTime.UtcNow, yearly) ? Sale : null;
        var line = new WrapPanel();
        line.Children.Add(new TextBlock
        {
            Text = VaultOffer.Money(sale != null ? VaultSale.FirstMonthCents(price.MonthlyCents, sale.Percent)
                : yearly ? price.YearlyCents : price.MonthlyCents, _currency),
            FontFamily = FriendsDrawer.Display, FontWeight = FontWeight.SemiBold, FontSize = compact ? 22 : 28, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (sale != null)
            line.Children.Add(new TextBlock
            {
                Text = VaultOffer.Money(price.MonthlyCents, _currency), TextDecorations = TextDecorations.Strikethrough, Tag = "struck",
                FontFamily = FriendsDrawer.Display, Foreground = Dim, FontSize = compact ? 14 : 17, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, compact ? 3 : 4),
            });
        line.Children.Add(new TextBlock
        {
            Text = sale != null ? Loc.Get("vaultgate_sale_first")
                : yearly ? Loc.GetF("vaultgate_price_year", VaultOffer.YearlyPerMonth(price))
                : Loc.GetF("vaultgate_price", VaultOffer.PerDay(price)),
            Foreground = Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5), TextWrapping = TextWrapping.Wrap,
        });
        slot.Children.Add(line);
        if (sale == null) return;
        slot.Children.Add(Para(Loc.GetF("vaultgate_sale_line", sale.Percent, VaultOffer.Money(price.MonthlyCents, _currency)),
            Good, compact ? 12 : 12.5, new Thickness(0, 2, 0, 0)));
        if (VaultSale.EndsText(sale) is string ends)
            slot.Children.Add(Para(Loc.GetF("vaultgate_sale_ends", ends), Dim, compact ? 11.5 : 12, new Thickness(0, 2, 0, 0)));
    }

    private Control YearlyHint(int tier)
        => Para(Loc.GetF("vaultgate_yearly_hint", VaultOffer.Money(VaultOffer.PriceFor(tier, _currency).YearlyCents, _currency)),
            Gold, 12, new Thickness(0, 0, 0, 10));

    /// <summary>Monthly / Yearly pills on the compare face. Rebuilds the face on a switch.</summary>
    private Control BillingSwitch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var yearly in new[] { false, true })
        {
            var on = yearly == _yearly;
            var pill = Bare(new Border
            {
                CornerRadius = new CornerRadius(99), Padding = new Thickness(12, 5, 12, 5),
                Background = on ? RowBg : Brushes.Transparent, BorderBrush = on ? Gold : Edge, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = Loc.Get(yearly ? "vaultgate_billing_yearly" : "vaultgate_billing_monthly"), Foreground = on ? Gold : Muted, FontSize = 12, FontWeight = FontWeight.SemiBold },
            });
            pill.Margin = new Thickness(0, 0, 6, 0);
            var choice = yearly;
            pill.Click += (_, _) => { if (_yearly != choice) { _yearly = choice; BuildCompare(); } };
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
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        return b;
    }

    /// <summary>The theme's pink pill; text in a TextBlock (Avalonia reads "_" in Content as an access key).</summary>
    private static Button SmallButton(string text)
    {
        var b = new Button { Content = new TextBlock { Text = text }, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("SmallPinkButton", out var t) == true && t is ControlTheme theme) b.Theme = theme;
        return b;
    }

    /// <summary>A button that draws only its content: WPF's hand-cursor TextBlocks/Borders, made focusable.
    /// Built on first use: a Cursor needs the platform, and a type initializer must not (reflection scans touch it).</summary>
    private static ControlTheme? _bareTheme;
    private static ControlTheme BareTheme => _bareTheme ??= new(typeof(Button))
    {
        Setters =
        {
            new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((b, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter", Background = Brushes.Transparent,
                [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
            })),
            new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)),
        },
    };

    private static Button Bare(Control content) => new() { Theme = BareTheme, Content = content };

    private static Button Link(string text)
    {
        var b = Bare(new TextBlock { Text = text, Foreground = Muted, FontSize = 12, TextDecorations = TextDecorations.Underline, TextWrapping = TextWrapping.Wrap });
        b.HorizontalAlignment = HorizontalAlignment.Center;
        return b;
    }

    private static TextBlock Fine(string text) => new()
    {
        Text = text, Foreground = Dim, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
    };

    private Button CloseLink()
    {
        var l = Link(Loc.Get("vaultgate_close"));
        l.Margin = new Thickness(0, 8, 0, 0);
        l.Click += (_, _) => Close();
        return l;
    }

    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
}
