using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Views.Tabs;

namespace ConditioningControlPanel
{
    // -------------------------------------------------------------------------------------------
    // Training Programs tab (Views/Tabs/ProgramsTabView.xaml) and the dashboard's Today card.
    //
    // The view is dumb: every handler forwards here, and every refresh is a full repaint from
    // App.Programs. There is no ViewModel layer in this app, so the item lists are rebuilt as
    // plain carriers (Views/Tabs/ProgramsTabItems.cs) and handed to ItemsSource.
    // -------------------------------------------------------------------------------------------
    public partial class MainWindow
    {
        #region Programs Tab

        /// <summary>One-shot guard: the service outlives the tab, so we subscribe exactly once.</summary>
        private bool _programsSubscribed;

        // -----------------------------------------------------------------------------------
        // "New tab" attention pulse
        //
        // A straight port of the Deeper tab's pulse (MainWindow.DeeperTab.cs) rather than a shared
        // helper. The two are only incidentally identical: Deeper's is spent history nobody should be
        // touching, and factoring them together would couple a shipped announcement to the next one.
        // When a third tab needs this, that is the point to generalise.
        // -----------------------------------------------------------------------------------

        private bool _programsPulseRunning;

        /// <summary>
        /// Grows the Programs tab button four times, then stops for good.
        ///
        /// The scale is animated on a ScaleTransform declared in XAML (BtnProgramsScale) because the
        /// button's Style is the shared TabButton template - animating the transform leaves the
        /// template alone, so the tab still restyles normally when it becomes the active one.
        /// </summary>
        private void StartProgramsTabPulse()
        {
            if (BtnProgramsScale == null || _programsPulseRunning) return;
            _programsPulseRunning = true;
            var anim = new DoubleAnimation
            {
                From = 1.0,
                To = 1.12,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            anim.Completed += (_, _) =>
            {
                _programsPulseRunning = false;
                if (BtnProgramsScale != null)
                {
                    BtnProgramsScale.ScaleX = 1.0;
                    BtnProgramsScale.ScaleY = 1.0;
                }
            };
            BtnProgramsScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            BtnProgramsScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        /// <summary>
        /// Detaches the animation and forces the button back to rest. Passing null to BeginAnimation
        /// is what releases the animation's hold on the property - without it the last animated value
        /// sticks, and a click landing mid-pulse would leave the tab button permanently oversized.
        /// </summary>
        private void StopProgramsTabPulse()
        {
            if (!_programsPulseRunning && BtnProgramsScale == null) return;
            _programsPulseRunning = false;
            if (BtnProgramsScale != null)
            {
                BtnProgramsScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                BtnProgramsScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                BtnProgramsScale.ScaleX = 1.0;
                BtnProgramsScale.ScaleY = 1.0;
            }
        }

        // -----------------------------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------------------------

        private void EnsureProgramsSubscribed()
        {
            if (_programsSubscribed) return;
            var svc = App.Programs;
            if (svc == null) return;

            svc.TodayChanged += OnProgramTodayChanged;
            svc.ProgramLapsed += OnProgramLapsed;
            svc.ProgramGraduated += OnProgramGraduated;
            _programsSubscribed = true;
        }

        private void OnProgramTodayChanged(object? sender, EventArgs e) => MarshalProgramRefresh();

        private void OnProgramLapsed(object? sender, Services.Program.ProgramLapsedEventArgs e) => MarshalProgramRefresh();

        private void OnProgramGraduated(object? sender, Services.Program.ProgramDayEventArgs e) => MarshalProgramRefresh();

        /// <summary>
        /// The service raises from a dispatcher timer today, but it may raise from a background
        /// completion tomorrow - marshal unconditionally and bail if the app is shutting down.
        /// </summary>
        private void MarshalProgramRefresh()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                if (dispatcher.CheckAccess()) RefreshProgramsUI();
                else dispatcher.BeginInvoke(new Action(RefreshProgramsUI));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program refresh marshalling failed");
            }
        }

        /// <summary>Loaded hook on the dashboard card - the earliest point we can subscribe without touching App.</summary>
        internal void ProgramTodayCard_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureProgramsSubscribed();
            RefreshProgramTodayCard();
        }

        // -----------------------------------------------------------------------------------
        // Small helpers
        // -----------------------------------------------------------------------------------

        private static Brush ProgramThemeBrush(string key, Brush fallback)
        {
            try { return Application.Current?.TryFindResource(key) as Brush ?? fallback; }
            catch { return fallback; }
        }

        private static Brush ProgramAccentBrush(string? hex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color color)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    return brush;
                }
            }
            catch { /* a bad accent must never break the tab */ }

            return ProgramThemeBrush("PinkBrush", Brushes.HotPink);
        }

        private static bool ProgramHasPremium => App.Patreon?.HasPremiumAccess == true;

        /// <summary>
        /// Horizontal accent wash for the rail's completed segment. Derived from the program accent
        /// rather than a theme colour so it can never clash with a mod, and gradient-only so the
        /// rail has depth without a decorative image strip behind it.
        /// </summary>
        private static Brush ProgramRailFillBrush(Brush accent)
        {
            try
            {
                if (accent is SolidColorBrush solid)
                {
                    var color = solid.Color;
                    var faded = Color.FromArgb((byte)(color.A * 0.40), color.R, color.G, color.B);
                    var gradient = new LinearGradientBrush(faded, color, 0);
                    gradient.Freeze();
                    return gradient;
                }
            }
            catch { /* a gradient failing must never break the rail */ }

            return accent;
        }

        /// <summary>
        /// Frozen radial glow derived from the program accent - the sigil halo, the breathing node
        /// halo and the hero band's ambient wash all come from here so they can never clash with a
        /// mod. Alpha and centre vary per use; the brush is frozen, so building one per refresh is
        /// free.
        /// </summary>
        private static Brush ProgramRadialGlowBrush(Brush accent, byte alpha,
                                                    double centerX = 0.5, double centerY = 0.5,
                                                    double radius = 0.75)
        {
            try
            {
                if (accent is SolidColorBrush solid)
                {
                    var c = solid.Color;
                    var brush = new RadialGradientBrush(
                        Color.FromArgb(alpha, c.R, c.G, c.B),
                        Color.FromArgb(0, c.R, c.G, c.B))
                    {
                        Center = new Point(centerX, centerY),
                        GradientOrigin = new Point(centerX, centerY),
                        RadiusX = radius,
                        RadiusY = radius
                    };
                    brush.Freeze();
                    return brush;
                }
            }
            catch { /* a glow failing must never break the tab */ }

            return Brushes.Transparent;
        }

        /// <summary>
        /// Points a tinted-art Rectangle at an image.
        ///
        /// The art ships as white RGB with the luminance in the ALPHA channel, so the Rectangle is
        /// filled with the program accent and the image is used as its OPACITY MASK: bright source
        /// becomes full accent, dark source becomes fully transparent. The alternative - a
        /// translucent accent laid over opaque greyscale - lifts the blacks to half-accent and gives
        /// the washed-out "failed tint" look. Do not swap it back.
        ///
        /// The declared XAML brush is reused when it is still writable; if WPF ever hands one back
        /// frozen, a fresh frozen brush is swapped in instead, so this can never throw at paint time.
        /// </summary>
        private static void ApplyProgramArtMask(System.Windows.Shapes.Rectangle target,
                                                ImageBrush declared, ImageSource image,
                                                Stretch stretch = Stretch.Uniform)
        {
            if (!declared.IsFrozen && ReferenceEquals(target.OpacityMask, declared))
            {
                declared.ImageSource = image;
                declared.Stretch = stretch;
                return;
            }

            var fresh = new ImageBrush(image) { Stretch = stretch };
            fresh.Freeze();
            target.OpacityMask = fresh;
        }

        /// <summary>
        /// The app's own feature icon for a task, keyed off the signal that verifies it.
        /// Rituals and anything without a mapped verifier return null and the row keeps its glyph
        /// alone. These stay FULL-COLOUR: it is the same product iconography the Dashboard shows
        /// under every mod, so a tinted copy here would read as a different, lesser set.
        /// </summary>
        private static string? ProgramTaskIconPath(ProgramTask task)
        {
            if (task.Kind != ProgramTaskKind.AutoVerified || task.Verifier == null) return null;

            return task.Verifier switch
            {
                Models.QuestCategory.Bubbles => "features/Bubble_pop.png",
                Models.QuestCategory.LockCard => "features/Phrase_Lock.png",
                Models.QuestCategory.Video => "features/mandatory_videos.png",
                Models.QuestCategory.BubbleCount => "features/Bubble_count.png",
                Models.QuestCategory.PinkFilter => "features/Pink_filter.png",
                Models.QuestCategory.Flash => "features/flash.png",
                Models.QuestCategory.Spiral => "features/spiral_overlay.png",
                _ => null
            };
        }

        /// <summary>
        /// The feature layers a program session can turn on, roughly in the order they arrive on
        /// screen: a predicate over the day's settings, a localisation key, and the app's own
        /// product icon for that feature.
        ///
        /// An explicit table rather than reflection over SessionSettings' *Enabled flags: a new flag
        /// should be a deliberate entry here with a name and an icon, not a chip that appears on its
        /// own labelled with a property name. Anything dead in the engine (PopQuiz, MiniGame,
        /// BrainDrain) is deliberately absent - see the template notes in BuiltInPrograms.
        /// </summary>
        private static readonly (Func<Models.SessionSettings, bool> IsOn, string LabelKey, string IconPath)[]
            ProgramLayerCatalog =
        {
            (s => s.FlashEnabled,            "programs_layer_flash",       "features/flash.png"),
            (s => s.SubliminalEnabled,       "programs_layer_subliminal",  "features/subliminal.png"),
            (s => s.AudioWhispersEnabled,    "programs_layer_whispers",    "features/audio_whispers.png"),
            (s => s.BouncingTextEnabled,     "programs_layer_bouncing",    "features/bouncing_text.png"),
            (s => s.BubblesEnabled,          "programs_layer_bubbles",     "features/Bubble_pop.png"),
            (s => s.PinkFilterEnabled,       "programs_layer_pink",        "features/Pink_filter.png"),
            (s => s.SpiralEnabled,           "programs_layer_spiral",      "features/spiral_overlay.png"),
            (s => s.MandatoryVideosEnabled,  "programs_layer_video",       "features/mandatory_videos.png"),
            (s => s.LockCardEnabled,         "programs_layer_lockcard",    "features/Phrase_Lock.png"),
            (s => s.BubbleCountEnabled,      "programs_layer_bubblecount", "features/Bubble_count.png"),
            (s => s.MindWipeEnabled,         "programs_layer_mindwipe",    "features/Mind_Wipers.png"),
            (s => s.CornerGifEnabled,        "programs_layer_cornergif",   "features/corner_gif.png")
        };

        /// <summary>
        /// The settings a day's session will run with, for display only.
        ///
        /// ProgramSessionBuilder lerps numeric fields but takes booleans, enums and phrase pools
        /// straight from the template's Floor, so the floor IS the feature list and no lerp is
        /// needed. Deliberately does NOT go through ProgramService.BuildTodaySession: that arms
        /// _expectedSessionId, and a repaint must never make the service believe the tab started a
        /// session. A day carrying sparse overrides can flip a flag, so those days - and only those -
        /// pay for a clone.
        /// </summary>
        private static Models.SessionSettings? ProgramDaySettings(ProgramDefinition program, ProgramDay? day)
        {
            if (day == null) return null;

            var template = program.GetTemplate(day.SessionTemplateId);
            if (template?.Floor == null) return null;
            if (day.Overrides is not { Count: > 0 }) return template.Floor;

            try
            {
                var copy = Services.Program.ProgramSessionBuilder.Clone(template.Floor);
                Services.Program.ProgramSessionBuilder.ApplyOverrides(copy, day.Overrides);
                return copy;
            }
            catch (Exception ex)
            {
                // A bad override must cost the NEW markers, never the panel.
                App.Logger?.Warning(ex, "Program day {Day} overrides could not be previewed", day.DayIndex);
                return template.Floor;
            }
        }

        // -----------------------------------------------------------------------------------
        // Refresh
        // -----------------------------------------------------------------------------------

        internal void RefreshProgramsUI()
        {
            try
            {
                EnsureProgramsSubscribed();

                var tab = ProgramsTab;
                if (tab == null) return;

                EnsureProgramsFxHooked(tab);

                // Remember whether the run view was already on screen: the entrance animation
                // plays on its first reveal only, never on the once-a-second event refreshes.
                var runWasVisible = tab.ProgramsRunPanel.Visibility == Visibility.Visible;

                tab.ProgramsBrowsePanel.Visibility = Visibility.Collapsed;
                tab.ProgramsRunPanel.Visibility = Visibility.Collapsed;
                tab.ProgramsLapsedPanel.Visibility = Visibility.Collapsed;
                tab.ProgramsGraduatedPanel.Visibility = Visibility.Collapsed;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;

                if (svc == null || enrollment == null || program == null ||
                    enrollment.State == ProgramEnrollmentState.Withdrawn)
                {
                    BuildProgramBrowseList();
                    tab.ProgramsBrowsePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    switch (enrollment.State)
                    {
                        case ProgramEnrollmentState.Lapsed:
                            BuildProgramLapsedPanel(program, enrollment);
                            tab.ProgramsLapsedPanel.Visibility = Visibility.Visible;
                            break;

                        case ProgramEnrollmentState.Graduated:
                            BuildProgramGraduatedPanel(program, enrollment);
                            tab.ProgramsGraduatedPanel.Visibility = Visibility.Visible;
                            break;

                        default:
                            BuildProgramRunPanel(program, enrollment);
                            tab.ProgramsRunPanel.Visibility = Visibility.Visible;
                            if (!runWasVisible && tab.IsVisible) StartProgramRunEntrance(tab);
                            break;
                    }
                }

                RefreshProgramTodayCard();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshProgramsUI failed");
            }
        }

        // -----------------------------------------------------------------------------------
        // Browse
        // -----------------------------------------------------------------------------------

        private void BuildProgramBrowseList()
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var library = svc?.Library ?? (IReadOnlyList<ProgramDefinition>)Array.Empty<ProgramDefinition>();

            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var pinkBrush = ProgramThemeBrush("PinkBrush", Brushes.HotPink);
            var surfaceBrush = ProgramThemeBrush("SurfaceBgBrush", Brushes.Transparent);
            var tintBrush = ProgramThemeBrush("TransparentPinkBrush", Brushes.Transparent);

            var items = new List<ProgramBrowseItem>();

            foreach (var def in library)
            {
                var isPremium = def.Tier == ProgramTier.Premium;
                var locked = isPremium && !ProgramHasPremium;

                var canEnroll = true;
                if (svc != null)
                {
                    // The service's reason strings are diagnostics, not UI copy - log, show our own.
                    canEnroll = svc.CanEnroll(def, out var reason);
                    if (!canEnroll && !locked)
                        App.Logger?.Debug("Program {Program} not enrollable: {Reason}", def.Id, reason);
                }

                var item = new ProgramBrowseItem
                {
                    ProgramId = def.Id,
                    Icon = def.Icon,
                    Title = def.Title,
                    Subtitle = def.Subtitle,
                    Pitch = def.Pitch,
                    LengthLabel = Loc.GetF("programs_length_days", def.LengthDays),
                    TierLabel = isPremium ? Loc.Get("programs_tier_premium") : Loc.Get("programs_tier_free"),
                    TierBrush = isPremium ? pinkBrush : mutedBrush,
                    TierBackground = isPremium ? tintBrush : surfaceBrush,
                    AccentBrush = ProgramAccentBrush(def.AccentColor),
                    IsLocked = locked
                };

                if (locked)
                {
                    item.ActionText = Loc.Get("btn_program_locked");
                    item.IsActionEnabled = true; // routes to the App Info popup, never to enrollment
                    item.ReasonText = Loc.Get("programs_locked_hint");
                    item.ReasonVisibility = Visibility.Visible;
                    item.CardOpacity = 0.72;
                }
                else if (!canEnroll)
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                    item.IsActionEnabled = false;
                    item.ReasonText = Loc.Get("programs_unavailable");
                    item.ReasonVisibility = Visibility.Visible;
                    item.CardOpacity = 0.72;
                }
                else
                {
                    item.ActionText = Loc.Get("btn_program_enroll");
                }

                items.Add(item);
            }

            tab.ProgramLibraryList.ItemsSource = items;
            tab.TxtProgramsBrowseEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // -----------------------------------------------------------------------------------
        // Run view
        // -----------------------------------------------------------------------------------

        private void BuildProgramRunPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var accent = ProgramAccentBrush(program.AccentColor);
            var chapter = svc?.TodayChapter;

            // Identity slot. The sigil is the accent masked by white-RGB/alpha art, so it can only
            // ever be the program's own colour; with no art at all the original 4px bar comes back
            // and the header is plainer, never broken.
            tab.RunAccentBar.Background = accent;

            var sigil = Services.Program.ProgramArt.Sigil(program);
            if (sigil != null)
            {
                ApplyProgramArtMask(tab.RunSigil, tab.RunSigilMask, sigil);
                tab.RunSigil.Fill = accent;
                tab.RunSigilGlow.Fill = ProgramRadialGlowBrush(accent, 150);
                tab.RunSigilHost.Visibility = Visibility.Visible;
                tab.RunSigilGlow.Visibility = Visibility.Visible;
                tab.RunAccentBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                tab.RunSigilHost.Visibility = Visibility.Collapsed;
                tab.RunSigilGlow.Visibility = Visibility.Collapsed;
                tab.RunAccentBar.Visibility = Visibility.Visible;
            }

            tab.TxtRunProgramTitle.Text = program.Title;
            tab.TxtRunChapterName.Text = chapter?.Name ?? program.Subtitle;
            tab.TxtRunChapterName.Foreground = accent;
            tab.TxtRunDayCounter.Text = Loc.GetF("programs_day_counter", enrollment.CurrentDay, program.LengthDays);

            tab.RunStrictBadge.Visibility = enrollment.StrictMode ? Visibility.Visible : Visibility.Collapsed;

            if (enrollment.AttemptNumber > 1)
            {
                tab.TxtRunAttempt.Text = Loc.GetF("programs_attempt", enrollment.AttemptNumber);
                tab.RunAttemptBadge.Visibility = Visibility.Visible;
            }
            else
            {
                tab.RunAttemptBadge.Visibility = Visibility.Collapsed;
            }

            // Season stats. Plain numbers on purpose - the captions are localised, the values are
            // digits, so nothing here goes stale on a language change.
            tab.TxtRunStatDone.Text = $"{enrollment.CompletedDayCount} / {program.LengthDays}";
            tab.TxtRunStatPerfect.Text = enrollment.PerfectDayCount.ToString();
            tab.TxtRunStatDaysOff.Text = enrollment.DaysOffRemaining.ToString();

            // Chapter reward chip at the end of the track - the season's carrot. Authored text,
            // trimmed with an ellipsis in XAML, full text on the tooltip.
            var chapterReward = chapter?.RewardDescription;
            if (!string.IsNullOrWhiteSpace(chapterReward))
            {
                tab.TxtRunChapterReward.Text = chapterReward;
                tab.RunChapterRewardChip.ToolTip = chapterReward;
                tab.RunChapterRewardChip.Visibility = Visibility.Visible;
            }
            else
            {
                tab.RunChapterRewardChip.Visibility = Visibility.Collapsed;
            }

            var paused = enrollment.State == ProgramEnrollmentState.Paused;
            tab.RunPausedNote.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            tab.BtnProgramPauseResume.Content = paused ? Loc.Get("btn_program_resume") : Loc.Get("btn_program_pause");

            BuildProgramDayStrip(program, enrollment, accent);
            BuildProgramTodayPanel(program, enrollment, accent);
        }

        private void BuildProgramDayStrip(ProgramDefinition program, ProgramEnrollment enrollment, Brush accent)
        {
            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var lightBrush = ProgramThemeBrush("TextLightBrush", Brushes.White);
            var surfaceBrush = ProgramThemeBrush("SurfaceBgBrush", Brushes.Transparent);
            var borderBrush = ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);
            var dangerBrush = ProgramThemeBrush("DangerBrush", Brushes.IndianRed);

            var pips = new List<ProgramDayPip>(Math.Max(0, program.LengthDays));

            // One snapshot of the day objects: GetDay walks AllDays per call, and milestone
            // dressing needs a lookup per node.
            var daysByIndex = program.AllDays.ToDictionary(d => d.DayIndex);
            var glowBrush = ProgramRadialGlowBrush(accent, 170);

            for (int i = 1; i <= program.LengthDays; i++)
            {
                var record = enrollment.GetRecord(i);
                var day = daysByIndex.GetValueOrDefault(i);
                var pip = new ProgramDayPip { DayIndex = i, Label = i.ToString() };

                // Milestone days (boss / an authored reward) read bigger and carry a glyph under
                // the node - the reward-track treatment that makes the rail worth looking at.
                var isMilestone = day != null && (day.IsBoss || !string.IsNullOrWhiteSpace(day.RewardDescription));
                if (isMilestone)
                {
                    pip.RewardGlyph = day!.IsBoss ? "👑" : "🎁";
                    pip.RewardVisibility = Visibility.Visible;
                    pip.RewardTip = !string.IsNullOrWhiteSpace(day.RewardDescription)
                        ? day.RewardDescription!
                        : Loc.Get("programs_boss_badge");
                }

                string tip;
                if (i == enrollment.CurrentDay)
                {
                    pip.Fill = Brushes.Transparent;
                    pip.Stroke = accent;
                    pip.PipBorderThickness = new Thickness(2.5);
                    pip.LabelBrush = accent;
                    pip.LabelWeight = FontWeights.Bold;
                    pip.NodeSize = 42;
                    pip.LabelSize = 15;
                    pip.IsCurrent = true;
                    pip.GlowBrush = glowBrush;
                    pip.GlowVisibility = Visibility.Visible;
                    tip = Loc.GetF("programs_pip_today", i);
                }
                else if (record?.DayCompleted == true)
                {
                    pip.Fill = accent;
                    pip.Stroke = accent;
                    pip.Label = "✓";
                    pip.LabelBrush = lightBrush;
                    pip.LabelWeight = FontWeights.Bold;
                    pip.NodeSize = 32;
                    pip.LabelSize = 13;
                    tip = Loc.GetF("programs_pip_done", i);
                }
                else if (record?.Missed == true)
                {
                    pip.Fill = Brushes.Transparent;
                    pip.Stroke = dangerBrush;
                    pip.Label = "✕";
                    pip.LabelBrush = dangerBrush;
                    pip.NodeSize = 32;
                    tip = Loc.GetF("programs_pip_missed", i);
                }
                else
                {
                    pip.Fill = surfaceBrush;
                    pip.Stroke = borderBrush;
                    pip.LabelBrush = mutedBrush;
                    pip.PipOpacity = 0.65;
                    tip = Loc.GetF("programs_pip_locked", i);
                }

                if (isMilestone) pip.NodeSize += 6;

                // The day's title on every tooltip: hovering the track reads as a table of
                // contents, which is most of what a reward track is for.
                pip.Tip = day != null && !string.IsNullOrWhiteSpace(day.Title) ? $"{tip} · {day.Title}" : tip;

                pips.Add(pip);
            }

            var tab = ProgramsTab;
            tab.ProgramDayStrip.ItemsSource = pips;

            // A short program stays a rail of a sensible length; only a long one earns the full
            // width of the panel. Stretching seven nodes across 1700px is the huddle problem in
            // reverse. This drives the capped star COLUMN, not the rail: setting MaxWidth on a
            // left-aligned rail does nothing, because the rail then sizes to its own content.
            if (program.LengthDays <= 14)
            {
                tab.RailWidthColumn.MaxWidth = 840;
                tab.RailSpacerColumn.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                tab.RailWidthColumn.MaxWidth = double.PositiveInfinity;
                tab.RailSpacerColumn.Width = new GridLength(0);
            }

            // The UniformGrid centres node i in cell i, so node i's centre sits at (i - 0.5)/N of
            // the rail. Sizing the done segment in those same star units lands the track's fill
            // exactly under today's node at any program length.
            var totalDays = Math.Max(1, program.LengthDays);
            var doneUnits = Math.Clamp(enrollment.CurrentDay, 0, totalDays) - 0.5;
            if (doneUnits < 0) doneUnits = 0;

            tab.RailDoneColumn.Width = new GridLength(doneUnits, GridUnitType.Star);
            tab.RailRestColumn.Width = new GridLength(Math.Max(0.0001, totalDays - doneUnits), GridUnitType.Star);
            tab.RailProgressFill.Background = ProgramRailFillBrush(accent);
        }

        private void BuildProgramTodayPanel(ProgramDefinition program, ProgramEnrollment enrollment, Brush accent)
        {
            var tab = ProgramsTab;
            var svc = App.Programs;
            var day = svc?.Today;
            var record = svc?.TodayRecord;

            if (day == null || record == null)
            {
                tab.TodayPanel.Visibility = Visibility.Collapsed;
                return;
            }

            tab.TodayPanel.Visibility = Visibility.Visible;

            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var lightBrush = ProgramThemeBrush("TextLightBrush", Brushes.White);
            var paused = enrollment.State == ProgramEnrollmentState.Paused;

            // Boss days read differently: the accent border makes the whole panel escalate.
            tab.TodayPanel.BorderBrush = day.IsBoss ? accent : ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);
            tab.TodayPanel.BorderThickness = new Thickness(day.IsBoss ? 2 : 1);
            tab.TodayBossBadge.Visibility = day.IsBoss ? Visibility.Visible : Visibility.Collapsed;
            tab.TodayReturnBadge.Visibility = record.IsReturnDay ? Visibility.Visible : Visibility.Collapsed;

            // Hero band. The wide art is keyed on the day's session-template archetype, so four
            // heroes dress every program length; it is cropped by UniformToFill and faded into the
            // panel by the host's gradient OpacityMask. Missing art zeroes the column - a star
            // column would keep reserving empty space - and the copy takes the whole band.
            var hero = Services.Program.ProgramArt.DayHero(program, day);
            if (hero != null)
            {
                ApplyProgramArtMask(tab.RunHeroPlate, tab.RunHeroMask, hero, Stretch.UniformToFill);
                tab.RunHeroPlate.Fill = accent;
                tab.RunHeroHost.Visibility = Visibility.Visible;
                tab.HeroArtColumn.Width = new GridLength(2, GridUnitType.Star);
            }
            else
            {
                tab.RunHeroHost.Visibility = Visibility.Collapsed;
                tab.HeroArtColumn.Width = new GridLength(0);
            }

            // Ambient accent wash behind the whole band - present with or without art, so the
            // panel never reverts to a flat sheet of surface colour.
            tab.TodayHeroGlow.Fill = ProgramRadialGlowBrush(accent, 70, 0.78, 0.2, 0.9);

            tab.TxtTodayTitle.Text = day.Title;
            tab.TxtTodayBlurb.Text = day.Blurb;

            // What today will actually do. The blurb is authored spoiler-free about the feeling;
            // this is the honest mechanical answer, and it is what fills the band between the copy
            // and the session row on a day whose blurb is two lines long.
            BuildProgramTodayLayers(program, day, accent);

            // Today's reward chip (boss days mostly - authored per day).
            if (!string.IsNullOrWhiteSpace(day.RewardDescription))
            {
                tab.TxtTodayReward.Text = day.RewardDescription;
                tab.TodayRewardChip.ToolTip = day.RewardDescription;
                tab.TodayRewardChip.Visibility = Visibility.Visible;
            }
            else
            {
                tab.TodayRewardChip.Visibility = Visibility.Collapsed;
            }

            tab.TodayCompleteBanner.Visibility = record.DayCompleted ? Visibility.Visible : Visibility.Collapsed;

            // Pop the banner once per day, the moment the day flips to done. The field remembers
            // the popped day even when the tab is hidden, so backgrounded completions don't replay
            // the pop on the next visit - they just show the banner already settled.
            if (record.DayCompleted && _programDayCompletePopped != enrollment.CurrentDay)
            {
                _programDayCompletePopped = enrollment.CurrentDay;
                if (tab.IsVisible)
                {
                    PopProgramScale(tab.TodayCompleteBannerScale);
                    // Event FX (PR-5): the burst rides the same once-per-day guard as the pop, so
                    // a day that completed in the background never replays it later.
                    CelebrateProgramDayComplete(tab.TodayCompleteBanner);
                }
            }

            // --- Session slot ---
            // Glyph / button / progress strip all live in UpdateProgramSessionRow so the
            // full repaint and the once-a-second live tick can never disagree about them.
            tab.TxtTodaySessionMinutes.Text = Loc.GetF("programs_session_minutes", day.SessionMinutes);
            UpdateProgramSessionRow();

            // --- Ambient layer ---
            var ambient = day.Ambient;
            if (ambient != null && (!string.IsNullOrWhiteSpace(ambient.Description) || ambient.RequiredMinutes > 0))
            {
                tab.TodayAmbientRow.Visibility = Visibility.Visible;
                tab.TxtTodayAmbient.Text = ambient.Description;
                if (ambient.RequiredMinutes > 0)
                {
                    tab.TxtTodayAmbientProgress.Text = Loc.GetF("programs_ambient_progress",
                        Math.Min(record.AmbientMinutes, ambient.RequiredMinutes), ambient.RequiredMinutes);
                    tab.TxtTodayAmbientProgress.Visibility = Visibility.Visible;
                }
                else
                {
                    tab.TxtTodayAmbientProgress.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                tab.TodayAmbientRow.Visibility = Visibility.Collapsed;
            }

            // --- Tasks ---
            var items = new List<ProgramTaskItem>();
            var hasRitual = false;
            var completedTasks = 0;
            var glassBrush = ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);

            foreach (var task in day.Tasks)
            {
                var complete = svc != null && svc.IsTaskComplete(record, task);
                if (complete) completedTasks++;
                var blocked = svc != null && svc.IsTaskBlocked(task);
                var isRitual = task.Kind == ProgramTaskKind.Ritual;
                if (isRitual) hasRitual = true;

                var item = new ProgramTaskItem
                {
                    TaskId = task.Id,
                    Description = task.Description,
                    StatusGlyph = complete ? "✓" : "○",
                    StatusBrush = complete ? accent : mutedBrush,
                    TextBrush = complete ? mutedBrush : lightBrush,
                    RowOpacity = blocked ? 0.5 : 1.0,
                    AccentBrush = accent,
                    CardBorderBrush = complete ? accent : glassBrush,
                    DoneChipVisibility = complete ? Visibility.Visible : Visibility.Collapsed
                };

                // One-shot completion pop: JustCompleted is set only when this exact task was seen
                // incomplete on a previous build of this same day. The very first paint of an
                // already-done day therefore renders settled, not celebrating.
                var seenKey = $"{enrollment.CurrentDay}:{task.Id}";
                if (complete)
                {
                    if (_programTasksSeenIncomplete.Remove(seenKey)) item.JustCompleted = true;
                }
                else
                {
                    _programTasksSeenIncomplete.Add(seenKey);
                }

                // Resolved here, not in a binding: the carriers hold finished values only.
                var iconPath = ProgramTaskIconPath(task);
                if (iconPath != null)
                {
                    var icon = Services.ModResourceResolver.ResolveImage(iconPath);
                    if (icon != null)
                    {
                        item.Icon = icon;
                        item.IconVisibility = Visibility.Visible;
                        item.GlyphVisibility = Visibility.Collapsed;
                    }
                }

                // Counted tasks get the mini bar, full and accent even when done ("20 / 20" reads
                // as a claimed quest, an empty slot reads as a bug).
                if (task.Kind == ProgramTaskKind.AutoVerified && task.TargetValue > 1)
                {
                    record.TaskProgress.TryGetValue(task.Id, out var current);
                    var shown = complete ? task.TargetValue : Math.Min(current, task.TargetValue);
                    item.ProgressText = Loc.GetF("programs_task_progress", shown, task.TargetValue);
                    item.ProgressStar = new GridLength(Math.Max(0, shown), GridUnitType.Star);
                    item.RemainderStar = new GridLength(Math.Max(0.0001, task.TargetValue - shown), GridUnitType.Star);
                    item.BarVisibility = Visibility.Visible;
                }

                if (blocked)
                {
                    item.BadgeText = Loc.Get("programs_task_locked");
                    item.BadgeVisibility = Visibility.Visible;
                }
                else if (task.Optional)
                {
                    item.BadgeText = Loc.Get("programs_task_optional");
                    item.BadgeVisibility = Visibility.Visible;
                }

                item.SubmitVisibility = isRitual && !complete && !blocked && !paused
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                items.Add(item);
            }

            tab.TodayTaskList.ItemsSource = items;
            tab.TxtTodayNoTasks.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.TxtRitualPrivacyNote.Visibility = hasRitual ? Visibility.Visible : Visibility.Collapsed;

            // Cap the card column at the width the cards genuinely occupy - 360 card + 10 margin,
            // three per row - so one task stops reserving a full-width band and the arc column beside
            // it gets the rest. Zero tasks still gets one column's worth, which is what the "no tasks
            // today" line wants. The cap is on a STAR column, so a narrow window shrinks it rather
            // than overflowing the grid (see the note in ProgramsTabView.xaml).
            tab.TaskListColumn.MaxWidth = Math.Clamp(items.Count, 1, 3) * 370;

            if (items.Count > 0)
            {
                tab.TxtTodayTasksDone.Text = Loc.GetF("programs_tasks_done_count", completedTasks, items.Count);
                tab.TodayTasksDonePill.Visibility = Visibility.Visible;
            }
            else
            {
                tab.TodayTasksDonePill.Visibility = Visibility.Collapsed;
            }

            BuildProgramUpNext(program, enrollment, day, record, accent);
        }

        /// <summary>
        /// The layer chips under the day blurb: one per feature today's session turns on, with the
        /// ones that were NOT in yesterday's session marked NEW.
        ///
        /// This is the only place in the app that says what a program day will actually do. It earns
        /// the space three ways: it gives the template its identity back (the authored name and
        /// description are otherwise never shown), it makes the escalation curve legible a day at a
        /// time, and on a feature that ramps flash rate, opacity and spiral on a schedule it is the
        /// honest "here is what you are about to sit through" the plan asks for (§10.4).
        /// </summary>
        private void BuildProgramTodayLayers(ProgramDefinition program, ProgramDay day, Brush accent)
        {
            var tab = ProgramsTab;

            var template = program.GetTemplate(day.SessionTemplateId);
            var settings = ProgramDaySettings(program, day);

            if (template == null || settings == null)
            {
                tab.TodayLayersPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Yesterday's layer set, so today's additions can be marked. Day 1 has no previous day
            // and therefore gets no NEW markers - flagging every layer on day one is noise.
            var previous = day.DayIndex > 1
                ? ProgramDaySettings(program, program.GetDay(day.DayIndex - 1))
                : null;

            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
            var lightBrush = ProgramThemeBrush("TextLightBrush", Brushes.White);
            var glassBrush = ProgramThemeBrush("GlassBorderBrush", Brushes.Gray);
            var newTip = Loc.Get("programs_layer_new_tip");

            var chips = new List<ProgramLayerChip>();

            foreach (var (isOn, labelKey, iconPath) in ProgramLayerCatalog)
            {
                if (!isOn(settings)) continue;

                var label = Loc.Get(labelKey);
                var isNew = previous != null && !isOn(previous);

                var chip = new ProgramLayerChip
                {
                    Label = label,
                    AccentBrush = accent,
                    LabelBrush = isNew ? lightBrush : mutedBrush,
                    BorderBrush = isNew ? accent : glassBrush,
                    NewVisibility = isNew ? Visibility.Visible : Visibility.Collapsed,
                    Tip = isNew ? $"{label} - {newTip}" : label
                };

                // Resolved here, not in a binding: the carriers hold finished values only. A missing
                // PNG collapses the image and leaves a plain labelled pill, never an empty box.
                var icon = Services.ModResourceResolver.ResolveImage(iconPath);
                if (icon != null)
                {
                    chip.Icon = icon;
                    chip.IconVisibility = Visibility.Visible;
                }

                chips.Add(chip);
            }

            tab.TxtTodayTemplateName.Text = template.Name;
            tab.TxtTodayTemplateName.Foreground = accent;
            tab.TxtTodayTemplateBlurb.Text = template.Description;
            tab.TxtTodayTemplateBlurb.Visibility = string.IsNullOrWhiteSpace(template.Description)
                ? Visibility.Collapsed
                : Visibility.Visible;

            tab.TodayLayerList.ItemsSource = chips;
            tab.TodayLayersPanel.Visibility = chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// The column beside the checklist: the next three days, the hour today closes at, and the
        /// streak.
        ///
        /// All three are things the run view could not say before. The upcoming days are the
        /// pre-commitment beat the design leans on (§4.1) and no new spoiler - the enrollment
        /// ceremony already shows the whole arc and the reward track already puts every day title on
        /// a tooltip. The boundary hour is the one mechanic that silently costs someone a day and it
        /// appeared nowhere in the UI. The streak is consistency, which is what the program is
        /// actually asking for and is not the same number as the header's DAYS DONE total.
        /// </summary>
        private void BuildProgramUpNext(ProgramDefinition program, ProgramEnrollment enrollment,
                                        ProgramDay day, ProgramDayRecord record, Brush accent)
        {
            var tab = ProgramsTab;
            var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);

            var upcoming = program.AllDays.Where(d => d.DayIndex > day.DayIndex).Take(3).ToList();
            var items = new List<ProgramUpNextItem>(upcoming.Count);

            for (int i = 0; i < upcoming.Count; i++)
            {
                var next = upcoming[i];

                var parts = new List<string> { Loc.GetF("programs_session_minutes", next.SessionMinutes) };

                // The first task says what the day is about; the rest are a count, so a day carrying
                // three tasks cannot grow this line past the column.
                var firstTask = next.Tasks.FirstOrDefault();
                if (firstTask != null && !string.IsNullOrWhiteSpace(firstTask.Description))
                {
                    parts.Add(firstTask.Description);
                    if (next.Tasks.Count > 1)
                        parts.Add(Loc.GetF("programs_next_more_tasks", next.Tasks.Count - 1));
                }

                items.Add(new ProgramUpNextItem
                {
                    DayLabel = Loc.GetF("programs_card_day", next.DayIndex),
                    Title = next.Title,
                    Meta = string.Join("  ·  ", parts),
                    DayBrush = next.IsBoss ? accent : mutedBrush,
                    Glyph = next.IsBoss ? "👑" : "",
                    GlyphTip = next.IsBoss ? Loc.Get("programs_boss_badge") : "",
                    GlyphVisibility = next.IsBoss ? Visibility.Visible : Visibility.Collapsed,

                    // Further-out days sit back, so the list reads as a horizon rather than a menu.
                    RowOpacity = 1.0 - (i * 0.18)
                });
            }

            tab.TodayUpNextList.ItemsSource = items;
            tab.TxtTodayUpNextFinal.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Rollover. With the default 04:00 boundary a 2am session still counts for the day before,
            // which is exactly the kind of rule people only discover by losing a day to it.
            var boundaryText = DateTime.Today
                .AddHours(Math.Clamp(enrollment.DayBoundaryHour, 0, 23))
                .ToShortTimeString();

            var closesVisible = true;
            var noteVisible = false;

            if (enrollment.State == ProgramEnrollmentState.Paused)
            {
                tab.TxtTodayCloses.Text = Loc.Get("programs_closes_paused");
            }
            else if (record.DayCompleted && items.Count > 0)
            {
                tab.TxtTodayCloses.Text = Loc.GetF("programs_closes_done", upcoming[0].DayIndex, boundaryText);
            }
            else if (record.DayCompleted)
            {
                // Last day, already done - the graduation panel is what comes next, not a deadline.
                closesVisible = false;
            }
            else
            {
                tab.TxtTodayCloses.Text = Loc.GetF("programs_closes_at", boundaryText);
                tab.TxtTodayClosesNote.Text = Loc.Get("programs_closes_note");
                noteVisible = true;
            }

            tab.TxtTodayCloses.Visibility = closesVisible ? Visibility.Visible : Visibility.Collapsed;
            tab.TxtTodayClosesNote.Visibility = noteVisible ? Visibility.Visible : Visibility.Collapsed;

            // Consecutive completed days ending at today (or at yesterday while today is still open).
            var streak = 0;
            for (int i = record.DayCompleted ? enrollment.CurrentDay : enrollment.CurrentDay - 1; i >= 1; i--)
            {
                if (enrollment.GetRecord(i)?.DayCompleted != true) break;
                streak++;
            }

            tab.TxtTodayStreak.Text = streak switch
            {
                0 => Loc.Get("programs_streak_none"),
                1 => Loc.Get("programs_streak_one"),
                _ => Loc.GetF("programs_streak_many", streak)
            };
        }

        // -----------------------------------------------------------------------------------
        // Live session row
        // -----------------------------------------------------------------------------------

        /// <summary>mm:ss, or h:mm:ss past the hour. Never negative.</summary>
        private static string FormatProgramClock(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
                : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
        }

        /// <summary>
        /// Repaints the Session row's live state: the glyph, the button and the progress strip.
        ///
        /// Called once per engine tick from OnSessionProgressUpdated (MainWindow.Presets.cs), so it
        /// must stay cheap - no list rebuilds, no disk, no service walks. It does NOT spin a timer:
        /// SessionEngine's own 1-second DispatcherTimer already drives ProgressUpdated, and all three
        /// engine construction sites (Presets, RemoteControl, ProgramsTab) subscribe the same
        /// MainWindow handlers, so there is nothing extra to subscribe and nothing to unsubscribe.
        ///
        /// A session this tab did NOT start (Dashboard, preset, remote) is deliberately not claimed:
        /// no progress bar, no completion, just a disabled button saying something else is running.
        /// ProgramService.IsProgramSession is the discriminator, the same one that decides whether a
        /// completed session may tick the day.
        /// </summary>
        internal void UpdateProgramSessionRow()
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                // No IsLoaded gate on purpose: the first full repaint can run before the window
                // finishes loading, and gating here would leave the row showing the XAML default
                // ("Start today's session") over a session that is already in flight. Only child
                // control properties are touched, so this is safe pre-load; teardown is covered
                // by the dispatcher check above.
                var tab = ProgramsTab;
                if (tab?.TodaySessionProgressRow == null) return;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var record = svc?.TodayRecord;

                if (svc == null || enrollment == null || record == null)
                {
                    tab.TodaySessionProgressRow.Visibility = Visibility.Collapsed;
                    tab.TxtTodaySessionProgress.Visibility = Visibility.Collapsed;
                    StopProgramSessionSheen();
                    return;
                }

                var mutedBrush = ProgramThemeBrush("TextMutedBrush", Brushes.Gray);
                var accent = ProgramAccentBrush(svc.ActiveProgram?.AccentColor);
                var paused = enrollment.State == ProgramEnrollmentState.Paused;

                var engine = _sessionEngine;
                var running = engine?.IsRunning == true;
                var current = running ? engine!.CurrentSession : null;
                var isOurs = running && svc.IsProgramSession(current);

                if (isOurs && current != null)
                {
                    var total = TimeSpan.FromMinutes(Math.Max(1, current.DurationMinutes));
                    var elapsed = engine!.ElapsedTime;
                    if (elapsed > total) elapsed = total;

                    tab.TodaySessionProgressBar.Foreground = accent;
                    tab.TodaySessionProgressBar.Value = Math.Clamp(engine.ProgressPercent, 0, 100);

                    var clock = Loc.GetF("programs_session_progress",
                        FormatProgramClock(elapsed), FormatProgramClock(total));
                    tab.TxtTodaySessionProgress.Text = engine.IsPaused
                        ? Loc.GetF("programs_session_progress_paused", clock)
                        : clock;
                    tab.TxtTodaySessionProgress.Foreground = accent;
                    tab.TxtTodaySessionProgress.Visibility = Visibility.Visible;

                    tab.TodaySessionProgressRow.Visibility = Visibility.Visible;

                    // The sheen sweep runs only while the bar is actually on screen. Tick-driven
                    // on purpose: on the first visible tick the bar may not be measured yet, and
                    // the next 1s tick simply retries.
                    if (tab.IsVisible && !engine.IsPaused) EnsureProgramSessionSheen(tab);
                    else StopProgramSessionSheen();

                    tab.TxtTodaySessionGlyph.Text = "◉";
                    tab.TxtTodaySessionGlyph.Foreground = accent;

                    // Deliberately NOT a second stop button: the bottom bar's STOP is on every
                    // screen and owns ending a session. A stop here would sit exactly where
                    // "Start today's session" was a second ago - a misclick would abandon the day.
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_in_progress");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    tab.BtnStartTodaySession.ToolTip = Loc.Get("programs_session_stop_hint");
                    return;
                }

                // Nothing of ours in flight - hide the strip and fall back to the slot states.
                tab.TodaySessionProgressRow.Visibility = Visibility.Collapsed;
                tab.TxtTodaySessionProgress.Visibility = Visibility.Collapsed;
                tab.TodaySessionProgressBar.Value = 0;
                tab.BtnStartTodaySession.ToolTip = null;
                StopProgramSessionSheen();

                if (record.SessionCompleted)
                {
                    tab.TxtTodaySessionGlyph.Text = "✓";
                    tab.TxtTodaySessionGlyph.Foreground = accent;
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_done");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    return;
                }

                tab.TxtTodaySessionGlyph.Text = "○";
                tab.TxtTodaySessionGlyph.Foreground = mutedBrush;

                if (running)
                {
                    // Someone else's session. Say so plainly, and refuse to start on top of it -
                    // StartProgramSession would otherwise kill it without asking.
                    tab.BtnStartTodaySession.Content = Loc.Get("programs_session_other_running");
                    tab.BtnStartTodaySession.IsEnabled = false;
                    tab.BtnStartTodaySession.ToolTip = Loc.Get("programs_session_other_running_hint");
                    return;
                }

                tab.BtnStartTodaySession.Content = Loc.Get("btn_program_start_session");
                tab.BtnStartTodaySession.IsEnabled = !paused;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "UpdateProgramSessionRow failed");
            }
        }

        // -----------------------------------------------------------------------------------
        // Run-view FX
        //
        // Everything here is opacity / translate / scale on plain named elements - no bitmap
        // effects, no per-frame CPU raster. The only continuous loop owned by this code is the
        // session-bar sheen, and it is explicitly stopped whenever the tab is hidden or nothing
        // of ours is running (the breathing today-node glow lives in a DataTemplate storyboard
        // and dies with its item container on every rebuild). All of it is decoration: every
        // method may fail without consequence, so everything is wrapped and logged at Warning.
        // -----------------------------------------------------------------------------------

        /// <summary>One-shot guard for the tab visibility hook (the tab outlives every refresh).</summary>
        private bool _programsFxHooked;

        /// <summary>True while the session-bar sheen loop is running.</summary>
        private bool _programSheenActive;

        /// <summary>Day index whose day-complete banner pop has already played (or been skipped).</summary>
        private int _programDayCompletePopped = -1;

        /// <summary>
        /// "{dayIndex}:{taskId}" for every task that has been BUILT incomplete. A task found
        /// complete while its key is in here has just flipped, which is the only moment the task
        /// card's pop storyboard is allowed to fire.
        /// </summary>
        private readonly HashSet<string> _programTasksSeenIncomplete = new();

        private void EnsureProgramsFxHooked(ProgramsTabView tab)
        {
            if (_programsFxHooked) return;
            tab.IsVisibleChanged += OnProgramsTabVisibleChanged;
            _programsFxHooked = true;
        }

        /// <summary>
        /// The FX lifecycle rides the tab's own visibility (ShowTab collapses every other tab), so
        /// no TabNavigation wiring is needed: hidden stops the sheen loop, visible replays the
        /// entrance and re-arms the sheen via the session row repaint.
        /// </summary>
        private void OnProgramsTabVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                var tab = ProgramsTab;
                if (tab == null) return;

                if (!tab.IsVisible)
                {
                    StopProgramSessionSheen();
                    return;
                }

                if (tab.ProgramsRunPanel.Visibility == Visibility.Visible) StartProgramRunEntrance(tab);

                // Re-evaluate the sheen after layout has produced a real bar width - straight away
                // the host still measures 0 and the start would be skipped.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        new Action(UpdateProgramSessionRow));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Programs tab FX visibility hook failed");
            }
        }

        /// <summary>
        /// Entrance for the run view: header and hero band fade/slide in with a slight stagger and
        /// the rail's done-segment grows into today's node. Plays on tab entry and on the run
        /// view's first reveal - never on the once-a-second refreshes (see RefreshProgramsUI).
        /// </summary>
        private void StartProgramRunEntrance(ProgramsTabView tab)
        {
            try
            {
                AnimateProgramPanelIn(tab.RunHeaderPanel, 0);
                AnimateProgramPanelIn(tab.TodayPanel, 90);

                var grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550))
                {
                    BeginTime = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                tab.RailFillScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program run entrance failed");
            }
        }

        /// <summary>Fade + 14px slide-up on a panel. On any failure the panel is left fully shown.</summary>
        private static void AnimateProgramPanelIn(FrameworkElement panel, int delayMs)
        {
            try
            {
                var slideTransform = new System.Windows.Media.TranslateTransform(0, 14);
                panel.RenderTransform = slideTransform;

                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                panel.BeginAnimation(UIElement.OpacityProperty, fade);

                var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(340))
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                slideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
            }
            catch
            {
                // Decoration only - make sure the panel is visible and move on.
                try { panel.Opacity = 1; panel.RenderTransform = null; } catch { }
            }
        }

        /// <summary>
        /// Starts the sheen sweeping the live session bar. Skipped (and retried by the next engine
        /// tick) until the bar has a real width, because the sweep distance is that width.
        /// </summary>
        private void EnsureProgramSessionSheen(ProgramsTabView tab)
        {
            if (_programSheenActive) return;

            try
            {
                if (!tab.SessionBarHost.IsLoaded) return;
                var width = tab.SessionBarHost.ActualWidth;
                if (width < 80) return;

                var sweep = new DoubleAnimation(-160, width + 60, TimeSpan.FromSeconds(2.8))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                tab.SessionSheen.Opacity = 1; // the gradient itself is translucent
                tab.SessionSheenSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
                _programSheenActive = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program session sheen start failed");
            }
        }

        private void StopProgramSessionSheen()
        {
            if (!_programSheenActive) return;
            _programSheenActive = false;

            try
            {
                var tab = ProgramsTab;
                if (tab == null) return;
                tab.SessionSheenSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                tab.SessionSheen.Opacity = 0;
            }
            catch { /* stopping decoration must never throw */ }
        }

        /// <summary>Small settle-in pop on a scale transform (day-complete banner).</summary>
        private static void PopProgramScale(ScaleTransform scale)
        {
            try
            {
                var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            catch { /* decoration */ }
        }

        // -----------------------------------------------------------------------------------
        // Session start/end announcements
        //
        // Both fire from the shared MainWindow session handlers (OnSessionStarted /
        // OnSessionStopped in MainWindow.Presets.cs), which ALL THREE engine construction sites
        // subscribe - Presets.cs, RemoteControl.cs and ProgramsTab.cs. Hooking there rather than
        // at a construction site is what stops this silently working in one path only.
        //
        // Surface: App.Notifications (Services/Notifications/NotificationService.cs), the app's
        // standard non-modal toast host in MainWindow's top-right. No new window, no new service.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Announces the start of a session THIS program launched. A foreign session is silent -
        /// the Programs feature has nothing to say about it.
        /// </summary>
        internal void AnnounceProgramSessionStarted()
        {
            try
            {
                var svc = App.Programs;
                var program = svc?.ActiveProgram;
                var enrollment = svc?.ActiveEnrollment;
                if (svc == null || program == null || enrollment == null) return;
                if (!svc.IsProgramSession(_sessionEngine?.CurrentSession)) return;

                App.Notifications?.Show(
                    Loc.GetF("programs_toast_session_started", program.Title, enrollment.CurrentDay),
                    Services.NotificationType.Info,
                    TimeSpan.FromSeconds(6));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program session start toast failed");
            }
        }

        /// <summary>
        /// Announces the end of a session this program launched.
        ///
        /// SessionEngine raises SessionStopped BEFORE SessionCompleted (StopSession fires the
        /// former, then does the XP/achievement work, then the latter), and it is
        /// SessionCompleted that lets ProgramService tick today's slot. So the caller passes the
        /// program-session verdict captured at SessionStopped time and this runs deferred at
        /// Background priority - by then StopSession has fully unwound and TodayRecord tells us
        /// whether the session finished or was cut short.
        /// </summary>
        internal void AnnounceProgramSessionEnded(bool wasProgramSession)
        {
            if (!wasProgramSession) return;

            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        if (Application.Current.Dispatcher.HasShutdownStarted) return;

                        var svc = App.Programs;
                        var program = svc?.ActiveProgram;
                        if (svc == null || program == null) return;

                        var completed = svc.TodayRecord?.SessionCompleted == true;

                        App.Notifications?.Show(
                            completed
                                ? Loc.GetF("programs_toast_session_completed", program.Title)
                                : Loc.GetF("programs_toast_session_ended_early", program.Title),
                            completed ? Services.NotificationType.Success : Services.NotificationType.Warning,
                            TimeSpan.FromSeconds(8),
                            Loc.Get("programs_toast_view_today"),
                            () => ShowTab("programs"));

                        // The slot may have flipped to done during that unwind.
                        UpdateProgramSessionRow();
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Program session end toast failed");
                    }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program session end toast could not be scheduled");
            }
        }

        // -----------------------------------------------------------------------------------
        // Lapsed / Graduated
        // -----------------------------------------------------------------------------------

        private void BuildProgramLapsedPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            ProgramsTab.TxtLapsedBody.Text = Loc.GetF("programs_lapsed_body",
                program.Title, enrollment.AttemptNumber + 1);
        }

        private void BuildProgramGraduatedPanel(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var tab = ProgramsTab;
            tab.TxtGraduatedSub.Text = Loc.GetF("programs_graduated_sub", program.Title);
            tab.TxtGraduatedStats.Text = Loc.GetF("programs_graduated_stats",
                enrollment.AttemptNumber, enrollment.PerfectDayCount, program.LengthDays);
        }

        // -----------------------------------------------------------------------------------
        // Handlers
        // -----------------------------------------------------------------------------------

        internal void BtnProgramEnroll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn) return;
                var programId = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(programId)) return;

                var svc = App.Programs;
                var def = svc?.Library?.FirstOrDefault(p =>
                    string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));
                if (svc == null || def == null) return;

                // Premium the user can't take: the ✨ card is an ad, not an entry point.
                if (def.Tier == ProgramTier.Premium && !ProgramHasPremium)
                {
                    ShowAppInfoPopup();
                    return;
                }

                if (!svc.CanEnroll(def, out var reason))
                {
                    App.Logger?.Information("Program enrollment blocked for {Program}: {Reason}", def.Id, reason);
                    ShowStyledDialog(Loc.Get("programs_unavailable_title"), Loc.Get("programs_unavailable"),
                        Loc.Get("btn_ok"), "");
                    return;
                }

                var dialog = new ProgramEnrollDialog(def) { Owner = this };
                if (dialog.ShowDialog() != true) return;

                svc.Enroll(def, dialog.StrictMode, dialog.ShareLevel, dialog.DayBoundaryHour, dialog.NudgeHour);
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Program enrollment failed");
            }
        }

        internal void BtnProgramPauseResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var svc = App.Programs;
                if (svc?.ActiveEnrollment == null) return;

                if (svc.ActiveEnrollment.State == ProgramEnrollmentState.Paused) svc.Resume();
                else svc.Pause();

                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program pause/resume failed");
            }
        }

        /// <summary>
        /// Always available, on every screen. The confirm exists so a misclick doesn't end a run -
        /// it does not argue, guilt or bargain.
        /// </summary>
        internal void BtnProgramWithdraw_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Programs?.ActiveEnrollment == null) return;

                var confirmed = ShowStyledDialog(
                    Loc.Get("programs_withdraw_confirm_title"),
                    Loc.Get("programs_withdraw_confirm_body"),
                    Loc.Get("btn_program_withdraw_confirm"),
                    Loc.Get("btn_program_withdraw_keep"));

                if (!confirmed) return;

                App.Programs?.Withdraw();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program withdraw failed");
            }
        }

        internal void BtnProgramRestart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Programs?.RestartAfterLapse();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program restart failed");
            }
        }

        internal void BtnProgramDismissGraduated_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Programs?.DismissGraduated();
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program graduation dismiss failed");
            }
        }

        /// <summary>Ritual task: pick a photo, hand it to the service. The file never leaves this machine.</summary>
        internal void BtnProgramSubmitRitual_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn) return;
                var taskId = btn.Tag as string;
                if (string.IsNullOrWhiteSpace(taskId)) return;

                var picker = new Microsoft.Win32.OpenFileDialog
                {
                    Title = Loc.Get("programs_photo_dialog_title"),
                    Filter = Loc.Get("programs_photo_filter"),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (picker.ShowDialog(this) != true) return;

                App.Programs?.SubmitRitualTask(taskId!, picker.FileName, null);
                RefreshProgramsUI();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program ritual submission failed");
            }
        }

        internal void BtnStartTodaySession_Click(object sender, RoutedEventArgs e) => StartProgramSession();

        // -----------------------------------------------------------------------------------
        // Starting today's session
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Mirrors StartSessionFromRemote (MainWindow.RemoteControl.cs). The session engine is a
        /// private MainWindow field created lazily, so this is the only place the Programs feature
        /// can reach it. StartSessionAsync THROWS when a session is already running - it does not
        /// return false - so a session already in flight is refused here, before anything is built.
        /// </summary>
        internal async void StartProgramSession()
        {
            try
            {
                // Never start on top of a running session. This used to stop whatever was running
                // and take over, which silently killed a session started from the Dashboard, a
                // preset or the remote. The button is disabled in that state too
                // (UpdateProgramSessionRow); this is the guard behind it.
                if (_sessionEngine?.IsRunning == true)
                {
                    App.Logger?.Information("[Programs] Start refused - a session is already running");
                    UpdateProgramSessionRow();
                    ShowStyledDialog(Loc.Get("programs_session_busy_title"),
                        Loc.Get("programs_session_busy_body"), Loc.Get("btn_ok"), "");
                    return;
                }

                var session = App.Programs?.BuildTodaySession();
                if (session == null)
                {
                    App.Logger?.Warning("[Programs] BuildTodaySession returned null - nothing to start");
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("programs_session_start_failed"),
                        Loc.Get("btn_ok"), "");
                    return;
                }

                if (_sessionEngine == null)
                {
                    _sessionEngine = new Services.SessionEngine(this);
                    _sessionEngine.SessionCompleted += OnSessionCompleted;
                    _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                    _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                    _sessionEngine.SessionStarted += OnSessionStarted;
                    _sessionEngine.SessionStopped += OnSessionStopped;
                }

                // Both attaches detach any previous engine first, so re-attaching is safe.
                App.Bark?.AttachSessionEngine(_sessionEngine);
                App.Programs?.AttachSessionEngine(_sessionEngine);

                if (!_isRunning)
                {
                    StartEngine();

                    // StartEngine turns on whatever the saved settings had; the session engine
                    // owns the overlays from here.
                    App.Overlay?.StopPinkFilter();
                    App.Overlay?.StopSpiral();
                }

                App.IsSessionRunning = true;
                await _sessionEngine.StartSessionAsync(session);

                App.Logger?.Information("[Programs] Started program session: {Name}", session.Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[Programs] Failed to start today's session");
                try
                {
                    ShowStyledDialog(Loc.Get("title_error"), Loc.Get("programs_session_start_failed"),
                        Loc.Get("btn_ok"), "");
                }
                catch { /* the dialog failing must not mask the original error */ }
            }
        }

        // -----------------------------------------------------------------------------------
        // Dashboard "Today" card
        // -----------------------------------------------------------------------------------

        /// <summary>Click target on the dashboard card.</summary>
        internal void ProgramTodayCard_Click(object sender, RoutedEventArgs e) => ShowTab("programs");

        /// <summary>
        /// Repaints the dashboard's Today card. Collapsed entirely when no program is running -
        /// the dashboard is crowded enough without an empty placeholder.
        /// </summary>
        internal void RefreshProgramTodayCard()
        {
            try
            {
                var dash = SettingsTab;
                if (dash?.ProgramTodayCard == null) return;

                var svc = App.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;
                var day = svc?.Today;
                var record = svc?.TodayRecord;

                if (svc == null || enrollment == null || program == null || day == null || record == null ||
                    enrollment.State is ProgramEnrollmentState.Withdrawn or ProgramEnrollmentState.Graduated)
                {
                    dash.ProgramTodayCard.Visibility = Visibility.Collapsed;
                    return;
                }

                var accent = ProgramAccentBrush(program.AccentColor);
                dash.ProgramTodayCard.BorderBrush = accent;
                dash.ProgramTodayAccent.Background = accent;
                dash.TxtProgramTodayTitle.Foreground = accent;
                dash.TxtProgramTodayTitle.Text = program.Title;

                string remainder;
                if (enrollment.State == ProgramEnrollmentState.Paused)
                {
                    remainder = Loc.Get("programs_card_paused");
                }
                else if (enrollment.State == ProgramEnrollmentState.Lapsed)
                {
                    remainder = Loc.Get("programs_card_lapsed");
                }
                else
                {
                    var parts = new List<string>();
                    if (!record.SessionCompleted) parts.Add(Loc.Get("programs_card_session_left"));

                    var tasksLeft = svc.RequiredTasks(day).Count(t => !svc.IsTaskComplete(record, t));
                    if (tasksLeft == 1) parts.Add(Loc.Get("programs_card_task_left_one"));
                    else if (tasksLeft > 1) parts.Add(Loc.GetF("programs_card_task_left_many", tasksLeft));

                    remainder = parts.Count == 0
                        ? Loc.Get("programs_card_all_done")
                        : Loc.GetF("programs_card_remaining", string.Join(", ", parts));
                }

                dash.TxtProgramTodayLine.Text = string.Join("  ·  ", new[]
                {
                    Loc.GetF("programs_card_day", enrollment.CurrentDay),
                    day.Title,
                    remainder
                });

                // Themed art + idle FX for this program (MainWindow.ProgramBanner.cs). Kept out of
                // the collapse path on purpose: the decoration stops itself off the card's own
                // IsVisible, so "no program", "another tab" and "shutting down" are all one hook.
                ApplyProgramBannerArt(program, accent);

                dash.ProgramTodayCard.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "RefreshProgramTodayCard failed");
            }
        }

        #endregion
    }
}
