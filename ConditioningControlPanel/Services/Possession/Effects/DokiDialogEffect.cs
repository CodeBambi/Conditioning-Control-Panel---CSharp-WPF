using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R4 "dokidialog" (Full Doki only) - the themed fake crash. Owner-locked rules: a fake crash / fake
/// deletion is allowed, but it must be OBVIOUSLY ours at a glance - crimson chrome, the companion's
/// face, an in-character title, ember accents. It NEVER borrows a real file path, a log name, desktop
/// chrome, or the name of the operating system: support has to be able to tell it from a real crash
/// from across the room, and so does the user.
///
/// The progress bar fills all the way to 100% and then admits there was nothing to delete.
/// </summary>
public sealed class DokiDialogEffect : PossessionEffectBase
{
    private static readonly string[] _titles =
    {
        "bambi.exe has stopped pretending",
        "the panel has stopped pretending",
        "lockdown is not responding to you",
    };

    private const double BarMs = 2200;
    private const double BarWidth = 260;

    private static readonly Color Crimson = Color.FromRgb(0xDC, 0x14, 0x3C);
    private static readonly Color DeepRed = Color.FromRgb(0x1A, 0x0A, 0x0A);

    private Window? _dialog;
    private DispatcherTimer? _failsafe;

    public override string Id => "dokidialog";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        // Targetless, so the warden has nothing to name - give it the dialog itself ("that window").
        // Set here because the base names the effect BEFORE ApplyCore runs.
        NameOverrideText = "that window";
        return _dialog == null && ctx.Host.Window != null;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        TextBlock? status = null;
        Rectangle? bar = null;

        try
        {
            _dialog = BuildDialog(ctx, out status, out bar);
            if (_dialog == null) return;
            _dialog.Show();

            // The failsafe closes it even if the director never gets round to undoing us.
            _failsafe = new DispatcherTimer { Interval = HoldFor + TimeSpan.FromSeconds(2) };
            _failsafe.Tick += (_, __) => { try { CloseDialog(); } catch { } };
            _failsafe.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession doki dialog failed to open: {Error}", ex.Message);
            return;
        }

        try
        {
            if (bar != null) PossAnim.To(bar, FrameworkElement.WidthProperty, BarWidth, BarMs, PossAnim.EaseInOut, 0);

            const int steps = 44;
            var ease = PossAnim.EaseInOut;
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                int pct = (int)Math.Round(ease.Ease(t) * 100);
                if (status != null) status.Text = "deleting your way out... " + pct + "%";
                if (!await PossAnim.DelayAsync(BarMs / steps, ct).ConfigureAwait(true)) return;
            }
            if (status != null) status.Text = "nothing happened. there was nothing to delete.";
        }
        catch (Exception ex) { App.Logger?.Warning("Possession doki dialog progress failed: {Error}", ex.Message); }
    }

    private Window? BuildDialog(PossessionContext ctx, out TextBlock? status, out Rectangle? bar)
    {
        status = null;
        bar = null;

        var crimsonBrush = new SolidColorBrush(Crimson); crimsonBrush.Freeze();
        var deepBrush = new SolidColorBrush(DeepRed); deepBrush.Freeze();
        var inkBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xDA)); inkBrush.Freeze();
        var dimBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x9A, 0x92)); dimBrush.Freeze();
        var trackBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x14, 0x14)); trackBrush.Freeze();

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            Topmost = false,
            ShowActivated = false,          // never steal focus from the secret phrase box
            Width = 440,
            Height = 214,
            Background = deepBrush,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        try
        {
            win.Owner = ctx.Host.Window;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        catch { /* owner not shown yet: centre on the screen instead */ }

        var shell = new Border
        {
            BorderBrush = crimsonBrush,
            BorderThickness = new Thickness(2),
            Background = deepBrush
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Crimson caption strip: obviously OURS, and obviously not desktop chrome.
        var caption = new Border { Background = crimsonBrush, Padding = new Thickness(10, 5, 10, 5) };
        caption.Child = new TextBlock
        {
            Text = _titles[Rng.Next(_titles.Length)],
            Foreground = Brushes.White,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(caption, 0);
        root.Children.Add(caption);

        var body = new Grid { Margin = new Thickness(14, 12, 14, 12) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var face = BuildFace();
        if (face != null)
        {
            Grid.SetColumn(face, 0);
            body.Children.Add(face);
        }

        var column = new StackPanel { Margin = new Thickness(face != null ? 12 : 0, 0, 0, 0) };
        Grid.SetColumn(column, 1);

        column.Children.Add(new TextBlock
        {
            Text = "you tried to leave.",
            Foreground = inkBrush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        status = new TextBlock
        {
            Text = "deleting your way out... 0%",
            Foreground = dimBrush,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        column.Children.Add(status);

        var track = new Border
        {
            Height = 10,
            Width = BarWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = trackBrush,
            BorderBrush = crimsonBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true
        };
        bar = new Rectangle
        {
            Width = 0,
            Fill = EmberBrush,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        track.Child = bar;
        column.Children.Add(track);

        var ok = new Button
        {
            Content = "ok",
            Width = 74,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = trackBrush,
            Foreground = inkBrush,
            BorderBrush = crimsonBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        ok.Click += (_, __) => { try { CloseDialog(); } catch { } };
        column.Children.Add(ok);

        body.Children.Add(column);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        shell.Child = root;
        win.Content = shell;
        return win;
    }

    /// <summary>The companion is the one telling you this. Her live portrait when we can reach it,
    /// otherwise a plain glyph: never a desktop error icon.</summary>
    private static FrameworkElement? BuildFace()
    {
        try
        {
            ImageSource? source = null;
            try { source = App.AvatarWindow?.ImgAvatar?.Source; } catch { }
            if (source == null) { try { source = Helpers.EmojiImage.Get("\U0001F494"); } catch { } }

            if (source != null)
            {
                return new Image
                {
                    Source = source,
                    Width = 76,
                    Height = 110,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false
                };
            }
        }
        catch { }
        return null;
    }

    private void CloseDialog()
    {
        try { _failsafe?.Stop(); } catch { }
        _failsafe = null;

        var dlg = _dialog;
        _dialog = null;
        if (dlg == null) return;
        try { dlg.Close(); }
        catch (Exception ex) { App.Logger?.Warning("Possession doki dialog close failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var dlg = _dialog;
        double fade = UndoMs(duration, 120, 320);
        if (dlg != null && fade > 0)
        {
            double ms = fade;
            try
            {
                PossAnim.To(dlg, UIElement.OpacityProperty, 0, ms, PossAnim.EaseInOut);
                await PossAnim.DelayAsync(ms + 20, CancellationToken.None).ConfigureAwait(true);
            }
            catch { }
        }
        CloseDialog();
    }
}
