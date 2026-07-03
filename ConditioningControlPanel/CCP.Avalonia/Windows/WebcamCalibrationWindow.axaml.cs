using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Help;
using Microsoft.Extensions.DependencyInjection;
using Point = global::Avalonia.Point;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the fullscreen 16-point webcam calibration window.
///
/// The real ~1300-LoC calibration pipeline (dot grid sampling, iris-vector
/// capture, polynomial fit, gesture validation, persistence) is deliberately
/// deferred — it is NOT implemented here. To stay honest, this window shows a
/// clear "calibration is not yet available in this version" panel on open and
/// exposes no fake-success path: the dot grid, the gesture checks, and the
/// "Done / verified" panel never run. Any existing calibration produced by the
/// classic (WPF) app keeps working untouched. When the real port lands it will
/// replace the body of this class; the window shell and AXAML are preserved for
/// that future work.
/// </summary>
public partial class WebcamCalibrationWindow : Window
{
    /// <summary>Holds the close result for ShowDialog-style callers.</summary>
    public bool? DialogResult { get; set; }

    /// <summary>True while a calibration window is on screen.</summary>
    public static bool IsShowing { get; private set; }

    /// <summary>Set to true when the user clicks Recalibrate on the verify panel (reserved for the future real flow).</summary>
    public bool WantsRecalibrate { get; private set; }

    private readonly IFrameSource? _frameSource;
    private readonly IVideoSurface? _videoSurface;
    private readonly IDialogService? _dialogService;

    public WebcamCalibrationWindow()
    {
        InitializeComponent();
        IsShowing = true;
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();
    }

    /// <summary>Constructor preserved for callers that still pass the frame/video seams; they are intentionally unused until the real pipeline is ported.</summary>
    public WebcamCalibrationWindow(IFrameSource? frameSource = null, IVideoSurface? videoSurface = null) : this()
    {
        _frameSource = frameSource;
        _videoSurface = videoSurface;
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e) => ShowNotAvailable();

    private void BtnIntroContinue_Click(object? sender, RoutedEventArgs e) => ShowNotAvailable();

    private async void BtnCalibrationHelp_Click(object? sender, RoutedEventArgs e)
    {
        var content = HelpContentService.GetContent("WebcamCalibration");
        if (content?.SectionId == "WebcamCalibration")
        {
            HelpVideoWindow.Show(content, this, topmost: true);
            return;
        }

        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("window_webcam_calibration_help_title"),
                Loc.Get("window_webcam_calibration_help_not_ported_message"),
                DialogSeverity.Info);
        }
    }

    private void BtnErrorClose_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close(false);
    }

    private void BtnVerifyAccuracy_Click(object? sender, RoutedEventArgs e)
    {
        // Verify panel is never reached in this version (calibration is not
        // available). Kept to satisfy the AXAML handler reference.
    }

    private void BtnVerifyRecalibrate_Click(object? sender, RoutedEventArgs e)
    {
        WantsRecalibrate = false;
        DialogResult = false;
        Close(false);
    }

    private void BtnVerifyDone_Click(object? sender, RoutedEventArgs e)
    {
        // No fake success: without a real calibration pipeline there is nothing
        // to confirm, so Done simply closes without marking success.
        DialogResult = false;
        Close(false);
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close(false);
        }
    }

    /// <summary>
    /// The only panel this window shows in the current version: an honest
    /// "not available" message. Hides every interactive surface so no
    /// fake-success path can be reached.
    /// </summary>
    private void ShowNotAvailable()
    {
        DotCanvas.IsVisible = false;
        IntroPanel.IsVisible = false;
        StatusPanel.IsVisible = false;
        ValidationPanel.IsVisible = false;
        VerifyPanel.IsVisible = false;
        TxtErrorDetail.Text = Loc.Get("window_webcam_calibration_not_available_detail");
        ErrorPanel.IsVisible = true;
    }

    /// <summary>
    /// Helper for ShowDialog-style callers. The window only ever shows the
    /// not-available panel in this version, so there is no recalibrate loop;
    /// the method is retained so callers compiled against it keep working and
    /// will light up automatically once the real flow is ported.
    /// </summary>
    public static async Task<bool?> ShowDialogWithRecalibrateAsync(Window? owner)
    {
        try
        {
            var dlg = new WebcamCalibrationWindow();
            return await dlg.ShowDialog<bool?>(owner!);
        }
        catch
        {
            return false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        IsShowing = false;
        base.OnClosed(e);
    }
}
