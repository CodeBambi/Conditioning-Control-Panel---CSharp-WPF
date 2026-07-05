using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Dialogs;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Generic modeless popup window that hosts a feature control.
/// Borderless, pink-themed titlebar, drag-to-move, Escape-to-close, centered on owner.
/// </summary>
public partial class FeaturePopupWindow : Window
{
    public FeaturePopupWindow()
    {
        InitializeComponent();
    }

    ~FeaturePopupWindow()
    {
        try
        {
            Opened -= OnOpened;
            KeyDown -= OnKeyDown;
        }
        catch { }
    }

    /// <summary>
    /// Creates a themed feature popup centered on its owner window.
    /// </summary>
    /// <param name="content">The feature control to host inside the popup.</param>
    /// <param name="title">Title shown in the title bar and window chrome.</param>
    /// <param name="icon">Optional icon image displayed next to the title.</param>
    /// <param name="glyph">Optional emoji/text glyph used when no icon is supplied.</param>
    /// <param name="owner">Optional owner window used to center the popup via <see cref="WindowStartupLocation" />.</param>
    public FeaturePopupWindow(Control content, string title, IImage? icon = null, string? glyph = null, Window? owner = null)
        : this()
    {
        if (owner is not null)
        {
            Owner = owner;
        }

        TxtTitle.Text = title;
        Title = title; // also set Window.Title for accessibility

        if (icon is not null)
        {
            ImgIcon.Source = icon;
            ImgIcon.IsVisible = true;
            TxtGlyph.IsVisible = false;
        }
        else if (!string.IsNullOrEmpty(glyph))
        {
            TxtGlyph.Text = glyph;
            TxtGlyph.IsVisible = true;
            ImgIcon.IsVisible = false;
        }
        else
        {
            ImgIcon.IsVisible = false;
            TxtGlyph.IsVisible = false;
        }

        ContentHost.Content = content;

        Opened += OnOpened;
        KeyDown += OnKeyDown;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            // WorkingArea is PHYSICAL pixels; Window.Height is DIPs -> divide by scaling.
            // (Repo convention: ChaosHudWindow.axaml.cs:93, ScreenWindowHelper.cs:21.) Treating
            // physical px as DIPs made the cap ~1.3-1.5x too tall on scaled displays, so the
            // window exceeded the screen and the bottom was cut off.
            var screen = Screens.Primary;
            var scale = screen is null || screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            var screenDipH = screen is null ? 0 : screen.WorkingArea.Height / scale;
            if (screenDipH <= 0) screenDipH = 800; // safe fallback
            var maxWindowH = screenDipH * 0.9;

            // The window is NOT auto-sized (SizeToContent removed): it measured the content
            // ScrollViewer at infinite height and broke scrolling (Avalonia issue #6441).
            // Instead measure the hosted content and fit the window to it, capped at maxWindowH.
            // The ScrollViewer then scrolls when a feature popup is taller than the screen;
            // short/medium popups stay snug.
            if (ContentHost.Content is Control content)
            {
                // Content width = window width (520) - outer border (2) - ScrollViewer padding (32).
                const double contentWidth = 486;
                content.Measure(new Size(contentWidth, double.PositiveInfinity));
                var desiredWindowH = content.DesiredSize.Height + 88; // titlebar + padding + border
                Height = Math.Clamp(desiredWindowH, MinHeight, maxWindowH);
            }
            else
            {
                Height = maxWindowH;
            }
            MaxHeight = maxWindowH;
        }
        catch { }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Don't steal Escape while a panic-key picker is open.
            if (IsCapturingPanicKey())
                return;

            Close();
            e.Handled = true;
        }
    }

    private static bool IsCapturingPanicKey()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return desktop.Windows.OfType<ChatShortcutCaptureDialog>().Any(w => w.IsVisible);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
