using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// THE DOCK CHIP: a 40 px pink ring at the bottom of the nav rail with EMI's live face in it.
/// Click summons her, click again sends her away.
///
/// <para><b>Self-wiring, like <c>DescentFuseRailChip</c> and <c>SpiralRailHost</c>.</b>
/// MainWindow.xaml adds the element and nothing else: no init call, no line in the startup
/// sequence, no partial to keep in step. Everything below hangs off Loaded / Unloaded.</para>
///
/// <para><b>The face is BOUND, not polled.</b> <c>EmiFace.Face</c> is a dependency property, so the
/// chip's mini face is a one-way binding onto the widget's own face element and mirrors every chain
/// frame for free. A 120 ms poll would have been the obvious alternative and it would have run
/// forever, including while she is away.</para>
///
/// <para><b>It is not a door.</b> No NavDoorMap row, no medallion shape, no "you are here" ring:
/// EMI is not a tab you can be inside. See the note in EmiDock.xaml about the markup
/// NavRailFlyoutTests counts.</para>
///
/// <para><b>THE KNOCK.</b> Once, ever, on a settled first launch, the ring pulses pink three times
/// over about six seconds and then is quiet forever. That is the entire discovery mechanism for a
/// feature that otherwise ships switched on and completely silent. The decision to knock is not
/// taken here - it belongs to <see cref="EmiKnockMachine"/>, which is pure and testable - and this
/// control only ever paints the answer.</para>
/// </summary>
public partial class EmiDock : UserControl
{
    private EmiDeskService? _svc;
    private bool _wired;

    /// <summary>True only while the six seconds of pulses are running. See the knock section below.</summary>
    private bool _knocking;

    /// <summary>Builds the chip. Everything live is wired on Loaded so a designer instance is inert.</summary>
    public EmiDock()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        BtnChip.Click += OnChipClick;
        MiniFace.Draw(EmiChains.RestFace);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyText();

            _svc = App.EmiDesk;
            if (_svc == null)
            {
                // The service failed to construct this run. Leave the chip visible but inert
                // rather than blanking a hole in the rail.
                Log.Debug("[EmiDesk] dock chip loaded with no service");
                return;
            }
            if (!_wired)
            {
                _svc.OutChanged += OnOutChanged;
                _svc.KnockRequested += OnKnockRequested;
                _wired = true;
            }
            Refresh(_svc.IsOut);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] dock chip load failed");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_svc != null && _wired)
            {
                _svc.OutChanged -= OnOutChanged;
                _svc.KnockRequested -= OnKnockRequested;
                _wired = false;
            }

            // An animation left running against a control the rail has thrown away keeps the whole
            // subtree alive and repainting.
            StopKnock();
            // The rail rebuilds this control on some layout paths; a live binding onto a window
            // that may be torn down would keep it alive.
            BindingOperations.ClearBinding(MiniFace, EmiFace.FaceProperty);
            BindingOperations.ClearBinding(MiniFace, EmiFace.SmallProperty);
            BindingOperations.ClearBinding(MiniFace, EmiFace.FlatProperty);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip unload failed");
        }
    }

    private void ApplyText()
    {
        try
        {
            TxtName.Text = Loc.Get("emi_desk_dock_name");
            TxtMuted.Text = Loc.Get("emi_desk_dock_muted_pill");
            BtnChip.ToolTip = Loc.Get("emi_desk_dock_tooltip");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip text failed");
        }
    }

    private void OnOutChanged(object? sender, bool isOut)
    {
        try
        {
            if (Dispatcher.CheckAccess()) Refresh(isOut);
            else Dispatcher.BeginInvoke(new Action(() => Refresh(isOut)));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip OutChanged failed");
        }
    }

    /// <summary>Point the mini face at the live widget (or let it rest) and show or hide the pill.</summary>
    private void Refresh(bool isOut)
    {
        try
        {
            // ANY route out - the chip, the chord, the tray, an automated summon - answers the
            // knock, so the pulses stop here and not only in the click handler below.
            if (isOut) StopKnock();

            var face = App.EmiDesk?.Window?.Face;
            if (isOut && face != null)
            {
                Bind(EmiFace.FaceProperty, nameof(EmiFace.Face), face);
                Bind(EmiFace.SmallProperty, nameof(EmiFace.Small), face);
                Bind(EmiFace.FlatProperty, nameof(EmiFace.Flat), face);
            }
            else
            {
                BindingOperations.ClearBinding(MiniFace, EmiFace.FaceProperty);
                BindingOperations.ClearBinding(MiniFace, EmiFace.SmallProperty);
                BindingOperations.ClearBinding(MiniFace, EmiFace.FlatProperty);
                MiniFace.Draw(EmiChains.RestFace);
            }

            // The pill states a FACT about right now, so it asks the same gate the tube asks: it is
            // never shown just because the setting is on.
            bool muted = App.EmiDesk?.AvatarMuted == true;
            TxtMuted.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip refresh failed");
        }
    }

    private void Bind(DependencyProperty dp, string path, object source)
    {
        BindingOperations.SetBinding(MiniFace, dp, new Binding(path)
        {
            Source = source,
            Mode = BindingMode.OneWay
        });
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        try
        {
            e.Handled = true;
            StopKnock();
            var svc = App.EmiDesk;
            if (svc == null)
            {
                Log.Debug("[EmiDesk] dock chip clicked with no service");
                return;
            }
            svc.Toggle();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] dock chip click failed");
        }
    }

    // ============================================================================================
    //  THE KNOCK
    // ============================================================================================

    /// <summary>One pulse: a fast swell and a slow fall. Three of these is the whole knock.</summary>
    private const int PulseMs = 2000;

    /// <summary>Pulses. Three reads as deliberate; more reads as a notification badge.</summary>
    private const int PulseCount = 3;

    /// <summary>Her pink at rest.</summary>
    private static readonly Color RestPink = Color.FromRgb(0xFF, 0x69, 0xB4);

    /// <summary>...and the brighter pink each swell reaches.</summary>
    private static readonly Color HotPink = Color.FromRgb(0xFF, 0xC4, 0xE8);

    /// <summary>The ring's resting stroke, restored by hand when the pulses are taken off.</summary>
    private const double RestThickness = 2.0;

    private void OnKnockRequested(object? sender, EventArgs e)
    {
        try
        {
            if (Dispatcher.CheckAccess()) StartKnock();
            else Dispatcher.BeginInvoke(new Action(StartKnock));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip knock request failed");
        }
    }

    /// <summary>
    /// Three pink pulses over about six seconds, and then quiet forever.
    ///
    /// <para><b>The ring, never the face.</b> <c>MiniFace</c> draws a live kaomoji that is already
    /// mirroring the widget's own chain frames; pulsing it on top of that reads as a rendering
    /// fault rather than as her knocking. Everything below touches the ellipse's stroke colour, its
    /// thickness and its glow, and nothing else.</para>
    ///
    /// <para><b>A designer instance is inert</b>, the same rule the rest of this control documents:
    /// nothing starts unless the chip is really loaded into a real window.</para>
    /// </summary>
    private void StartKnock()
    {
        try
        {
            if (_knocking) return;
            if (!IsLoaded) return;
            if (DesignerProperties.GetIsInDesignMode(this)) return;

            // A brush or an effect that WPF froze on the way in throws the instant an animation is
            // applied to it. Inline XAML instances are not normally frozen, but a rail rebuild or a
            // resource-sharing change upstream could make them so, and a decorative pulse must never
            // be the thing that takes the nav rail down.
            if (RingStroke.IsFrozen) Ring.Stroke = RingStroke.Clone();
            if (RingGlow.IsFrozen) Ring.Effect = RingGlow.Clone();

            if (Ring.Stroke is not SolidColorBrush stroke) return;
            if (Ring.Effect is not DropShadowEffect glow) return;

            _knocking = true;

            var span = TimeSpan.FromMilliseconds(PulseMs);
            var repeat = new RepeatBehavior(PulseCount);

            // The swell is fast and the fall is slow: a pulse that decays reads as a knock, a pulse
            // that is symmetrical reads as a warning light.
            var colour = new ColorAnimationUsingKeyFrames { Duration = span, RepeatBehavior = repeat };
            colour.KeyFrames.Add(new LinearColorKeyFrame(RestPink, KeyTime.FromPercent(0.0)));
            colour.KeyFrames.Add(new LinearColorKeyFrame(HotPink, KeyTime.FromPercent(0.18)));
            colour.KeyFrames.Add(new LinearColorKeyFrame(RestPink, KeyTime.FromPercent(0.55)));
            colour.KeyFrames.Add(new LinearColorKeyFrame(RestPink, KeyTime.FromPercent(1.0)));

            var glowOpacity = new DoubleAnimationUsingKeyFrames { Duration = span, RepeatBehavior = repeat };
            glowOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            glowOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.95, KeyTime.FromPercent(0.18)));
            glowOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.55)));
            glowOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));

            var blur = new DoubleAnimationUsingKeyFrames { Duration = span, RepeatBehavior = repeat };
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(16.0, KeyTime.FromPercent(0.18)));
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.55)));
            blur.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));

            var thickness = new DoubleAnimationUsingKeyFrames { Duration = span, RepeatBehavior = repeat };
            thickness.KeyFrames.Add(new LinearDoubleKeyFrame(RestThickness, KeyTime.FromPercent(0.0)));
            thickness.KeyFrames.Add(new LinearDoubleKeyFrame(3.2, KeyTime.FromPercent(0.18)));
            thickness.KeyFrames.Add(new LinearDoubleKeyFrame(RestThickness, KeyTime.FromPercent(0.55)));
            thickness.KeyFrames.Add(new LinearDoubleKeyFrame(RestThickness, KeyTime.FromPercent(1.0)));

            // ONE of the four carries the tidy-up. Completed fires once the whole repeat count is
            // spent, and StopKnock is idempotent, so a click landing mid-pulse and the natural end
            // both arrive at the same place.
            colour.Completed += OnKnockFinished;

            stroke.BeginAnimation(SolidColorBrush.ColorProperty, colour);
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOpacity);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
            Ring.BeginAnimation(Shape.StrokeThicknessProperty, thickness);

            Log.Information("[EmiDesk] the dock chip is knocking");
        }
        catch (Exception ex)
        {
            _knocking = false;
            Log.Warning(ex, "[EmiDesk] dock chip knock failed to start");
        }
    }

    private void OnKnockFinished(object? sender, EventArgs e) => StopKnock();

    /// <summary>
    /// Put the ring back exactly as it was, and never knock again. Idempotent, and safe from the
    /// click handler, the out-changed handler, the animation's own Completed and Unloaded.
    ///
    /// <para>Every animation is removed with a NULL timeline rather than left to hold its final
    /// value: a finished-but-attached animation keeps the property under animation control, and any
    /// later code that sets the stroke (a theme change, a rail rebuild) then silently does
    /// nothing.</para>
    /// </summary>
    private void StopKnock()
    {
        try
        {
            if (!_knocking) return;
            _knocking = false;

            if (Ring.Stroke is SolidColorBrush stroke)
            {
                stroke.BeginAnimation(SolidColorBrush.ColorProperty, null);
                stroke.Color = RestPink;
            }
            if (Ring.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
                glow.Opacity = 0;
                glow.BlurRadius = 0;
            }
            Ring.BeginAnimation(Shape.StrokeThicknessProperty, null);
            Ring.StrokeThickness = RestThickness;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dock chip knock stop failed");
        }
    }
}
