using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF Settings > Devices live apply (DevicesSettingsSection.xaml.cs:252): cut the open
        /// capture so the wake loop reopens on the new microphone or the new phrases, then reconcile.
        /// A no-op beyond the reconcile when the mic is not armed.</summary>
        internal void ReopenVoiceInput()
        {
            try
            {
                if (Services.Speech.VoiceInputRules.MicIsArmed(CoreSettings.Current))
                {
                    try { VoiceSpeech?.StopListening(); } catch (Exception ex) { Diag.Swallowed(ex); }
                    StopWakeLoop();
                }
                RefreshVoiceInputModes();
            }
            catch (Exception ex) { Log.Warning(ex, "ReopenVoiceInput failed"); }
        }
    }
}
