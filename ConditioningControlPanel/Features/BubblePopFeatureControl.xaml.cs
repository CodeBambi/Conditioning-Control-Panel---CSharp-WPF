using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubblePopFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public BubblePopFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            // The egg hint names the active persona, and the hero/side plates are mod art; the
            // rack hosts this control permanently, so a mod switch must repaint them (a popup
            // instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
            Services.BubbleService.AmbientXpBudgetChanged += OnAmbientXpBudgetChanged;
            UpdateAmbientXpBudgetLine();
            if (App.Webcam != null) App.Webcam.OnTrackingStateChanged += OnWebcamTrackingStateChanged;
            UpdateGazeHint();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            Services.BubbleService.AmbientXpBudgetChanged -= OnAmbientXpBudgetChanged;
            if (App.Webcam != null) App.Webcam.OnTrackingStateChanged -= OnWebcamTrackingStateChanged;
        }

        /// <summary>
        /// The camera raises this off the UI thread, so the repaint is marshalled and swallowed on
        /// a shutting-down dispatcher (CLAUDE.md async/threading known issues #6/#8).
        /// </summary>
        private void OnWebcamTrackingStateChanged(Services.WebcamTrackingState _)
        {
            try
            {
                var disp = Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(new Action(UpdateGazeHint));
            }
            catch { }
        }

        /// <summary>
        /// "Stare to pop" is a stored preference, but it does nothing until the shared camera is
        /// actually running on a stored calibration with current consent - the same three
        /// conditions GazeFocusService.EvaluateDesiredState checks before it will start the dwell
        /// engine. Without this line the toggle reads as broken, so the row stays live and gains a
        /// hint rather than being hidden or disabled.
        /// </summary>
        private void UpdateGazeHint()
        {
            try
            {
                if (TxtBubbleGazeHint == null) return;
                var cam = App.Webcam;
                var ready = cam != null
                            && cam.IsRunning
                            && cam.Calibration != null
                            && Services.WebcamTrackingService.IsConsentCurrent();
                TxtBubbleGazeHint.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        /// <summary>
        /// The service raises this from the pop path, which can be off this control's thread once the
        /// pop is queued, so the repaint is marshalled and swallowed on a shutting-down dispatcher
        /// (CLAUDE.md async/threading known issues #6/#8).
        /// </summary>
        private void OnAmbientXpBudgetChanged()
        {
            try
            {
                var disp = Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                disp.BeginInvoke(new Action(UpdateAmbientXpBudgetLine));
            }
            catch { }
        }

        /// <summary>
        /// "Ambient bubble XP: N/300 today" (#1019/#1026). Ambient pops stop paying once the daily
        /// bucket is spent; before this line the ceiling was completely invisible and two users
        /// reported the XP system itself as broken.
        /// </summary>
        private void UpdateAmbientXpBudgetLine()
        {
            try
            {
                if (TxtAmbientXpBudget == null) return;
                TxtAmbientXpBudget.Text = Localization.Loc.GetF("label_ambient_bubble_xp_budget",
                    Services.BubbleService.AmbientBubbleXpPaidToday(),
                    Services.BubbleService.AmbientBubbleDailyXpCap);
            }
            catch { }
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// One handler, both repaints - the persona line and the two art plates change answer on
        /// exactly the same event, and splitting them would be two BeginInvokes for one switch.
        /// </summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(() => { LoadFromSettings(); ApplyFeatureArt(); }));
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubblesEnabled;
                SliderFreq.Value = s.BubblesFrequency;
                TxtFreq.Text = s.BubblesFrequency.ToString();
                SliderVolume.Value = s.BubblesVolume;
                TxtVolume.Text = $"{s.BubblesVolume}%";
                SliderSize.Value = s.BubblesSize;
                TxtSize.Text = $"{s.BubblesSize}%";
                SliderSpeed.Value = s.BubbleSpeedBoost;
                TxtSpeed.Text = $"+{s.BubbleSpeedBoost}%";
                ChkSolidMode.IsChecked = s.BubbleSharedHost;
                ChkBubbleGazePop.IsChecked = s.BubbleGazePopEnabled;

                // Easter-egg hint (companion auto-pops a lingering effect bubble) — name the active persona.
                var persona = App.Mods?.ActiveModId switch
                {
                    "builtin-bambisleep" => "Bambi",
                    "builtin-sissyhypno" => "your bimbo",
                    "builtin-locked" => "Circe",
                    _ => "your companion"
                };
                TxtTriggerEggHint.Text = $"careful — {persona} loves these…";

                ChkTriggers.IsChecked = s.BubbleTriggersEnabled;
                TriggerOptionsPanel.Visibility = s.BubbleTriggersEnabled
                    ? Visibility.Visible : Visibility.Collapsed;
                SliderTriggerChance.Value = s.BubbleTriggerChance;
                TxtTriggerChance.Text = $"{s.BubbleTriggerChance}%";
                var ids = s.BubbleTriggerVariants ?? new System.Collections.Generic.List<string>();
                ChkTypeFlash.IsChecked = ids.Contains("flash");
                ChkTypeSubliminal.IsChecked = ids.Contains("subliminal");
                ChkTypePink.IsChecked = ids.Contains("pink");
                ChkTypeSpiral.IsChecked = ids.Contains("spiral");
                ChkTypeGlitch.IsChecked = ids.Contains("glitch");
                ChkTypeCascade.IsChecked = ids.Contains("htlink");
                ChkTypeVideo.IsChecked = ids.Contains("video");
                UpdateAmbientXpBudgetLine();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BubblesEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesVolume) ||
                e.PropertyName == nameof(Models.AppSettings.BubblesSize) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleSpeedBoost) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleGazePopEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggersEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggerChance) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleTriggerVariants))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.BubblesEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop bubble service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.Bubbles?.Start();
                else
                    App.Bubbles?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.BubblesFrequency = v;
            try { App.Bubbles?.RefreshFrequency(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Bubbles RefreshFrequency failed"); }
            App.Settings?.Save();
        }

        private void SliderVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            s.BubblesVolume = v;
            App.Settings?.Save();
        }

        private void SliderSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSize.Text = $"{v}%";
            s.BubblesSize = v;
            App.Settings?.Save();
            // No live-apply hook: size is read when each bubble is CONSTRUCTED, so the change
            // shows on the next spawn without disturbing the ones already drifting. Restarting the
            // service to resize mid-flight would pop the field out from under the user.
        }

        private void SliderSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtSpeed.Text = $"+{v}%";
            s.BubbleSpeedBoost = v;
            App.Settings?.Save();
        }

        /// <summary>
        /// The bubble twin of FlashFeatureControl's ChkFlashGazePop_Changed. GazeFocusService
        /// listens to the setting itself (EvaluateDesiredState), so there is nothing to start or
        /// stop here - flipping this on with the camera already up arms bubble dwell on the next
        /// tick, and flipping it off releases any dwell in progress.
        /// </summary>
        private void ChkBubbleGazePop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BubbleGazePopEnabled = ChkBubbleGazePop.IsChecked ?? false;
            App.Settings?.Save();
            UpdateGazeHint();
        }

        private void ChkSolidMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BubbleSharedHost = ChkSolidMode.IsChecked ?? false;
            App.Settings?.Save();

            // The render path is latched per Start->Stop session, so bounce a live bubble service to
            // pick up the new mode (no-op when bubbles aren't currently running).
            if (App.IsEngineRunning && s.BubblesEnabled && App.Bubbles?.IsRunning == true)
            {
                App.Bubbles.Stop();
                App.Bubbles.Start();
            }
        }

        private void ChkTriggers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkTriggers.IsChecked ?? false;
            s.BubbleTriggersEnabled = on;
            TriggerOptionsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            App.Settings?.Save();
        }

        private void SliderTriggerChance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTriggerChance.Text = $"{v}%";
            s.BubbleTriggerChance = v;
            App.Settings?.Save();
        }

        private void TriggerType_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            if (sender is not CheckBox cb || cb.Tag is not string id) return;

            var ids = new System.Collections.Generic.List<string>(
                s.BubbleTriggerVariants ?? new System.Collections.Generic.List<string>());
            var on = cb.IsChecked ?? false;
            if (on) { if (!ids.Contains(id)) ids.Add(id); }
            else ids.Remove(id);
            s.BubbleTriggerVariants = ids;   // reassign so the setter fires change notification
            App.Settings?.Save();
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/Bubble_pop.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
        /// width, so each is decoded once for the whole session per mod.</para>
        ///
        /// <para>The brushes are mutated in place. Swapping the <c>Border.Background</c> object
        /// would work too and would throw away the XAML-declared Stretch/AlignmentX/Opacity with
        /// it; a frozen brush would silently never repaint at all, which is why they are named
        /// rather than declared inline as literals.</para>
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BubblePopFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

    }
}
