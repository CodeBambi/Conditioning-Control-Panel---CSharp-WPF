using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "She's Listening" - the voice-control Exclusive, PORTED from
    /// ConditioningControlPanel/Views/Tabs/SheListeningTabView.xaml.cs. A purpose-built surface for
    /// the offline mic features (spoken mantras + the "Hey Bambi" voice commands), with a command
    /// cheat-sheet. The microphone hardware and the voice input modes (device, wake word,
    /// push-to-talk, headphone barge-in) are owned by Settings &gt; Devices since Phase 2 of the UX
    /// restructure and appear here only as read-only chips.
    ///
    /// <para><b>What is real here.</b> The mic-sensitivity dial is a stored threshold on
    /// <see cref="AppSettings.SpeechLoudnessThreshold"/> (Core), so its load, its readout and its
    /// save are the WPF round-trip verbatim - see Core VoiceInputRules.SensToThreshold. The two
    /// audio_whispers plates are painted through <see cref="Helpers.ModArt.TryLoad"/> and repainted
    /// on <see cref="CoreMods.ModChanged"/>, which is the WPF ModResourceResolver behaviour split
    /// across the seam.</para>
    ///
    /// <para>Every button forwards to MainShellWindow.SheListening.cs, as WPF forwards to
    /// MainWindow.SheListening.cs. The Test button (WPF BtnTestVoice_Click ->
    /// AutonomyService.TestVoiceCommand) asks one spoken mantra (MainShellWindow.TestSpokenMantra).</para>
    /// </summary>
    public partial class SheListeningTabView : UserControl
    {
        private bool _isLoading;

        private Windows.MainShellWindow? Main => TopLevel.GetTopLevel(this) as Windows.MainShellWindow;

        // Each "What you can say" row's phrase lines, as authored (rows are rebuilt from these).
        private readonly Dictionary<TextBlock, List<Inline>> _voiceRowLines = new();

        /// <summary>
        /// "What you can say" names only what this head can do. The axaml keeps WPF's full list; each
        /// row header carries <c>Tag="voice:intent|intent"</c>. A row whose phrase lines match its
        /// intents one to one keeps only the runnable lines; any other row shows iff one is runnable.
        /// </summary>
        internal void ShowOnlyVoiceCommands(ISet<string> runnable)
        {
            foreach (var head in this.GetLogicalDescendants().OfType<TextBlock>()
                         .Where(t => t.Tag is string tag && tag.StartsWith("voice:")).ToList())
            {
                var names = ((string)head.Tag!)["voice:".Length..].Split('|');
                var any = names.Any(runnable.Contains);
                head.IsVisible = any;
                if (head.Parent is not Panel panel || panel.Children.IndexOf(head) + 1 >= panel.Children.Count
                    || panel.Children[panel.Children.IndexOf(head) + 1] is not TextBlock desc) continue;
                desc.IsVisible = any;
                if (desc.Inlines is not { } inlines) continue;
                if (!_voiceRowLines.TryGetValue(desc, out var all)) _voiceRowLines[desc] = all = inlines.ToList();
                var lines = new List<List<Inline>> { new() };
                foreach (var i in all) { if (i is LineBreak) lines.Add(new()); else lines[^1].Add(i); }
                if (lines.Count != names.Length) continue;
                inlines.Clear();
                foreach (var (line, name) in lines.Zip(names).Where(l => runnable.Contains(l.Second)))
                {
                    if (inlines.Count > 0) inlines.Add(new LineBreak());
                    inlines.AddRange(line);
                }
            }
        }

        public SheListeningTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            BtnSL_MicMaster.Click += (_, _) => Main?.ToggleVoiceMic();
            BtnSL_OpenDeviceSettings.Click += (_, _) => Main?.OpenDeviceSettings();
            BtnSL_Calibrate.Click += (_, _) => Main?.SL_Calibrate_Click();
            BtnSL_TestMantra.Click += (_, _) => Main?.TestSpokenMantra();
            BtnSL_RevokeConsent.Click += (_, _) => Main?.SL_RevokeMicConsent_Click();
            BtnSL_GateUnlock.Click += (s, e) => Main?.BtnGateUnlock_Click(s, e);
            ChkSL_Mantras.IsCheckedChanged += (_, _) => Main?.SL_Mantras_Changed();

            // MainWindow.SheListening.cs:419, both halves. The readout uses Math.Round, not a
            // truncating cast, so 49.6 reads 50 the way it does on WPF.
            SldSL_MicSensitivity.ValueChanged += (_, e) =>
            {
                TxtSL_MicSensitivity.Text = $"{(int)Math.Round(e.NewValue)}%";
                if (_isLoading) return;
                CoreSettings.Current.SpeechLoudnessThreshold = Services.Speech.VoiceInputRules.SensToThreshold(e.NewValue);
                CoreSettings.Save();
            };

            _isLoading = true;
            SldSL_MicSensitivity.Value = Services.Speech.VoiceInputRules.ThresholdToSens(CoreSettings.Current.SpeechLoudnessThreshold);
            _isLoading = false;

            ApplyFeatureArt();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            ApplyFeatureArt();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>ModChanged can be raised off the UI thread, so the repaint is marshalled.</summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(ApplyFeatureArt);

        /// <summary>
        /// The WPF ImageBrush pair, resolved mod-first. A null answer means neither the mod nor this
        /// head has the picture, and the authored surface - a bare hero, the wash on the side card -
        /// stands, which is what WPF's resolver falls back to as well.
        /// </summary>
        private void ApplyFeatureArt()
        {
            var art = Helpers.ModArt.TryLoad("features/audio_whispers.png");
            if (art == null) return;

            SheListeningHeroArt.Background = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
            };
            SheListeningSideArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        }
    }
}
