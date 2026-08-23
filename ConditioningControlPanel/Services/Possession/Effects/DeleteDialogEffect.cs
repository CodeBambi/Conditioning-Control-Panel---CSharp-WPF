using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R4 "deletedialog" (Full Doki only) - the themed fake deletion. It walks a crimson progress bar
/// through the user's REAL session names, one at a time, and deletes absolutely nothing. Same
/// owner-locked rules as the Doki crash dialog: crimson chrome, the companion's face, an in-character
/// caption, ember accents, and NEVER a real path, a log name or desktop chrome - support has to tell it
/// from a real deletion across the room, and so does the user.
///
/// <para><b>Why real names.</b> A fake list of invented sessions is a screensaver; the user's own
/// "Morning Drift" going grey is the whole effect. So the names are read (names ONLY, never a path,
/// never a file) straight off the session store, read-only, and nothing is ever opened for write.</para>
///
/// <para><b>The cancel button really cancels.</b> It stops the walk and closes at any point, because a
/// dialog about losing your sessions that will not close is no longer theatre. The failsafe timer
/// closes it even if the director never comes back, exactly like the Doki dialog.</para>
/// </summary>
public sealed class DeleteDialogEffect : PossessionEffectBase
{
    private const double BarWidth = 300;
    private const int MaxNames = 6;

    private static readonly Color Crimson = Color.FromRgb(0xDC, 0x14, 0x3C);
    private static readonly Color DeepRed = Color.FromRgb(0x1A, 0x0A, 0x0A);

    private static readonly string[] _captions =
    {
        "tidying up after you",
        "removing what you do not need",
        "housekeeping, sweetheart",
    };

    private Window? _dialog;
    private DispatcherTimer? _failsafe;
    private bool _cancelled;
    private List<string> _names = new();

    public override string Id => "deletedialog";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(8);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        // Targetless: the warden names what the dialog claims to be eating.
        NameOverrideText = "your sessions";
        if (_dialog != null || ctx.Host.Window == null) return false;
        _names = ReadSessionNames();
        return _names.Count > 0;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        TextBlock? status = null;
        Rectangle? bar = null;
        StackPanel? list = null;
        var rows = new List<TextBlock>();

        try
        {
            if (_names.Count == 0) _names = ReadSessionNames();
            if (_names.Count == 0) return;

            _cancelled = false;
            _dialog = BuildDialog(ctx, out status, out bar, out list);
            if (_dialog == null) return;

            if (list != null)
            {
                foreach (var name in _names)
                {
                    var row = new TextBlock
                    {
                        Text = "  " + name,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x9A, 0x92)),
                        FontSize = 12,
                        Margin = new Thickness(0, 1, 0, 1),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    };
                    list.Children.Add(row);
                    rows.Add(row);
                }
            }

            _dialog.Show();

            _failsafe = new DispatcherTimer { Interval = HoldFor + TimeSpan.FromSeconds(3) };
            _failsafe.Tick += (_, __) => { try { CloseDialog(); } catch { } };
            _failsafe.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Possession delete dialog failed to open: {Error}", ex.Message);
            return;
        }

        // The walk. One name at a time, the bar creeping to match, and nothing touched.
        try
        {
            double perName = Math.Max(280, (HoldFor.TotalMilliseconds - 900) / Math.Max(1, _names.Count));

            for (int i = 0; i < _names.Count; i++)
            {
                if (_cancelled || ct.IsCancellationRequested) return;

                if (status != null) status.Text = Fmt("possession_delete_removing", _names[i], "removing {0}...");
                if (bar != null)
                    PossAnim.To(bar, FrameworkElement.WidthProperty,
                                BarWidth * (i + 1) / _names.Count, perName, PossAnim.EaseInOut);

                if (!await PossAnim.DelayAsync(perName, ct).ConfigureAwait(true)) return;

                if (i < rows.Count)
                {
                    // Struck through, greyed: the row LOOKS gone, and the file it names never moved.
                    rows[i].TextDecorations = TextDecorations.Strikethrough;
                    rows[i].Opacity = 0.35;
                }
            }

            if (_cancelled) return;
            if (status != null) status.Text = "nothing was deleted. i just wanted to watch you read it.";
        }
        catch (Exception ex) { App.Logger?.Warning("Possession delete dialog walk failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var dlg = _dialog;
        double fade = UndoMs(duration, 120, 320);
        if (dlg != null && fade > 0)
        {
            try
            {
                PossAnim.To(dlg, UIElement.OpacityProperty, 0, fade, PossAnim.EaseInOut);
                await PossAnim.DelayAsync(fade + 20, CancellationToken.None).ConfigureAwait(true);
            }
            catch { }
        }
        CloseDialog();
        _names = new List<string>();
        _cancelled = false;
    }

    // ---- the dialog ----------------------------------------------------------------------------

    private Window? BuildDialog(PossessionContext ctx, out TextBlock? status, out Rectangle? bar,
                                out StackPanel? list)
    {
        status = null;
        bar = null;
        list = null;

        var crimson = new SolidColorBrush(Crimson); crimson.Freeze();
        var deep = new SolidColorBrush(DeepRed); deep.Freeze();
        var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xDA)); ink.Freeze();
        var dim = new SolidColorBrush(Color.FromRgb(0xC0, 0x9A, 0x92)); dim.Freeze();
        var track = new SolidColorBrush(Color.FromRgb(0x2A, 0x14, 0x14)); track.Freeze();

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = false,
            ShowInTaskbar = false,
            Topmost = false,
            ShowActivated = false,          // never steal focus from the secret phrase box
            Width = 470,
            Height = 300,
            Background = deep,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        try
        {
            win.Owner = ctx.Host.Window;
            win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        catch { /* owner not shown yet: centre on the screen instead */ }

        var shell = new Border
        {
            BorderBrush = crimson,
            BorderThickness = new Thickness(2),
            Background = deep,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var caption = new Border { Background = crimson, Padding = new Thickness(10, 5, 10, 5) };
        caption.Child = new TextBlock
        {
            Text = _captions[Rng.Next(_captions.Length)],
            Foreground = Brushes.White,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
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
            Text = Loc.Get("possession_delete_heading") is { Length: > 0 } h && h != "possession_delete_heading"
                ? h
                : "deleting your sessions.",
            Foreground = ink,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        list = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };
        column.Children.Add(list);

        status = new TextBlock
        {
            Text = Fmt("possession_delete_removing", _names.Count > 0 ? _names[0] : "", "removing {0}..."),
            Foreground = dim,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        column.Children.Add(status);

        var trackBorder = new Border
        {
            Height = 10,
            Width = BarWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = track,
            BorderBrush = crimson,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            ClipToBounds = true,
        };
        bar = new Rectangle { Width = 0, Fill = EmberBrush, HorizontalAlignment = HorizontalAlignment.Left };
        trackBorder.Child = bar;
        column.Children.Add(trackBorder);

        var cancel = new Button
        {
            Content = Loc.Get("possession_delete_cancel") is { Length: > 0 } c && c != "possession_delete_cancel"
                ? c
                : "cancel",
            Width = 90,
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = track,
            Foreground = ink,
            BorderBrush = crimson,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        cancel.Click += (_, __) =>
        {
            _cancelled = true;
            GrinOnTheWayOut();
            try { CloseDialog(); } catch { }
        };
        column.Children.Add(cancel);

        body.Children.Add(column);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        shell.Child = root;
        win.Content = shell;

        // Closing it any other way is the same act, and gets the same line.
        win.Closing += (_, __) => GrinOnTheWayOut();
        return win;
    }

    private static FrameworkElement? BuildFace()
    {
        try
        {
            ImageSource? source = null;
            try { source = App.AvatarWindow?.ImgAvatar?.Source; } catch { }
            if (source == null) { try { source = Helpers.EmojiImage.Get("\U0001F5D1"); } catch { } }

            if (source != null)
            {
                return new Image
                {
                    Source = source,
                    Width = 82,
                    Height = 120,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false,
                };
            }
        }
        catch { }
        return null;
    }

    private bool _grinned;

    /// <summary>Closing it does not end the joke, it lands it: a second, quieter bark under the same
    /// PossessionEffect trigger, keyed so the packs can answer the close specifically.</summary>
    private void GrinOnTheWayOut()
    {
        if (_grinned) return;
        _grinned = true;
        try { Ctx?.Name("deletedialog_closed", "your sessions"); }
        catch (Exception ex) { App.Logger?.Debug("Possession delete dialog grin failed: {Error}", ex.Message); }
    }

    private void CloseDialog()
    {
        try { _failsafe?.Stop(); } catch { }
        _failsafe = null;

        var dlg = _dialog;
        _dialog = null;
        _grinned = false;
        if (dlg == null) return;
        try { dlg.Close(); }
        catch (Exception ex) { App.Logger?.Warning("Possession delete dialog close failed: {Error}", ex.Message); }
    }

    // ---- the names -----------------------------------------------------------------------------

    /// <summary>
    /// The user's own session names, read-only. Custom sessions first (those are the ones with
    /// something to lose), then the built-ins to fill the list. NAMES ONLY: no path is read out, none
    /// is shown, and nothing here ever opens a file for write or creates a folder.
    /// </summary>
    private List<string> ReadSessionNames()
    {
        var names = new List<string>();
        try
        {
            var files = new SessionFileService();

            var folder = SessionFileService.CustomSessionsFolder;
            if (Directory.Exists(folder))
            {
                foreach (var path in Directory.GetFiles(folder, "*.session.json"))
                {
                    if (names.Count >= MaxNames) break;
                    string? name = null;
                    try { name = files.ImportSession(path)?.Name; } catch { }
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!names.Contains(name!, StringComparer.OrdinalIgnoreCase)) names.Add(name!);
                }
            }

            if (names.Count < MaxNames)
            {
                foreach (var s in Models.Session.GetAllSessions())
                {
                    if (names.Count >= MaxNames) break;
                    var n = s?.Name;
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (!names.Contains(n!, StringComparer.OrdinalIgnoreCase)) names.Add(n!);
                }
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession delete dialog name read failed: {Error}", ex.Message); }

        return names;
    }

    private static string Fmt(string key, string arg, string fallback)
    {
        try
        {
            var s = Loc.Get(key);
            if (!string.IsNullOrWhiteSpace(s) && s != key && s.Contains("{0}", StringComparison.Ordinal))
                return string.Format(s, arg);
        }
        catch { }
        return string.Format(fallback, arg);
    }
}
