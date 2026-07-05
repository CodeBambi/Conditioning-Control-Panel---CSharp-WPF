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
            if (Screens.Primary is { } screen)
            {
                // Cap the CONTENT scroller, not just the window. The window uses
                // SizeToContent="Height", so it measures the ScrollViewer at infinite height —
                // a window-level MaxHeight therefore only CLIPS tall content instead of scrolling
                // it (the viewport never overflows). Bounding the ScrollViewer itself makes
                // Avalonia's MeasureCore clamp its available height, so it measures its content
                // bounded and actually scrolls when a feature popup is taller than the screen.
                // Short/medium popups stay snug via SizeToContent; only tall ones cap + scroll.
                var screenH = screen.WorkingArea.Height;
                if (screenH > 0)
                {
                    ContentScroll.MaxHeight = screenH * 0.85;
                    MaxHeight = screenH * 0.92; // backstop: window never exceeds the screen
                    InvalidateMeasure();
                }
            }
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
