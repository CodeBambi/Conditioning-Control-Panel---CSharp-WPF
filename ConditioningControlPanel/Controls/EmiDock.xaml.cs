using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
/// </summary>
public partial class EmiDock : UserControl
{
    private EmiDeskService? _svc;
    private bool _wired;

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
                _wired = false;
            }
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
}
