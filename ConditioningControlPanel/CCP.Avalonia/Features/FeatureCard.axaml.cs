using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Theme;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Click-to-open tile for a feature on the dashboard grid. Shows an icon + title;
/// when locked, desaturates the content and overlays a padlock + required level.
/// </summary>
public partial class FeatureCard : UserControl
{
    private string _title = "Feature";
    public static readonly DirectProperty<FeatureCard, string> TitleProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string>(
            nameof(Title), o => o.Title, (o, v) => o.Title = v);

    private Bitmap? _icon;
    public static readonly DirectProperty<FeatureCard, Bitmap?> IconProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, Bitmap?>(
            nameof(Icon), o => o.Icon, (o, v) => o.Icon = v);

    private string? _glyph;
    public static readonly DirectProperty<FeatureCard, string?> GlyphProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(Glyph), o => o.Glyph, (o, v) => o.Glyph = v);

    private int _lockLevel;
    public static readonly DirectProperty<FeatureCard, int> LockLevelProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, int>(
            nameof(LockLevel), o => o.LockLevel, (o, v) => o.LockLevel = v);

    private bool _isLocked;
    public static readonly DirectProperty<FeatureCard, bool> IsLockedProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, bool>(
            nameof(IsLocked), o => o.IsLocked, (o, v) => o.IsLocked = v);

    private bool _isActive;
    public static readonly DirectProperty<FeatureCard, bool> IsActiveProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, bool>(
            nameof(IsActive), o => o.IsActive, (o, v) => o.IsActive = v);

    private string? _helpSectionId;
    public static readonly DirectProperty<FeatureCard, string?> HelpSectionIdProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(HelpSectionId), o => o.HelpSectionId, (o, v) => o.HelpSectionId = v);

    private bool _canTest;
    public static readonly DirectProperty<FeatureCard, bool> CanTestProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, bool>(
            nameof(CanTest), o => o.CanTest, (o, v) => o.CanTest = v);

    private string? _iconUri;
    public static readonly DirectProperty<FeatureCard, string?> IconUriProperty =
        AvaloniaProperty.RegisterDirect<FeatureCard, string?>(
            nameof(IconUri), o => o.IconUri, (o, v) => o.IconUri = v);

    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ToggleRequestedEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(ToggleRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> TestRequestedEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(TestRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> HelpRequestedEvent =
        RoutedEvent.Register<FeatureCard, RoutedEventArgs>(nameof(HelpRequested), RoutingStrategies.Bubble);

    public string Title
    {
        get => _title;
        set => SetAndRaise(TitleProperty, ref _title, value);
    }

    public Bitmap? Icon
    {
        get => _icon;
        set => SetAndRaise(IconProperty, ref _icon, value);
    }

    public string? Glyph
    {
        get => _glyph;
        set => SetAndRaise(GlyphProperty, ref _glyph, value);
    }

    /// <summary>Required level for this feature. 0 means always unlocked.</summary>
    public int LockLevel
    {
        get => _lockLevel;
        set => SetAndRaise(LockLevelProperty, ref _lockLevel, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetAndRaise(IsLockedProperty, ref _isLocked, value);
    }

    /// <summary>
    /// Highlights the card with a pink glow + border when the underlying
    /// feature is enabled in settings.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetAndRaise(IsActiveProperty, ref _isActive, value);
    }

    /// <summary>
    /// ID of the help section whose tooltip should be shown when hovering the "?" icon.
    /// Null/empty IDs hide the icon entirely.
    /// </summary>
    public string? HelpSectionId
    {
        get => _helpSectionId;
        set => SetAndRaise(HelpSectionIdProperty, ref _helpSectionId, value);
    }

    /// <summary>
    /// When true, the right-click context menu exposes a "Test" option in addition
    /// to the toggle. The dashboard wires this to a feature-specific test action.
    /// </summary>
    public bool CanTest
    {
        get => _canTest;
        set => SetAndRaise(CanTestProperty, ref _canTest, value);
    }

    /// <summary>
    /// Optional legacy pack:// or avares:// URI for the card background image.
    /// When set, this is loaded and assigned to <see cref="Icon"/>.
    /// </summary>
    public string? IconUri
    {
        get => _iconUri;
        set => SetAndRaise(IconUriProperty, ref _iconUri, value);
    }

    public event EventHandler<RoutedEventArgs> Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    /// <summary>
    /// Raised on right-click so the dashboard can quick-toggle the underlying
    /// feature on/off without opening its config popup. Left-click still opens
    /// the popup via <see cref="Click"/>.
    /// </summary>
    public event EventHandler<RoutedEventArgs> ToggleRequested
    {
        add => AddHandler(ToggleRequestedEvent, value);
        remove => RemoveHandler(ToggleRequestedEvent, value);
    }

    /// <summary>
    /// Raised when the user selects "Test" from the right-click context menu.
    /// Only available when <see cref="CanTest"/> is true.
    /// </summary>
    public event EventHandler<RoutedEventArgs> TestRequested
    {
        add => AddHandler(TestRequestedEvent, value);
        remove => RemoveHandler(TestRequestedEvent, value);
    }

    /// <summary>
    /// Raised when the user clicks the "?" help button on the card.
    /// </summary>
    public event EventHandler<RoutedEventArgs> HelpRequested
    {
        add => AddHandler(HelpRequestedEvent, value);
        remove => RemoveHandler(HelpRequestedEvent, value);
    }

    static FeatureCard()
    {
        TitleProperty.Changed.AddClassHandler<FeatureCard>(OnTitleChanged);
        IconProperty.Changed.AddClassHandler<FeatureCard>(OnIconChanged);
        GlyphProperty.Changed.AddClassHandler<FeatureCard>(OnGlyphChanged);
        LockLevelProperty.Changed.AddClassHandler<FeatureCard>(OnLockStateChanged);
        IsLockedProperty.Changed.AddClassHandler<FeatureCard>(OnLockStateChanged);
        IsActiveProperty.Changed.AddClassHandler<FeatureCard>(OnActiveStateChanged);
        HelpSectionIdProperty.Changed.AddClassHandler<FeatureCard>(OnHelpSectionIdChanged);
        IconUriProperty.Changed.AddClassHandler<FeatureCard>(OnIconUriChanged);
    }

    public FeatureCard()
    {
        InitializeComponent();
        RootBorder.PointerPressed += OnPointerPressed;
        ApplyLockState();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The active-state glow/border is applied imperatively over the XAML
        // DynamicResource, so it must be re-applied on a live theme/mod switch or
        // the outer border keeps the previous theme's accent. Subscribe on load and
        // unsubscribe on unload to avoid leaking a handler on the singleton service.
        var themeService = App.Services?.GetService<AvaloniaThemeService>();
        if (themeService != null)
            themeService.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        var themeService = App.Services?.GetService<AvaloniaThemeService>();
        if (themeService != null)
            themeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyActiveState();
    }

    private static void OnTitleChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtTitle.Text = c.Title ?? "";
    }

    private static void OnIconChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        var src = c.Icon;
        if (src is not null)
        {
            c.ImgIconHost.Background = new ImageBrush(src)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = AlignmentY.Center
            };
            c.ImgIconHost.IsVisible = true;
            c.GlyphHost.IsVisible = false;
        }
        else
        {
            c.ImgIconHost.Background = null;
            if (!string.IsNullOrEmpty(c.Glyph))
            {
                c.ImgIconHost.IsVisible = false;
                c.GlyphHost.IsVisible = true;
            }
        }
    }

    private static void OnGlyphChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.TxtGlyph.Text = c.Glyph ?? "";
        if (c.Icon is null && !string.IsNullOrEmpty(c.Glyph))
        {
            c.GlyphHost.IsVisible = true;
            c.ImgIconHost.IsVisible = false;
        }
        else if (string.IsNullOrEmpty(c.Glyph))
        {
            c.GlyphHost.IsVisible = false;
        }
    }

    private static void OnLockStateChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyLockState();
    }

    private static void OnActiveStateChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.ApplyActiveState();
    }

    private static void OnHelpSectionIdChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.RefreshHelpState();
    }

    private static void OnIconUriChanged(FeatureCard c, AvaloniaPropertyChangedEventArgs e)
    {
        c.Icon = LoadBitmapFromUri(c.IconUri);
    }

    private static Bitmap? LoadBitmapFromUri(string? uri)
    {
        return AvaloniaBitmapHelper.Load(uri);
    }

    private void RefreshHelpState()
    {
        BtnHelp.IsVisible = !string.IsNullOrWhiteSpace(HelpSectionId);
    }

    private void ApplyLockState()
    {
        if (IsLocked)
        {
            LockedOverlay.IsVisible = true;
            TxtLockLabel.Text = LockLevel > 0
                ? Core.Localization.LocalizationManager.Instance.GetF("label_lvl_0", LockLevel)
                : Core.Localization.LocalizationManager.Instance.Get("label_locked");
            ContentRoot.Opacity = 0.35;
        }
        else
        {
            LockedOverlay.IsVisible = false;
            ContentRoot.Opacity = 1.0;
        }
        ApplyActiveState();
    }

    private void ApplyActiveState()
    {
        // Active state is suppressed while the card is locked — a locked feature
        // can't really be "on" even if the underlying setting is true.
        var showActive = IsActive && !IsLocked;
        ActiveBorder.IsVisible = showActive;

        if (showActive)
        {
            RootBorder.BorderBrush = FindBrush("PinkBrush");
            RootBorder.BorderThickness = new Thickness(2.5);
            RootBorder.BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 20,
                Spread = 2,
                Color = FindColor("TransparentPink50")
            });
        }
        else
        {
            RootBorder.BorderBrush = FindBrush("GlassBorderBrush") ?? FindBrush("PanelAccentBrush");
            RootBorder.BorderThickness = new Thickness(1);
            RootBorder.BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, Color = Colors.Transparent });
        }
    }

    private static IBrush? FindBrush(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var value) == true && value is IBrush brush)
            return brush;
        return null;
    }

    private static Color FindColor(string key)
    {
        if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var value) == true)
        {
            if (value is Color c) return c;
            if (value is ISolidColorBrush b) return b.Color;
        }
        return Colors.Transparent;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Swallow presses that originate inside the help button so the user
        // can hover/click the "?" without also opening the feature popup.
        if (e.Source is Visual src && IsDescendantOf(src, BtnHelp))
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed && !IsLocked)
        {
            e.Handled = true;
            // Shift+right-click or CanTest shows context menu; plain right-click toggles directly
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || CanTest)
            {
                OpenContextMenu();
            }
            else
            {
                RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent, this));
            }
        }
        else
        {
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }

    private void BtnHelp_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(HelpRequestedEvent, this));
    }

    private void OpenContextMenu()
    {
        var menu = new ContextMenu();

        var toggleHeader = IsActive
            ? Core.Localization.LocalizationManager.Instance.Get("btn_disable") ?? "Turn off"
            : Core.Localization.LocalizationManager.Instance.Get("btn_enable") ?? "Turn on";
        var toggleItem = new MenuItem { Header = toggleHeader };
        toggleItem.Click += (_, _) => RaiseEvent(new RoutedEventArgs(ToggleRequestedEvent, this));
        menu.Items.Add(toggleItem);

        if (CanTest)
        {
            var testHeader = Core.Localization.LocalizationManager.Instance.Get("btn_test_2") ?? "Test";
            var testItem = new MenuItem { Header = testHeader };
            testItem.Click += (_, _) => RaiseEvent(new RoutedEventArgs(TestRequestedEvent, this));
            menu.Items.Add(testItem);
        }

        menu.Open(this);
    }

    private static bool IsDescendantOf(Visual? node, Visual ancestor)
    {
        if (node is null) return false;
        if (ReferenceEquals(node, ancestor)) return true;
        return node.GetVisualAncestors().Any(v => ReferenceEquals(v, ancestor));
    }
}
