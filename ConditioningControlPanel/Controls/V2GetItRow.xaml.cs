using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The "Get it" row inside a v2 box. One prize, one price, one button.
///
/// <para>It draws whatever <see cref="V2PurchaseRule"/> decides and knows nothing else: no price, no
/// grant id, no server reason of its own. <see cref="RowChanged"/> tells the panel to re-measure its
/// box, because a box holding only this row must collapse the moment the row goes <see
/// cref="V2PurchaseRowState.Hidden"/>.</para>
///
/// <para>Buying is confirmed first (it spends the same Sparkle Points the skill tree spends) and the
/// button is dead while the request is out, so a double press cannot send two. The server's receipt
/// is the guarantee behind that; the disabled button is only the manners.</para>
/// </summary>
public partial class V2GetItRow : UserControl
{
    private string _prizeId = string.Empty;
    private string _blurbKey = string.Empty;
    private string _nameKey = string.Empty;
    private bool _wired;

    /// <summary>Raised on the UI thread after the row repaints, so the panel can re-measure its box.</summary>
    public event EventHandler? RowChanged;

    /// <summary>True while the account owns the prize: the box should ignore this row entirely.</summary>
    public bool IsRowHidden { get; private set; } = true;

    public V2GetItRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // The counter is read the first time this row is genuinely on screen and never before: the
        // effects rack is mounted permanently, so Loaded would mean "at startup" for every user,
        // including the ones who never open the Flashes page. IsVisible is the whole chain.
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) App.V2Purchase?.EnsureState(); };
    }

    /// <summary>
    /// Name the prize this row sells. <paramref name="nameKey"/> is the box's own heading key, so the
    /// confirm asks about the thing the user is looking at.
    /// </summary>
    public void Configure(string prizeId, string blurbKey, string nameKey)
    {
        _prizeId = prizeId;
        _blurbKey = blurbKey;
        _nameKey = nameKey;
        TxtBlurb.Text = Loc.Get(blurbKey);
        Apply();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_wired) return;
        _wired = true;
        if (App.V2Purchase is { } svc)
        {
            svc.Changed += OnServiceChanged;
            svc.Bought += OnBought;
        }
        PrizeGrants.GrantsChanged += OnServiceChanged;
        Apply();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_wired) return;
        _wired = false;
        if (App.V2Purchase is { } svc)
        {
            svc.Changed -= OnServiceChanged;
            svc.Bought -= OnBought;
        }
        PrizeGrants.GrantsChanged -= OnServiceChanged;
    }

    // The service finishes its work off the UI thread, so every repaint is marshalled.
    private void OnServiceChanged() => Dispatcher.BeginInvoke(new Action(Apply));

    private void OnBought(string prizeId) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (prizeId != _prizeId) return;
        App.Notifications?.Show(Loc.GetF("v2_get_toast_done", Loc.Get(_nameKey)), NotificationType.Success);
    }));

    /// <summary>Reads the rule and dresses the row. Safe to call at any time on the UI thread.</summary>
    public void Apply()
    {
        if (string.IsNullOrEmpty(_prizeId)) return;
        var row = App.V2Purchase?.RowFor(_prizeId)
                  ?? new V2PurchaseRow(V2PurchaseRowState.Hidden, 0, 0, null);

        IsRowHidden = row.State == V2PurchaseRowState.Hidden;
        Visibility = IsRowHidden ? Visibility.Collapsed : Visibility.Visible;

        PricePlate.Visibility = row.ShowsPrice ? Visibility.Visible : Visibility.Collapsed;
        TxtPrice.Text = row.PriceSp.ToString(System.Globalization.CultureInfo.CurrentCulture);

        BtnGet.Visibility = row.State == V2PurchaseRowState.Hidden || row.State == V2PurchaseRowState.Loading
            || row.State == V2PurchaseRowState.Unavailable
            ? Visibility.Collapsed
            : Visibility.Visible;
        BtnGet.IsEnabled = row.State == V2PurchaseRowState.SignIn || row.BuyEnabled;
        BtnGet.Opacity = BtnGet.IsEnabled ? 1.0 : 0.5;
        BtnGet.Content = Loc.Get(row.State == V2PurchaseRowState.SignIn ? "v2_get_signin_button" : "v2_get_button");

        TxtStatus.Text = row.State switch
        {
            V2PurchaseRowState.SignIn => Loc.Get("v2_get_signin"),
            V2PurchaseRowState.Loading => Loc.Get("v2_get_checking"),
            V2PurchaseRowState.Unavailable => Loc.Get("v2_get_unavailable"),
            V2PurchaseRowState.Busy => Loc.Get("v2_get_working"),
            V2PurchaseRowState.CannotAfford => Loc.GetF("v2_get_short", row.ShortBy),
            V2PurchaseRowState.Failed => Loc.Get(row.MessageKey ?? "v2_get_error_generic"),
            _ => string.Empty,
        };

        RowChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks before spending, then buys. The button is already dead for every other state.</summary>
    private async void BtnGet_Click(object sender, RoutedEventArgs e)
    {
        var svc = App.V2Purchase;
        if (svc == null || string.IsNullOrEmpty(_prizeId)) return;
        var row = svc.RowFor(_prizeId);

        if (row.State == V2PurchaseRowState.SignIn)
        {
            (Application.Current?.MainWindow as MainWindow)?.OpenUnifiedLoginDialog(Window.GetWindow(this));
            return;
        }
        if (!row.BuyEnabled) return;

        var owner = Window.GetWindow(this);
        var answer = owner == null
            ? MessageBox.Show(Loc.GetF("v2_get_confirm_body", row.PriceSp, Loc.Get(_nameKey)),
                Loc.Get("v2_get_confirm_title"), MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, Loc.GetF("v2_get_confirm_body", row.PriceSp, Loc.Get(_nameKey)),
                Loc.Get("v2_get_confirm_title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await svc.BuyAsync(_prizeId);
    }
}
