using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace CcpClient.Desktop.Features;

/// <summary>
/// Demonstrator feature popup (board row "Prove feature-popup scrolling") — the SP-007
/// demonstrator card's left-click settings popup as a REAL window implementing the W-04
/// contract (window-behavior-manifest; WPF evidence FeaturePopupWindow.xaml(.cs),
/// MainWindow.Presets.cs:846-873): owned modeless, taskbar-absent, non-resizable,
/// popup-LOCAL chrome with title-bar drag, Escape ≡ close button through ONE operation,
/// owner-monitor working-area capping, focus restoration via <see cref="FeaturePopupManager"/>.
/// Explicitly a DEMONSTRATOR: superseded by the first real feature popup; does NOT
/// discharge W-04's exercise gate. Height constants are WPF-parity, pending-owner.
/// </summary>
public partial class FeaturePopupWindow : Window, FeaturePopupManager.IPopup
{
    public enum ContentVariant
    {
        Tall,
        Short,
        Nested,
    }

    private Window? _owner;
    private PixelRect _capScreenBounds;
    private double _capScaling = 1.0;
    private string? _lastProbe;

    public FeaturePopupWindow()
    {
        InitializeComponent();
        SetVariant(ContentVariant.Tall); // default: the capping/scrolling case

        CloseButton.Click += (_, _) => ClosePopup();

        // Popup-LOCAL title-bar drag (WPF xaml.cs:60-66 parity; 12.1.0 Window.BeginMoveDrag).
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        VariantTall.Click += (_, _) => SetVariant(ContentVariant.Tall);
        VariantShort.Click += (_, _) => SetVariant(ContentVariant.Short);
        VariantNested.Click += (_, _) => SetVariant(ContentVariant.Nested);

        PopupScroller.ScrollChanged += (_, _) => UpdateScrollProbe();
        LayoutUpdated += (_, _) => UpdatePopupProbe();
    }

    /// <summary>THE one close operation (W-04): title-bar button and Escape both land here.</summary>
    public void ClosePopup() => Close();

    /// <summary>
    /// Escape closes the popup (WPF PreviewKeyDown tunnel parity, xaml.cs:45-58). A window
    /// KeyBinding was tried first and does NOT fire here: KeyBinding objects in
    /// Window.KeyBindings don't inherit DataContext, so the compiled CloseCommand binding
    /// never resolved (found by the headless test). The tunnel override is also the WPF
    /// shape. Both routes terminate in <see cref="ClosePopup"/> — ONE operation.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            ClosePopup();
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Swap the synthetic content variant and re-fit the popup height (compact short content; capped tall content).</summary>
    public void SetVariant(ContentVariant variant)
    {
        ContentHost.Content = variant switch
        {
            ContentVariant.Short => SyntheticPopupContent.BuildShort(),
            ContentVariant.Nested => SyntheticPopupContent.BuildNested(),
            _ => SyntheticPopupContent.BuildTall(),
        };
        PopupScroller.Offset = new Vector(0, 0);
        if (_owner is not null)
        {
            ApplyFitAndCap();
        }

        UpdateScrollProbe();
    }

    /// <summary>Modeless owned show (12.1.0 <c>Window.Show(Window)</c>: "Shows the window as a child of owner").</summary>
    public void ShowOwned(Window owner)
    {
        _owner = owner;
        ApplyFitAndCap();
        Show(owner);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Position AFTER first layout: the real (fitted) size is known only now; Position is
        // physical pixels (pre-approach consult). Centered on the owner, clamped into the
        // OWNER monitor's working area (manifest §6 constraint 4).
        ClampPositionIntoOwnerWorkingArea();
        if (_owner is not null)
        {
            _owner.PositionChanged += OnOwnerPositionChanged;
        }

        UpdateScrollProbe();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_owner is not null)
        {
            _owner.PositionChanged -= OnOwnerPositionChanged;
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Owner-monitor capping (capability-inventory §Feature-popup behavior): cap = WPF-parity
    /// constants against the WORKING AREA of the monitor containing the owner, never the
    /// primary monitor by default. Recomputed at open, on variant switch, and on owner
    /// monitor/scaling change — never on popup drag (a non-resizable window must not
    /// visibly resize mid-drag; pre-approach consult).
    /// </summary>
    private void ApplyFitAndCap()
    {
        if (_owner is null)
        {
            return;
        }

        var screen = _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        _capScreenBounds = screen.Bounds;
        _capScaling = screen.Scaling;
        var workingArea = screen.WorkingArea; // physical pixels (12.1.0 XML docs)
        Width = PopupPlacement.FitWidthDip(workingArea.Width, screen.Scaling);
        MaxHeight = PopupPlacement.CapHeightDip(workingArea.Height, screen.Scaling);
        Height = PopupPlacement.FitHeightDip(MeasureDesiredContentHeight(), workingArea.Height, screen.Scaling);
    }

    /// <summary>
    /// Desired height = chrome + content measured at the popup width. Pre-approach consult:
    /// SizeToContent=Height fights programmatic MaxHeight (resets to Manual) — measure and
    /// set Height directly instead. Scroller padding/border are included; the scrollbar
    /// reserve is not (approximation, recorded — acceptance is capping + reachability,
    /// not pixel-perfect fit).
    /// </summary>
    private double MeasureDesiredContentHeight()
    {
        var width = Math.Max(50, Width - 2 - 32);
        var contentHeight = 0.0;
        if (ContentHost.Content is Control content)
        {
            content.Measure(new Size(width, double.PositiveInfinity));
            contentHeight = content.DesiredSize.Height;
        }

        TitleBar.Measure(new Size(Width, double.PositiveInfinity));
        VariantBar.Measure(new Size(Width, double.PositiveInfinity));
        ProbeFooter.Measure(new Size(Width, double.PositiveInfinity));
        return TitleBar.DesiredSize.Height + VariantBar.DesiredSize.Height
            + contentHeight + 32 // scroller vertical padding
            + ProbeFooter.DesiredSize.Height + 2; // border
    }

    private void ClampPositionIntoOwnerWorkingArea()
    {
        if (_owner is null)
        {
            return;
        }

        var screen = _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var ownerRect = new PixelRect(_owner.Position, PixelSize.FromSize(_owner.ClientSize, _owner.RenderScaling));
        var popupSize = PixelSize.FromSize(ClientSize, RenderScaling);
        Position = PopupPlacement.CenteredClampedPosition(ownerRect, popupSize, screen.WorkingArea);
    }

    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_owner is null)
        {
            return;
        }

        var screen = _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
        if (screen is null || (screen.Bounds == _capScreenBounds && Math.Abs(screen.Scaling - _capScaling) < 0.001))
        {
            return;
        }

        // The owner crossed monitors (or the monitor's scaling changed): re-cap and re-clamp.
        ApplyFitAndCap();
        ClampPositionIntoOwnerWorkingArea();
    }

    /// <summary>
    /// The observable scrolling-evidence channel (row verification language): changing
    /// Extent/Viewport/Offset plus whether the final control is inside the viewport —
    /// UIA-readable, same pattern as the SP-007 layout probe.
    /// </summary>
    private void UpdateScrollProbe()
    {
        ScrollProbeText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"scroll-probe: extent {PopupScroller.Extent.Height:F1} viewport {PopupScroller.Viewport.Height:F1} offset {PopupScroller.Offset.Y:F1} final-in-viewport {(IsFinalControlInViewport() ? "true" : "false")}");
    }

    private bool IsFinalControlInViewport()
    {
        var final = this.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => AutomationProperties.GetName(c) == SyntheticPopupContent.FinalControlName);
        if (final is null || PopupScroller.Viewport.Height <= 0)
        {
            return false;
        }

        var topLeft = final.TranslatePoint(new Point(0, 0), PopupScroller);
        if (topLeft is null)
        {
            return false;
        }

        return new Rect(topLeft.Value, final.Bounds.Size)
            .Intersects(new Rect(0, 0, PopupScroller.Viewport.Width, PopupScroller.Viewport.Height));
    }

    /// <summary>Popup/scrollbar/thumb screen geometry (physical px) for the headed harness's real-input clicks.</summary>
    private void UpdatePopupProbe()
    {
        var size = PixelSize.FromSize(ClientSize, RenderScaling);
        var scrollbar = PopupScroller.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault();
        var thumb = PopupScroller.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();

        string Geometry(Control? control)
        {
            if (control is null)
            {
                return "none";
            }

            var point = control.PointToScreen(new Point(0, 0));
            var controlSize = PixelSize.FromSize(control.Bounds.Size, RenderScaling);
            return string.Create(CultureInfo.InvariantCulture, $"{point.X},{point.Y},{controlSize.Width},{controlSize.Height}");
        }

        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"popup-probe: pos {Position.X},{Position.Y} size {size.Width}x{size.Height} scale {RenderScaling:0.##} scroller {Geometry(PopupScroller)} scrollbar {Geometry(scrollbar)} thumb {Geometry(thumb)}");
        if (text != _lastProbe)
        {
            _lastProbe = text;
            PopupProbeText.Text = text;
        }
    }
}
