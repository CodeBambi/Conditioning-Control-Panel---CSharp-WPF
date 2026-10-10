using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// The "Get it" row inside a v2 box, ported from WPF Controls/V2GetItRow.xaml.cs. One prize, one
    /// price, one button.
    ///
    /// <para>It draws whatever <see cref="V2PurchaseRule"/> decides and knows nothing else: no price, no
    /// grant id, no server reason of its own. <see cref="RowChanged"/> tells the panel to re-measure its
    /// box, because a box holding only this row must collapse the moment the row goes
    /// <see cref="V2PurchaseRowState.Hidden"/>.</para>
    ///
    /// <para>Buying is confirmed first (it spends the same Sparkle Points the skill tree spends) and the
    /// button is dead while the request is out, so a double press cannot send two. The server's receipt
    /// is the guarantee behind that; the disabled button is only the manners. The wallet is never
    /// touched here: the service hands the reply's balance to the app, which applies
    /// <c>V2WalletAdoption</c>.</para>
    /// </summary>
    public partial class V2GetItRow : UserControl
    {
        private string _prizeId = string.Empty;
        private string _nameKey = string.Empty;
        private bool _wired;
        private V2PurchaseService? _hooked;

        /// <summary>Raised on the UI thread after the row repaints, so the panel can re-measure its box.</summary>
        public event EventHandler? RowChanged;

        /// <summary>True while the account owns the prize: the box should ignore this row entirely.</summary>
        public bool IsRowHidden { get; private set; } = true;

        /// <summary>Test seams: the service (default <see cref="App.V2Purchase"/>), the Yes/No question,
        /// the way in, the toast.</summary>
        internal V2PurchaseService? ServiceOverride;
        internal Func<Window?, string, string, Task<bool>> Ask = (owner, title, message) =>
            owner == null ? Task.FromResult(false) : MessageDialog.ConfirmAsync(owner, title, message);
        internal Action<Window?> SignIn = owner =>
            _ = (owner as MainShellWindow ?? MainShellWindow.Current)?.OpenUnifiedLoginDialog(owner);
        internal Action<string> Toast = message => App.Notifications.Show(message, NotificationType.Success);

        private V2PurchaseService? Service => ServiceOverride ?? App.V2Purchase;

        public V2GetItRow()
        {
            InitializeComponent();
            // The counter is read the first time this row is genuinely on screen and never before: the
            // effects rack is mounted permanently, so "attached" would mean "at startup" for every user,
            // including the ones who never open the Flashes page. IsEffectivelyVisible is the whole chain.
            // Avalonia has no IsVisibleChanged for the chain: the effective viewport moves when the row
            // is first laid out on a shown page, and EnsureState is a no-op once the counter is known.
            EffectiveViewportChanged += (_, _) => { if (IsEffectivelyVisible) Service?.EnsureState(); };
        }

        /// <summary>
        /// Name the prize this row sells. <paramref name="nameKey"/> is the box's own heading key, so the
        /// confirm asks about the thing the user is looking at.
        /// </summary>
        public void Configure(string prizeId, string blurbKey, string nameKey)
        {
            _prizeId = prizeId;
            _nameKey = nameKey;
            TxtBlurb.Text = Loc.Get(blurbKey);
            Apply();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (_wired) return;
            _wired = true;
            _hooked = Service;
            if (_hooked is { } svc)
            {
                svc.Changed += OnServiceChanged;
                svc.Bought += OnBought;
            }
            PrizeOwnership.Changed += OnServiceChanged;
            Apply();
            if (IsEffectivelyVisible) Service?.EnsureState();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_wired)
            {
                _wired = false;
                if (_hooked is { } svc)
                {
                    svc.Changed -= OnServiceChanged;
                    svc.Bought -= OnBought;
                }
                _hooked = null;
                PrizeOwnership.Changed -= OnServiceChanged;
            }
            base.OnDetachedFromVisualTree(e);
        }

        // The service finishes its work off the UI thread, so every repaint is marshalled.
        private void OnServiceChanged() => Dispatcher.UIThread.Post(Apply);

        private void OnBought(string prizeId) => Dispatcher.UIThread.Post(() =>
        {
            if (prizeId != _prizeId) return;
            Toast(Loc.GetF("v2_get_toast_done", Loc.Get(_nameKey)));
        });

        /// <summary>Reads the rule and dresses the row. Safe to call at any time on the UI thread.</summary>
        public void Apply()
        {
            if (string.IsNullOrEmpty(_prizeId)) return;
            var row = Service?.RowFor(_prizeId)
                      ?? new V2PurchaseRow(V2PurchaseRowState.Hidden, 0, 0, null);

            IsRowHidden = row.State == V2PurchaseRowState.Hidden;
            IsVisible = !IsRowHidden;

            PricePlate.IsVisible = row.ShowsPrice;
            TxtPrice.Text = row.PriceSp.ToString(System.Globalization.CultureInfo.CurrentCulture);

            BtnGet.IsVisible = row.ShowsBuyButton;
            BtnGet.IsEnabled = row.State == V2PurchaseRowState.SignIn || row.BuyEnabled;
            BtnGet.Opacity = BtnGet.IsEnabled ? 1.0 : 0.5;
            TxtGet.Text = Loc.Get(row.State == V2PurchaseRowState.SignIn ? "v2_get_signin_button" : "v2_get_button");

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

        private async void BtnGet_Click(object? sender, RoutedEventArgs e)
        {
            try { await PressAsync(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[Prizes] Get it press failed"); }
        }

        /// <summary>Asks before spending, then buys. The row's state is read again at the press: the
        /// button is already dead for every other state, and this is the access re-check.</summary>
        internal async Task<bool> PressAsync()
        {
            var svc = Service;
            if (svc == null || string.IsNullOrEmpty(_prizeId)) return false;
            var row = svc.RowFor(_prizeId);
            var owner = TopLevel.GetTopLevel(this) as Window;

            if (row.State == V2PurchaseRowState.SignIn)
            {
                SignIn(owner);
                return false;
            }
            // No price on the row means no press: a confirm reading "Spend 0 Sparkle Points" must never
            // be shown, and the buy that followed it would charge whatever the counter says.
            if (!row.BuyEnabled || !row.ShowsPrice) return false;

            var body = Loc.GetF("v2_get_confirm_body", row.PriceSp, Loc.Get(_nameKey));
            if (!await Ask(owner, Loc.Get("v2_get_confirm_title"), body)) return false;

            // The price that was confirmed travels with the request: if the counter has repriced in
            // between, the service refuses rather than charging a number the player never saw.
            return await svc.BuyAsync(_prizeId, row.PriceSp);
        }
    }
}
