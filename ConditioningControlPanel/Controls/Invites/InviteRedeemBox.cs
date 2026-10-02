using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Controls.Invites;

/// <summary>
/// The one redeem box: code field, "Start my week", and the line under it. Hosted by the invites
/// section (<see cref="InvitePanel"/>) and the vault gate card. It confirms a date only when the
/// week actually opened on this machine; a code the server accepted whose end date could not be
/// applied yet (none sent, or a PC clock far off) says the week is on its way, and the heartbeat
/// delivers it.
/// </summary>
public sealed class InviteRedeemBox : StackPanel
{
    private static readonly Brush RowBg = Frozen("#252542");
    private static readonly Brush Edge = Frozen("#3D3D60");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Good = Frozen("#7FD8A6");
    private static readonly Brush Warn = Frozen("#FFB347");

    private readonly Func<IInviteApi> _api;
    private readonly string _source;
    private readonly Grid _row = new();
    private readonly TextBox _box;
    private readonly Button _go;
    private readonly TextBlock _result;
    private bool _busy;

    /// <summary>Raised on the UI thread after the server accepted a code.</summary>
    public event Action? Redeemed;

    public InviteRedeemBox(string source, Func<IInviteApi>? api = null)
    {
        _api = api ?? (() => new InviteApi());
        _source = source;

        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 320 });
        _row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _box = new TextBox
        {
            Background = RowBg, Foreground = Text, BorderBrush = Edge, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), FontSize = 14, MaxLength = 80, CharacterCasing = CharacterCasing.Upper,
        };
        _box.SetResourceReference(Control.FontFamilyProperty, "Font.Mono");
        _box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "PinkBrush");
        _row.Children.Add(_box);

        _go = new Button { Content = Loc.Get("invites_redeem_go"), Cursor = Cursors.Hand, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _go.SetResourceReference(StyleProperty, "SmallPinkButton");
        Grid.SetColumn(_go, 1);
        _row.Children.Add(_go);
        Children.Add(_row);

        _result = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        Children.Add(_result);

        _go.Click += (_, _) => Redeem();
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Redeem(); };
    }

    /// <summary>Put the caret in the code field.</summary>
    public void FocusCode() => Dispatcher.BeginInvoke(new Action(() => _box.Focus()));

    private async void Redeem()
    {
        if (_busy) return;
        if (InviteRules.NormalizeCode(_box.Text) == null)
        {
            Say(Loc.Get("invites_err_bad_code"), Warn);
            return;
        }
        _busy = true;
        _go.IsEnabled = false;
        try
        {
            var outcome = await _api().RedeemAsync(_box.Text);
            if (!outcome.Ok)
            {
                Say(Loc.Get(InvitePanel.ReasonKey(outcome.Reason)), Warn);
                return;
            }

            InviteGrantSync.ApplyNow(outcome.GrantUntilUtc, _source);
            _row.Visibility = Visibility.Collapsed;
            var settings = App.Settings?.Current;
            if (settings?.HasInviteGrant == true && settings.InviteGrantUntil is DateTime until)
                Say(Loc.GetF("invites_redeem_ok", until.ToLocalTime().ToString("d MMM, HH:mm")), Good);
            else
            {
                App.Logger?.Warning("[Invites] code accepted but no usable end date yet (sent: {Sent})", outcome.GrantUntilUtc.HasValue);
                Say(Loc.Get("invites_redeem_ok_pending"), Good);
            }
            Redeemed?.Invoke();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Invites] redeem failed: {E}", ex.GetType().Name);
            Say(Loc.Get("invites_err_offline"), Warn);
        }
        finally
        {
            _busy = false;
            _go.IsEnabled = true;
        }
    }

    private void Say(string text, Brush brush)
    {
        _result.Text = text;
        _result.Foreground = brush;
        _result.Visibility = Visibility.Visible;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
