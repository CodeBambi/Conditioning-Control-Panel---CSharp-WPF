using System;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "Bambi Takeover" - the autonomy Exclusive, PORTED from
    /// ConditioningControlPanel/Views/Tabs/BambiTakeoverTabView.xaml.cs.
    ///
    /// <para><b>Almost every handler on the WPF head is a one-line shim</b> - it looks up the
    /// hosting MainWindow and forwards to a MainWindow.Autonomy partial
    /// (BtnAutonomyStartStop_Click, BtnForceStartAutonomy_Click, BtnGateUnlock_Click,
    /// BtnTestAutonomy_Click, BtnTestVoice_Click, ChkAutonomyVoice_Changed,
    /// ChkAutonomyResume_Changed, ChkShowTakeoverCountdown_Changed, OpenDeviceSettings,
    /// ChkAutonomyBehavior_Changed, BtnWallpaperFolder_Click, ChkWallpaperKeep_Changed,
    /// SliderWallpaperDuration_Changed, ChkAutonomyEnabled_Changed, ChkAutonomyIdle_Changed,
    /// ChkAutonomyRandom_Changed, ChkAutonomyTimeAware_Changed, and the four
    /// SliderAutonomy*_Changed). None of those partials is on this head, so each is a stub with
    /// the same name so the eventual wiring diffs cleanly.</para>
    ///
    /// <para>The six value readouts beside the sliders ARE ported: painting a number next to its
    /// slider is pure view state, and the formats are copied verbatim from
    /// MainWindow.Autonomy.cs (253/261/269/438/447) and this view's own LoadMantraChant, so the
    /// truncating cast and the "s"/"%" suffixes match. Only the settings write and the service
    /// call each handler also does are the stubbed half. Same split SheListeningTabView made for
    /// the mic-sensitivity readout.</para>
    ///
    /// <para><b>Dropped:</b> the mod-aware feature art (ModService.ModChanged -&gt;
    /// ModResourceResolver repainting the hero and side takeover.png plates, including the
    /// BambiSleep "bambi takeover.png" fork). Both plates are art-less on this head - see the
    /// .axaml header - so there is nothing to repaint yet. ponytail: needs ModService +
    /// ModResourceResolver, restore together with the plates when Resources/features ships here.
    /// The Mantra Chant load/save (App.Settings + App.MantraChant) is the same story.</para>
    /// </summary>
    public partial class BambiTakeoverTabView : UserControl
    {
        public BambiTakeoverTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs MainWindow.Autonomy (AutonomyService, the premium gate, the wallpaper
            // folder picker, Settings → Devices); wired when they move to Core.
            BtnAutonomyStartStop.Click += (_, _) => { };   // mw.BtnAutonomyStartStop_Click(...)
            BtnForceStartAutonomy.Click += (_, _) => { };  // mw.BtnForceStartAutonomy_Click(...)
            BtnGateUnlock.Click += (_, _) => { };          // mw.BtnGateUnlock_Click(...)
            BtnTestAutonomy.Click += (_, _) => { };        // mw.BtnTestAutonomy_Click(...)
            BtnTestVoice.Click += (_, _) => { };           // mw.BtnTestVoice_Click(...)
            BtnOpenDeviceSettings.Click += (_, _) => { };  // mw.OpenDeviceSettings()
            BtnWallpaperFolder.Click += (_, _) => { };     // mw.BtnWallpaperFolder_Click(...)

            // Triggers + session toggles. WPF split these across Checked/Unchecked; Avalonia 11
            // raises one IsCheckedChanged for both edges.
            ChkAutonomyIdle.IsCheckedChanged += (_, _) => { };              // mw.ChkAutonomyIdle_Changed(...)
            ChkAutonomyRandom.IsCheckedChanged += (_, _) => { };            // mw.ChkAutonomyRandom_Changed(...)
            ChkAutonomyTimeAware.IsCheckedChanged += (_, _) => { };         // mw.ChkAutonomyTimeAware_Changed(...)
            ChkAutonomyResumeOnStartup.IsCheckedChanged += (_, _) => { };   // mw.ChkAutonomyResume_Changed(...)
            ChkAutonomyVoice.IsCheckedChanged += (_, _) => { };             // mw.ChkAutonomyVoice_Changed(...)
            ChkShowTakeoverCountdown.IsCheckedChanged += (_, _) => { };     // mw.ChkShowTakeoverCountdown_Changed(...)
            ChkWallpaperKeep.IsCheckedChanged += (_, _) => { };             // mw.ChkWallpaperKeep_Changed(...)
            ChkAutonomyEnabled.IsCheckedChanged += (_, _) => { };           // mw.ChkAutonomyEnabled_Changed(...)

            // The behaviour grid: eleven toggles that all route to the SAME handler on WPF.
            foreach (var chk in new[]
                     {
                         ChkAutonomyFlash, ChkAutonomyVideo, ChkAutonomyWebVideo, ChkProtectBrowserVideo,
                         ChkAutonomySubliminal, ChkAutonomyComment, ChkAutonomyBubbles, ChkAutonomyPinkFilter,
                         ChkAutonomyLockCard, ChkAutonomyBouncingText, ChkAutonomyMindWipe, ChkAutonomyWallpaper,
                         ChkAutonomySpiral, ChkAutonomyBubbleCount,
                     })
                chk.IsCheckedChanged += (_, _) => { };  // mw.ChkAutonomyBehavior_Changed(...)

            // View-only half of the five slider handlers: the readout beside each one. Formats
            // copied verbatim - a truncating (int) cast, not Math.Round, so 59.9 reads 59 the way
            // it does on WPF. The settings write + RefreshRandomTimer() are the stubbed half.
            SliderAutonomyInterval.ValueChanged += (_, e) => TxtAutonomyInterval.Text = $"{(int)e.NewValue}s";
            SliderAutonomyCooldown.ValueChanged += (_, e) => TxtAutonomyCooldown.Text = $"{(int)e.NewValue}s";
            SliderAutonomyIntensity.ValueChanged += (_, e) => TxtAutonomyIntensity.Text = $"{(int)e.NewValue}";
            SliderAutonomyAnnounce.ValueChanged += (_, e) => TxtAutonomyAnnounce.Text = $"{(int)e.NewValue}%";
            SliderWallpaperDuration.ValueChanged += (_, e) => TxtWallpaperDuration.Text = $"{(int)e.NewValue}s";

            // Mantra Chant (#653) is the one block whose handlers really live in this view. The
            // settings read/write and App.MantraChant.Start/Stop/ApplyVolume are stubbed; the two
            // readouts are real, with LoadMantraChant's Math.Round formats
            // (BambiTakeoverTabView.xaml.cs:118/120) rather than the sliders' truncating cast.
            // ponytail: needs App.Settings + MantraChantService, wired when they move to Core.
            SldMantraChantVolume.ValueChanged += (_, e) => TxtMantraChantVolume.Text = $"{(int)Math.Round(e.NewValue)}%";
            SldMantraChantGap.ValueChanged += (_, e) => TxtMantraChantGap.Text = $"{(int)Math.Round(e.NewValue)}s";
            ChkMantraChant.IsCheckedChanged += (_, _) => { };

            // Placeholder start values; the real ones come from settings when the services land.
            // These fire the two handlers above, so the em-dash placeholders in the XAML are
            // replaced by real readings in --render-all rather than staying "—".
            SldMantraChantVolume.Value = 70;
            SldMantraChantGap.Value = 8;
        }
    }
}
