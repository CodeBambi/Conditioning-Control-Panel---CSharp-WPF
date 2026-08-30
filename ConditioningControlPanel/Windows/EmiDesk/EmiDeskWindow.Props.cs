using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: the FLAT ConditioningControlPanel namespace, same as every other file under
// Windows\. See the header of EmiDeskWindow.xaml.cs before "tidying" this.
namespace ConditioningControlPanel;

/// <summary>
/// THE THING SHE IS HOLDING. One plate at her right hand, up for a couple of seconds and gone: the
/// phone, the clipboard, the punch card. The art, the anchor and the sizes all live in
/// <see cref="EmiProps"/>; this file is only the WPF half of showing one.
///
/// <para><b>It is the lowest-priority thing she owns</b>, like everything else in wave A. A prop
/// beat starts only from <c>StepFidgets</c> when nothing else has the face, and ANY interruption -
/// a pat, a chain, an ask, a drag, a dismissal - takes the plate straight off, because a widget
/// that keeps holding a phone while she is being spoken to reads as a stuck sprite. That teardown
/// is <see cref="HideProp"/> and it is safe to call at any time, including twice.</para>
///
/// <para>The plate is <b>not</b> a control: <c>IsHitTestVisible="False"</c> in the XAML, no pointer
/// vocabulary of its own, and it never eats a click meant for her body.</para>
/// </summary>
public partial class EmiDeskWindow
{
    // ---------------------------------------------------------------- state

    /// <summary>Decoded plates, one per prop key, for the sitting. Three small PNGs.</summary>
    private readonly Dictionary<string, ImageSource> _propCache = new(StringComparer.Ordinal);

    /// <summary>Which prop is up, or null. The single witness the rest of the window reads.</summary>
    private string? _propKey;

    /// <summary>Takes the plate away when the hold is over. One timer, reused, never two.</summary>
    private DispatcherTimer? _propTimer;

    /// <summary>Is she holding something right now?</summary>
    public bool PropUp => _propKey != null;

    // ---------------------------------------------------------------- geometry

    /// <summary>
    /// Size and place the plate for the CURRENT body width. Called from <c>ApplyBodyWidth</c>, so a
    /// resize mid-beat moves the prop with her instead of leaving it stranded, and called again on
    /// the way up so a plate shown between resizes is never laid out at a stale width.
    ///
    /// <para>Everything is derived from <see cref="EmiProps"/>'s fractions of the body box - the
    /// same numbers the web's <c>.emi-prop</c> uses - so the two halves cannot drift apart by
    /// someone tuning one of them in DIPs.</para>
    /// </summary>
    private void LayoutProp()
    {
        try
        {
            var prop = EmiProps.Get(_propKey);
            if (prop == null) return;

            double bw = _bodyWidth;
            double bh = bw * BodyAspect;

            double w, h;
            if (prop.Sizing == EmiProps.Fit.Height)
            {
                h = bh * prop.Frac;
                w = h * PropPlateWidthOverHeight(prop.Key);
            }
            else
            {
                w = bw * prop.Frac;
                h = w / PropPlateWidthOverHeight(prop.Key);
            }

            PropImage.Width = Math.Max(1, w);
            PropImage.Height = Math.Max(1, h);

            // HorizontalAlignment=Right / VerticalAlignment=Bottom in the XAML, so the margin is the
            // gap between the plate and the body box's own right and bottom edges.
            PropImage.Margin = new Thickness(0, 0,
                bw * (1 - EmiProps.RightFrac),
                bh * (1 - EmiProps.BottomFrac));

            PropTilt.Angle = prop.TiltDeg;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] prop layout failed for {Key}", _propKey);
        }
    }

    /// <summary>The drawn plate's width / height, from its decoded bitmap when there is one and
    /// from the shipped size otherwise. Never zero, never a divide by nothing.</summary>
    private double PropPlateWidthOverHeight(string key)
    {
        if (_propCache.TryGetValue(key, out var src) && src.Height > 0)
            return src.Width / src.Height;
        return key switch
        {
            "phone" => 98.0 / 170.0,
            "clipboard" => 170.0 / 226.0,
            "punchcard" => 214.0 / 118.0,
            _ => 1.0
        };
    }

    // ---------------------------------------------------------------- show / hide

    /// <summary>
    /// Bring a plate up at her hand and take it away again after <see cref="EmiProps.HoldMs"/>.
    /// Junk keys, missing art and a window that is already gone are all the same silent no.
    /// </summary>
    /// <param name="key">A key from <see cref="EmiProps.All"/>.</param>
    /// <param name="holdMs">How long to hold it. Non-positive takes the authored hold.</param>
    public void ShowProp(string? key, int holdMs = 0)
    {
        try
        {
            var prop = EmiProps.Get(key);
            if (prop == null) return;

            if (!_propCache.TryGetValue(prop.Key, out var img))
            {
                var path = EmiProps.Path(prop.Key);
                if (path == null)
                {
                    // Art is allowed to arrive after the code does. No plate, no beat, no error.
                    Log.Debug("[EmiDesk] prop art missing for {Key}", prop.Key);
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
                _propCache[prop.Key] = img;
            }

            _propKey = prop.Key;
            PropImage.Source = img;
            LayoutProp();                      // AFTER the cache fill: the aspect comes from the bitmap
            PropImage.Visibility = Visibility.Visible;

            RunPropRise(up: true);

            int hold = holdMs > 0 ? holdMs : EmiProps.HoldMs;
            _propTimer ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
            _propTimer.Stop();
            _propTimer.Interval = TimeSpan.FromMilliseconds(hold);
            _propTimer.Tick -= OnPropHoldOver;
            _propTimer.Tick += OnPropHoldOver;
            _propTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ShowProp failed for {Key}", key);
        }
    }

    private void OnPropHoldOver(object? sender, EventArgs e)
    {
        try
        {
            _propTimer?.Stop();
            HideProp();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] prop hold expiry failed");
        }
    }

    /// <summary>
    /// Take the plate away. Idempotent, never throws, and safe to call from any interruption - it
    /// is what every "something else took the face" path calls.
    /// </summary>
    /// <param name="animate">False drops it instantly (a dismissal, a teardown): there is nobody
    /// left to watch it slide.</param>
    public void HideProp(bool animate = true)
    {
        try
        {
            _propTimer?.Stop();
            if (_propKey == null)
            {
                // Still make sure the node is down: a failed rise must not leave a plate hanging.
                if (PropImage.Visibility != Visibility.Collapsed)
                {
                    PropImage.Visibility = Visibility.Collapsed;
                    PropImage.Source = null;
                }
                return;
            }
            _propKey = null;

            if (!animate || !AliveMotionOk)
            {
                PropRise.BeginAnimation(TranslateTransform.YProperty, null);
                PropRise.Y = 0;
                PropImage.Visibility = Visibility.Collapsed;
                PropImage.Source = null;
                return;
            }

            RunPropRise(up: false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] HideProp failed");
            try
            {
                PropImage.Visibility = Visibility.Collapsed;
                PropImage.Source = null;
            }
            catch (Exception inner) { Log.Debug(inner, "[EmiDesk] prop stand-down failed"); }
        }
    }

    /// <summary>
    /// The slide. She lifts it out from behind herself rather than having it appear, which is the
    /// difference between a prop and a popup. On the way down the node collapses when the animation
    /// lands, so a plate is never left sitting invisible-but-live under the chrome.
    /// </summary>
    private void RunPropRise(bool up)
    {
        try
        {
            double travel = PropImage.Height * EmiProps.RiseFrac;
            if (double.IsNaN(travel) || travel <= 0) travel = 20;

            if (!AliveMotionOk)
            {
                // Reduced motion keeps the prop and drops the slide, exactly like the desk toy's bob.
                PropRise.BeginAnimation(TranslateTransform.YProperty, null);
                PropRise.Y = 0;
                if (!up)
                {
                    PropImage.Visibility = Visibility.Collapsed;
                    PropImage.Source = null;
                }
                return;
            }

            var anim = new DoubleAnimation
            {
                From = up ? travel : 0,
                To = up ? 0 : travel,
                Duration = TimeSpan.FromMilliseconds(EmiProps.RiseMs),
                EasingFunction = new SineEase
                {
                    EasingMode = up ? EasingMode.EaseOut : EasingMode.EaseIn
                },
                FillBehavior = FillBehavior.HoldEnd
            };

            if (!up)
            {
                anim.Completed += (_, _) =>
                {
                    try
                    {
                        if (_propKey != null) return;   // a new beat started under the old fade
                        PropRise.BeginAnimation(TranslateTransform.YProperty, null);
                        PropRise.Y = 0;
                        PropImage.Visibility = Visibility.Collapsed;
                        PropImage.Source = null;
                    }
                    catch { /* she is gone */ }
                };
            }

            PropRise.BeginAnimation(TranslateTransform.YProperty, anim);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] prop rise failed");
        }
    }

    // ---------------------------------------------------------------- the beat

    /// <summary>
    /// THE IDLE BEAT: she checks something. A wordless chain holds the reading face for the length
    /// of the plate's hold and then lets go with the small pleased look, and the plate rides
    /// alongside on its own timer so a chain that is cut short does not leave it up.
    ///
    /// <para>Which prop is a plain roll, minus the one she did last, for the reason the fidget
    /// scheduler never repeats itself: the same object twice running reads as a loop.</para>
    /// </summary>
    private void RunPropBeat()
    {
        try
        {
            if (EmiProps.All.Count == 0) return;

            string key = _lastPropKey;
            for (int guard = 0; guard < 8 && key == _lastPropKey; guard++)
                key = EmiProps.All[Rng.Next(EmiProps.All.Count)].Key;
            _lastPropKey = key;

            ShowProp(key);
            if (!PropUp) return;               // art missing: do not put the reading face on nothing

            PlayChain(new EmiChain(
                "prop", "IDLE BEAT (checks something)",
                new[]
                {
                    new EmiFrame(EmiProps.Face, EmiProps.HoldMs),
                    new EmiFrame(EmiProps.DoneFace, 420)
                },
                BodyFrame: "idle"), done: () => HideProp());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] prop beat failed");
        }
    }

    /// <summary>The prop the last beat used, so the next one picks a different one.</summary>
    private string _lastPropKey = string.Empty;
}
