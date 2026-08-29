using System;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using Serilog;

// NAMESPACE TRAP, and it is not a style preference. Every window under Windows/ lives in the
// FLAT ConditioningControlPanel namespace - not one of them declares ConditioningControlPanel.Windows.
// Declaring it here compiles for a moment and then breaks ScreenOcrService, whose
// `Windows.Graphics.Imaging.BitmapDecoder` is resolved relative to the enclosing
// ConditioningControlPanel namespace: the instant a ConditioningControlPanel.Windows exists, it
// shadows the WinRT `Windows` root and the OCR service stops finding its decoder. Keep it flat.
namespace ConditioningControlPanel;

/// <summary>What the user said when asked whether to mute the avatar while EMI is out.</summary>
public enum EmiMuteChoice
{
    /// <summary>Keep the avatar talking. Also what a dismissed dialog means.</summary>
    Keep = 0,

    /// <summary>Mute the avatar for this visit.</summary>
    Mute = 1,

    /// <summary>Mute, and stop asking. Persisted as <c>EmiDeskMuteDontAsk</c>.</summary>
    DontAsk = 2
}

/// <summary>
/// The one question EMI Desk asks: two voices at once is the failure mode, so when she arrives
/// while something that talks is already switched on, the user picks which voice wins.
///
/// Shown at most once per app session, and only when a talking feature is actually live. Closing
/// it without answering keeps the avatar: silence is never read as consent to being silenced.
/// </summary>
public partial class EmiMutePromptWindow : Window
{
    private EmiMuteChoice _choice = EmiMuteChoice.Keep;

    private EmiMutePromptWindow()
    {
        InitializeComponent();

        TxtTitle.Text = Loc.Get("emi_desk_mute_prompt_title");
        TxtBody.Text = Loc.Get("emi_desk_mute_prompt_body");
        BtnMute.Content = Loc.Get("emi_desk_mute_prompt_mute");
        BtnKeep.Content = Loc.Get("emi_desk_mute_prompt_keep");
        BtnDontAsk.Content = Loc.Get("emi_desk_mute_prompt_dontask");

        BtnMute.Click += (_, _) => Answer(EmiMuteChoice.Mute);
        BtnKeep.Click += (_, _) => Answer(EmiMuteChoice.Keep);
        BtnDontAsk.Click += (_, _) => Answer(EmiMuteChoice.DontAsk);

        // Escape is "keep", matching a dismissed dialog. Never "mute": a reflex key press must not
        // silence something the user did not choose to silence.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(EmiMuteChoice.Keep);
        };
    }

    private void Answer(EmiMuteChoice choice)
    {
        try
        {
            _choice = choice;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] mute prompt answer failed");
            try { Close(); } catch { /* going away anyway */ }
        }
    }

    /// <summary>
    /// Ask, modally, on the UI thread. Never throws: any failure is logged and answered
    /// <see cref="EmiMuteChoice.Keep"/>, because the safe default is leaving the app as it was.
    /// </summary>
    public static EmiMuteChoice Ask()
    {
        try
        {
            var w = new EmiMutePromptWindow();
            try
            {
                var owner = App.MainWindowRef;
                if (owner != null && owner.IsLoaded && owner.IsVisible) w.Owner = owner;
                else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch
            {
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            w.ShowDialog();
            return w._choice;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] mute prompt could not be shown, keeping the avatar");
            return EmiMuteChoice.Keep;
        }
    }
}
