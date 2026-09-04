using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Windows/ModCreatorWindow.xaml.cs.
    ///
    /// The window is ~95% code-built: the .axaml is a shell (title bar, sidebar host, content
    /// host, bottom bar) and every section below it is constructed here, exactly as in the WPF
    /// original. Field names, section order, slot lists, labels and descriptions are copied
    /// verbatim so the two files diff.
    ///
    /// Mechanical translations applied throughout, each the only Avalonia spelling of the WPF one:
    ///   ColorConverter.ConvertFromString(hex)  -> Color.Parse / Brush.Parse
    ///   Visibility.Collapsed/Visible           -> IsVisible = false/true
    ///   Cursors.Hand                           -> new Cursor(StandardCursorType.Hand)
    ///   FontWeights.Bold / FontStyles.Italic   -> FontWeight.Bold / FontStyle.Italic
    ///   Style = (Style)FindResource(k)         -> Theme = (ControlTheme)this.FindResource(k)
    ///   tb.VerticalScrollBarVisibility         -> ScrollViewer.SetVerticalScrollBarVisibility
    ///   ToolTip = "x"                          -> ToolTip.SetTip(control, "x")
    ///   ScrollToTop()                          -> ScrollToHome()
    ///   CheckBox.Checked/Unchecked             -> IsCheckedChanged
    ///   BitmapImage + DecodePixelWidth         -> Bitmap.DecodeToWidth(stream, 200)
    ///   AllowDrop/DragOver/Drop                -> DragDrop.SetAllowDrop + AddHandler(DropEvent)
    ///   OpenFileDialog / SaveFileDialog        -> StorageProvider.Open/SaveFilePickerAsync
    ///   MessageBox.Show(msg, caption)          -> MessageDialog.ShowAsync(this, title, msg)
    ///                                             NOTE the argument order flips
    ///   WinForms ColorDialog                   -> Views/Dialogs/ColorPickerDialog.PickAsync
    ///   Tag + GotFocus/LostFocus placeholder   -> TextBox.PlaceholderText
    ///
    /// Two deliberate simplifications, both taking a native rung rather than porting code:
    ///   - The placeholder TextBox dance (Tag + GotFocus/LostFocus + a dimmed Foreground, and
    ///     GetTextBoxValue comparing Text against the placeholder to decide it is empty) is
    ///     Avalonia's <c>TextBox.Watermark</c>. That deletes ~45 lines and fixes the case the WPF
    ///     version got wrong - an author who genuinely typed the placeholder text exported an
    ///     empty field. Two call sites passed a real VALUE as the "placeholder"
    ///     (AddCustomAvatarSet, AddVideoLinkRow); those set .Text instead, so they still show it.
    ///   - SetImageSlot/ClearImageSlot walked the visual tree to find each slot's Border. The
    ///     parts are created here, so <see cref="_imageSlotParts"/> holds them by key instead.
    ///
    /// The manifest round-trip is live: BuildManifestFromForm, PopulateFromManifest, export to a
    /// .ccpmod zip, load from one, and the active mod auto-loaded as a starting preset. The models
    /// are Core's (ModManifest / ModPackage) and the active mod comes from the CoreMods seam, which
    /// answers "the built-in default" on a head with no mod layer, so the ctor touches no disk.
    ///
    /// Stubbed, all with a ponytail marker at the call site: everything reaching App.*, a service,
    /// NAudio, or one of the nine per-panel partial classes
    /// (ModCreatorWindow.Pools.cs, .Personalities.cs, .Advanced.cs, .Barks.cs, .Mantras.cs,
    /// .EventAudio.cs, .Portraits.cs, .Emotes.cs, .UiArt.cs / .ArtFraming.cs), which are their own
    /// port layers. Their sidebar entries stay - they are part of this view's chrome - and each
    /// draws a "ported in a later layer" panel rather than a dead click.
    /// </summary>
    public partial class ModCreatorWindow : Window
    {
        // ─── State ───────────────────────────────────────────────
        private readonly Dictionary<string, string?> _imageSlots = new();       // resourceKey → local file path
        private readonly Dictionary<string, Image> _imageControls = new();      // resourceKey → Image control
        private readonly Dictionary<string, string> _imageNames = new();        // resourceKey → display name
        private readonly Dictionary<string, TextBox> _imageNameBoxes = new();   // resourceKey → name TextBox
        private readonly Dictionary<string, List<string>> _phraseData = new();  // category → phrase list
        private readonly Dictionary<string, StackPanel> _phrasePanels = new();  // category → UI panel
        private readonly Dictionary<string, Border> _sectionPanels = new();     // sectionKey → panel
        private readonly List<(TextBox From, TextBox To)> _textReplacements = new();

        // Section text field controls
        private TextBox? _txtModName, _txtAuthor, _txtVersion, _txtDescription;
        private TextBox? _txtTags, _txtMinAppVersion;
        private TextBox? _txtAffirmation, _txtRankSubject;
        private TextBox? _txtAccentHex, _txtLightHex, _txtDarkHex, _txtFilterHex;
        private TextBox? _txtBgHex, _txtPanelHex, _txtSurfaceHex;
        private Border? _swatchAccent, _swatchLight, _swatchDark, _swatchFilter;
        private Border? _swatchBg, _swatchPanel, _swatchSurface;
        private TextBox? _txtMistHex, _txtParticleHex, _txtGlowHex, _txtFlashTintHex;
        private Border? _swatchMist, _swatchParticle, _swatchGlow, _swatchFlashTint;
        private StackPanel? _previewStrip;
        private TextBox? _txtCompanionName, _txtUserTerm, _txtModeDisplayName, _txtTalkToLabel, _txtTakeoverLabel;
        private TextBox? _txtFreeze, _txtReset, _txtCumCollapse, _txtAutonomyOn;
        private TextBox? _txtAttentionFail, _txtAttentionMercy, _txtBubbleRetry;
        private StackPanel? _replacementsPanel;

        // Avatar set toggles and custom sets
        private readonly Dictionary<int, CheckBox> _avatarSetCheckboxes = new();
        private readonly Dictionary<int, StackPanel> _avatarSetContainers = new();
        private StackPanel? _avatarSetsParent;
        private readonly List<(int SetNum, TextBox LabelBox, TextBox LevelBox, StackPanel Container)> _customAvatarSets = new();
        private int _nextCustomSetNum = 8;

        // Audio slots and voice lines
        private readonly Dictionary<string, string?> _audioSlots = new();
        private readonly Dictionary<string, TextBlock> _audioFileLabels = new();
        private readonly Dictionary<string, Grid> _audioRows = new();
        private readonly List<(string FilePath, StackPanel Row)> _voiceLines = new();
        private StackPanel? _voiceLinesPanel;

        // Browser / video links
        private TextBox? _txtBrowserUrl, _txtBrowserSiteName;
        private CheckBox? _chkShowBambiCloud;
        private readonly List<(TextBox Name, TextBox Url)> _videoLinks = new();
        private StackPanel? _videoLinksPanel;

        private string _activeSectionKey = "";
        private readonly Dictionary<string, Button> _sidebarButtons = new();
        private readonly bool _startWithTutorial;

        // Shell controls. The examples in this head call AvaloniaXamlLoader.Load directly rather
        // than the generated InitializeComponent, so named controls are resolved by FindControl.
        private readonly StackPanel _sidebarPanel;
        private readonly StackPanel _contentPanel;
        private readonly ScrollViewer _contentScroll;
        private readonly TextBlock _txtStatus;

        // ─── Slot Definitions ────────────────────────────────────
        // ponytail: needs Achievement.All, wired when the achievement registry moves to Core.
        // In the WPF head this is ModAchievementSlots.Build(), which walks Achievement.All (69
        // entries) and dedupes by badge file. Achievement lives in ConditioningControlPanel/Models,
        // which this head cannot reference, so a representative slice of the real badge files
        // stands in - enough for the Achievements section to draw its grid with true filenames.
        private static readonly (string Key, string Name)[] AchievementSlots =
        {
            ("achievements/lv_10.png", "Level 10"),
            ("achievements/Dumb_Bimbo.png", "Dumb Bimbo"),
            ("achievements/lv_50.png", "Level 50"),
            ("achievements/docile_cow.png", "Docile Cow"),
            ("achievements/perfect_plastic_puppet.png", "Perfect Plastic Puppet"),
            ("achievements/BrainwashedSlavedoll.png", "Brainwashed Slavedoll"),
            ("achievements/PlatinumPuppet.png", "Platinum Puppet"),
            ("achievements/daily_maintenance.png", "Daily Maintenance"),
            ("achievements/window_shopping.png", "Window Shopping"),
            ("achievements/10_hours_pink.png", "10 Hours Pink"),
            ("achievements/deep_sleep.png", "Deep Sleep"),
            ("achievements/spiral_eyes.png", "Spiral Eyes"),
            ("achievements/obedience_reflex.png", "Obedience Reflex"),
            ("achievements/total_lockdown.png", "Total Lockdown"),
            ("achievements/modder.png", "Modder"),
            ("achievements/she_remembers.png", "She Remembers"),
        };

        private static readonly (string Key, string Name)[] FeatureSlots =
        {
            ("features/flash.png", "Flash Images"),
            ("features/mandatory_videos.png", "Mandatory Videos"),
            ("features/subliminal.png", "Subliminal Text"),
            ("features/bouncing_text.png", "Bouncing Text"),
            ("features/Pink_filter.png", "Pink Filter"),
            ("features/spiral_overlay.png", "Spiral Overlay"),
            ("features/brain_drain.png", "Brain Drain"),
            ("features/Bubble_pop.png", "Bubbles"),
            ("features/Phrase_Lock.png", "Lock Cards"),
            ("features/Bubble_count.png", "Bubble Count"),
            ("features/corner_gif.png", "Corner GIF"),
            ("features/audio_whispers.png", "Audio Whispers"),
            ("features/Mind_Wipers.png", "Mind Wipe"),
            // "Alt" was exactly backwards for anyone but BambiSleep, and the Frame button made it
            // visible: features/takeover.png is the Takeover page art for EVERY other mod AND the
            // premium rail chip (always, whatever the active mod), while "bambi takeover.png" is
            // reached only when BambiSleep is active. An author framing their rail chip was being
            // sent to the slot that reads like the spare one. Display names only - the keys are a
            // compatibility surface and are never renamed.
            ("features/bambi takeover.png", "Takeover (BambiSleep only)"),
            ("features/takeover.png", "Takeover (page + rail chip)"),
            ("features/vibe.png", "Vibe"),
            ("features/4new.png", "New Features"),
        };

        private static readonly (string Key, string Name)[] SkillSlots =
        {
            ("skills/pink_hours.png", "Pink Hours"),
            ("skills/ditzy_data.png", "Ditzy Data"),
            ("skills/sparkle_boost_1.png", "Sparkle Boost"),
            ("skills/good_girl_streak.png", "Good Girl Streak"),
            ("skills/hive_mind.png", "Hive Mind"),
            ("skills/trophy_case.png", "Trophy Case"),
            ("skills/sparkle_boost_2.png", "Extra Sparkly"),
            ("skills/lucky_bimbo.png", "Lucky Bimbo"),
            ("skills/milestone_rewards.png", "Milestone Rewards"),
            ("skills/oopsie_insurance.png", "Oopsie Insurance"),
            ("skills/popular_girl.png", "Popular Girl"),
            ("skills/quest_refresh.png", "Quest Refresh"),
            ("skills/better_quests.png", "Better Quests"),
            ("skills/sparkle_boost_3.png", "Maximum Sparkle"),
            ("skills/lucky_bubbles.png", "Lucky Bubbles"),
            ("skills/pink_rush.png", "Pink Rush"),
            ("skills/streak_power.png", "Streak Power"),
            ("skills/reroll_addict.png", "Reroll Addict"),
            ("skills/perfect_bimbo_week.png", "Perfect Bimbo Week"),
            ("skills/night_shift.png", "Night Shift"),
            ("skills/early_bird_bimbo.png", "Early Bird Bimbo"),
            ("skills/eternal_doll.png", "Eternal Doll"),
        };

        private static readonly (string SetLabel, string Prefix, int SetNum)[] AvatarSets =
        {
            ("Default", "avatar", 1),
            ("Level 20", "avatar2", 2),
            ("Level 35", "avatar3", 3),
            ("Level 50", "avatar4", 4),
            ("Level 125", "avatar5", 5),
            ("Level 150", "avatar6", 6),
            ("Level 175", "avatar7", 7),
        };

        private static readonly (string Key, string Name)[] UiAssetSlots =
        {
            ("bubble.png", "Bubble"),
            ("tube.png", "Tube"),
            ("tube2.png", "Tube Alt"),
            ("spiral.gif", "Spiral GIF"),
            ("logo.png", "Logo"),
        };

        private static readonly string[] PhraseCategories =
        {
            "Greeting", "StartupGreeting", "Idle", "RandomFloating", "Generic",
            "Gaming", "Browsing", "Shopping", "Social", "Discord",
            "TrainingSite", "HypnoContent", "Working", "Media", "Learning",
            "WindowAwarenessIdle", "EngineStop", "FlashPre", "SubliminalAck",
            "RandomBubble", "BubbleCountMercy", "BubblePop", "GameFailed",
            "BubbleMissed", "FlashClicked", "LevelUp", "MindWipe", "BrainDrain"
        };

        private static readonly (string Key, string Label)[] SectionDefs =
        {
            ("info", "Info"),
            ("theme", "Theme"),
            ("identity", "Identity"),
            ("achievements", "Achievements"),
            ("features", "Features"),
            ("skills", "Skills"),
            ("avatars", "Avatars"),
            ("uiassets", "UI Assets"),
            ("uiart", "UI Art"),
            ("audio", "Audio"),
            ("browser", "Browser"),
            ("triggers", "Triggers"),
            ("messages", "Messages"),
            ("phrases", "Phrases"),
            ("replacements", "Text Replacements"),
            ("pools", "Pools & Triggers"),
            ("personalities", "Personalities"),
            ("advanced", "Advanced"),
            ("barks", "Barks"),
            ("mantras", "Mantras"),
            ("eventaudio", "Event Audio"),
            ("portraits", "Portraits"),
            ("emotes", "Animated Emotes"),
        };

        // ─── Constructor ─────────────────────────────────────────
        /// <summary>
        /// Parameterless entry point. The WPF ctor's one optional argument is invisible to
        /// reflection, so --render-all could not discover the window without this.
        /// </summary>
        public ModCreatorWindow() : this(false) { }

        public ModCreatorWindow(bool startWithTutorial)
        {
            _startWithTutorial = startWithTutorial;
            AvaloniaXamlLoader.Load(this);

            _sidebarPanel = this.FindControl<StackPanel>("SidebarPanel")!;
            _contentPanel = this.FindControl<StackPanel>("ContentPanel")!;
            _contentScroll = this.FindControl<ScrollViewer>("ContentScroll")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;

            this.FindControl<Border>("TitleBar")!.PointerPressed += TitleBar_PointerPressed;
            this.FindControl<Button>("BtnHelp")!.Click += (_, _) => BtnHelp_Click();
            this.FindControl<Button>("BtnMinimize")!.Click += (_, _) => WindowState = WindowState.Minimized;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnLoad")!.Click += (_, _) => BtnLoad_Click();
            this.FindControl<Button>("BtnReset")!.Click += (_, _) => BtnReset_Click();
            this.FindControl<Button>("BtnExport")!.Click += (_, _) => BtnExport_Click();

            BuildSidebar();
            BuildAllSections();
            PopulateDefaults();
            LoadActiveModAsPreset();
            NavigateToSection("info");
            UpdateStatusBar();

            if (_startWithTutorial)
            {
                Opened += (_, _) => LaunchTutorial();
            }
        }

        // ─── Title Bar ──────────────────────────────────────────
        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                // WPF's DragMove(). Throws if the button was released between press and call.
                try { BeginMoveDrag(e); } catch { /* not pressed any more */ }
            }
        }

        private void BtnHelp_Click()
        {
            // WPF's order: prefer the tutorial clip when the topic ships one, else the coach-mark
            // tutorial. Both halves are reachable now - HelpContentService is Core's, HelpVideoWindow
            // is this head's - and "Modding" does ship a clip, so this always takes the video branch
            // and the popup always lands in its fail-soft state (no video surface on this head:
            // title, glyph and the topic blurb). LaunchTutorial is the other branch and is wired
            // now, so the fallback is live rather than a dead button.
            var content = Services.HelpContentService.GetContent("Modding");
            if (content.HasClip)
            {
                HelpVideoWindow.Show(content, this);
                return;
            }
            LaunchTutorial();
        }

        /// <summary>
        /// WPF's <c>App.Tutorial.Start(TutorialType.Modding)</c> plus a <c>TutorialOverlay</c>, both
        /// of which this head has: <see cref="CoreTutorial"/> is the seam and
        /// <see cref="TutorialOverlay"/> is the ported overlay, which reads that seam itself.
        ///
        /// <para>The overlay is only shown once the seam confirms a tour is actually running.
        /// Unseeded, <c>Start</c> is a silent no-op and <c>IsActive</c> stays false, so this does
        /// nothing rather than putting an empty coach-mark card on screen - a tour that looks
        /// started while nothing tracks is worse than no tour.</para>
        ///
        /// <para>ponytail: the step-rewriting half cannot be done through the seam. WPF walks
        /// <c>App.Tutorial.CurrentSteps</c> and replaces each <c>step.OnActivate</c> so a step
        /// tagged <c>RequiresTab="mod:&lt;section&gt;"</c> calls <see cref="NavigateToSection"/>.
        /// <c>CoreTutorial</c> deliberately exposes only a per-step SNAPSHOT (no step list, no
        /// OnActivate) because activation is head navigation - so until the seam grows a
        /// step-activation callback, a Modding tour spotlights sections this window does not
        /// navigate to. NavigateToSection is ready for it.</para>
        /// </summary>
        private void LaunchTutorial()
        {
            if (_tutorialOverlay != null) return;

            CoreTutorial.Start("Modding");
            if (!CoreTutorial.IsActive) return;

            _tutorialOverlay = new TutorialOverlay(this);
            _tutorialOverlay.Closed += (_, _) => _tutorialOverlay = null;
            _tutorialOverlay.Show();
        }

        private TutorialOverlay? _tutorialOverlay;

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            StopAudioPreview();
            // Null the field BEFORE closing, and do not Skip here. TutorialOverlay.OnClosed already
            // skips a still-running tour, and it also subscribes to CoreTutorial.Finished and closes
            // itself from it - so calling Skip() first would close the overlay, run the Closed
            // handler below that nulls this field, and leave the next line dereferencing null.
            var overlay = _tutorialOverlay;
            _tutorialOverlay = null;
            overlay?.Close();
            CleanupTempDir();
        }

        // ─── Sidebar ────────────────────────────────────────────
        private void BuildSidebar()
        {
            foreach (var (key, label) in SectionDefs)
            {
                var btn = new Button
                {
                    Content = label,
                    Tag = key,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(13, 9, 13, 9),
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(0),
                };

                btn.Click += (_, _) => NavigateToSection(key);
                _sidebarButtons[key] = btn;
                _sidebarPanel.Children.Add(btn);
            }
        }

        private void NavigateToSection(string key)
        {
            _activeSectionKey = key;

            foreach (var (k, btn) in _sidebarButtons)
            {
                if (k == key)
                {
                    btn.Background = new SolidColorBrush(Color.Parse("#353560"));
                    btn.Foreground = Brushes.White;
                    btn.FontWeight = FontWeight.SemiBold;
                    btn.BorderBrush = new SolidColorBrush(Color.Parse("#FF69B4"));
                    btn.BorderThickness = new Thickness(3, 0, 0, 0);
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = new SolidColorBrush(Color.Parse("#C0C0C0"));
                    btn.FontWeight = FontWeight.Normal;
                    btn.BorderThickness = new Thickness(0);
                }
            }

            foreach (var (k, panel) in _sectionPanels)
                panel.IsVisible = k == key;

            _contentScroll.ScrollToHome();
        }

        // ─── Build All Sections ──────────────────────────────────
        private void BuildAllSections()
        {
            BuildInfoSection();
            BuildThemeSection();
            BuildIdentitySection();
            BuildImageSlotsSection("achievements", "Achievements", "Custom badge images for the built-in achievements shown in the Trophy Case. Use square PNGs (128x128 recommended). Achievement display names are changed via Text Replacements, not here.", AchievementSlots);
            BuildImageSlotsSection("features", "Features", "Icons for the feature tiles in the main control panel tabs (Flashes, Videos, Overlays, etc). Use square PNGs with transparent backgrounds. Feature display names are changed via Text Replacements, not here.", FeatureSlots);
            BuildImageSlotsSection("skills", "Skills", "Icons for the nodes in the skill tree. Each icon represents a specific unlockable skill. Use square PNGs. Skill display names are changed via Text Replacements.", SkillSlots);
            BuildAvatarsSection();
            BuildImageSlotsSection("uiassets", "UI Assets", "Miscellaneous UI images: Bubble is the floating orb in the pop minigame, Tube is the glass container around the avatar, Spiral GIF is the hypnotic overlay animation, and Logo replaces the app logo.", UiAssetSlots);
            BuildAudioSection();
            BuildBrowserSection();
            BuildTriggersSection();
            BuildMessagesSection();
            BuildPhrasesSection();
            BuildReplacementsSection();

            // The nine newer content types are one partial-class file per panel in the WPF head
            // and one port layer each here. Their sidebar entries stay, so each gets a panel
            // saying so rather than a button that does nothing.
            BuildDeferredSection("uiart", "UI Art", "ModCreatorWindow.UiArt.cs / .ArtFraming.cs");
            BuildDeferredSection("pools", "Pools & Triggers", "ModCreatorWindow.Pools.cs");
            BuildDeferredSection("personalities", "Personalities", "ModCreatorWindow.Personalities.cs");
            BuildDeferredSection("advanced", "Advanced", "ModCreatorWindow.Advanced.cs");
            BuildDeferredSection("barks", "Barks", "ModCreatorWindow.Barks.cs");
            BuildDeferredSection("mantras", "Mantras", "ModCreatorWindow.Mantras.cs");
            BuildDeferredSection("eventaudio", "Event Audio", "ModCreatorWindow.EventAudio.cs");
            BuildDeferredSection("portraits", "Portraits", "ModCreatorWindow.Portraits.cs");
            BuildDeferredSection("emotes", "Animated Emotes", "ModCreatorWindow.Emotes.cs");
        }

        /// <summary>
        /// ponytail: needs the matching ModCreatorWindow partial, ported in its own layer. Draws
        /// the section's header plus a one-line note, so the sidebar entry leads somewhere.
        /// </summary>
        private void BuildDeferredSection(string key, string header, string partialFile)
        {
            var panel = CreateSectionPanel(key);
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader(header));
            stack.Children.Add(CreateSectionDescription(
                $"This panel is built by {partialFile} in the WPF head and is ported in a later layer."));
        }

        // ─── Side-file seams ─────────────────────────────────────
        // ponytail: needs the panel partials (Barks/Mantras/EventAudio/Portraits/Emotes), wired
        // when each is ported. In the WPF head these three aggregate hooks let export write each
        // panel's side files into the .ccpmod resources tree, let load repopulate the panels from
        // an extracted tree, and let reset clear them.
        private void WriteSideFilesTo(string resourcesDir) { }
        private void LoadSideFilesFrom(string resourcesDir) { }
        private void ClearSideFileState() { }

        // ponytail: needs the manifest-backed panel partials (Pools/Personalities/Advanced/UiArt),
        // wired when each is ported. In the WPF head BuildManifestFromForm ends with
        // ApplyPoolsToManifest / ApplyPersonalitiesToManifest / ApplyAdvancedToManifest /
        // ApplyArtFramingToManifest and PopulateFromManifest with the four Populate* twins.
        // Aggregated into one pair on purpose: a later layer that ports Pools.cs as a partial will
        // define the real ApplyPoolsToManifest, and a stub of that exact name here would collide.
        private void ApplyPanelSectionsToManifest(ModManifest manifest) { }
        private void PopulatePanelSectionsFromManifest(ModManifest manifest) { }

        private Border CreateSectionPanel(string key)
        {
            var panel = new Border
            {
                IsVisible = false,
                Padding = new Thickness(0, 5, 0, 20),
            };
            _sectionPanels[key] = panel;
            _contentPanel.Children.Add(panel);
            return panel;
        }

        private static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#FF69B4")),
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 12),
            };
        }

        private static TextBlock CreateSectionDescription(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#909090")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, -6, 0, 14),
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
                LineHeight = 18,
            };
        }

        private static TextBlock CreateSubHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#B0B0B0")),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 6),
            };
        }

        private static TextBlock CreateFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 3),
            };
        }

        private ControlTheme DarkTextBoxTheme => (ControlTheme)this.FindResource("DarkTextBox")!;

        /// <summary>
        /// The WPF version faked a watermark with Tag + GotFocus/LostFocus + a dimmed Foreground,
        /// and every reader had to compare Text against the placeholder to tell "empty" from
        /// "the author typed exactly that". Avalonia has PlaceholderText; see the class note.
        /// </summary>
        private TextBox CreateDarkTextBox(string placeholder = "", bool multiline = false, double height = 0)
        {
            var tb = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Margin = new Thickness(0, 0, 0, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            if (multiline)
            {
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Auto);
                if (height > 0) tb.Height = height;
            }
            if (!string.IsNullOrEmpty(placeholder))
                tb.PlaceholderText = placeholder;
            return tb;
        }

        private static string GetTextBoxValue(TextBox? tb) => tb?.Text ?? "";

        private static void SetTextBoxValue(TextBox? tb, string? value)
        {
            if (tb == null) return;
            tb.Text = value ?? "";
        }

        // ─── Info Section ────────────────────────────────────────
        private void BuildInfoSection()
        {
            var panel = CreateSectionPanel("info");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Mod Info"));
            stack.Children.Add(CreateSectionDescription("Basic metadata for your mod package. Name and author are required. The preview image appears as your mod's thumbnail in the mod browser."));

            stack.Children.Add(CreateFieldLabel("Mod Name *"));
            _txtModName = CreateDarkTextBox();
            _txtModName.Width = 350;
            stack.Children.Add(_txtModName);

            stack.Children.Add(CreateFieldLabel("Author *"));
            _txtAuthor = CreateDarkTextBox();
            _txtAuthor.Width = 250;
            stack.Children.Add(_txtAuthor);

            stack.Children.Add(CreateFieldLabel("Version"));
            _txtVersion = CreateDarkTextBox();
            _txtVersion.Width = 120;
            _txtVersion.Text = "1.0.0";
            stack.Children.Add(_txtVersion);

            stack.Children.Add(CreateFieldLabel("Description"));
            _txtDescription = CreateDarkTextBox(multiline: true, height: 80);
            _txtDescription.Width = 500;
            stack.Children.Add(_txtDescription);

            stack.Children.Add(CreateFieldLabel("Tags (comma-separated)"));
            _txtTags = CreateDarkTextBox("e.g. feminization, soft, voice");
            _txtTags.Width = 350;
            stack.Children.Add(_txtTags);

            stack.Children.Add(CreateFieldLabel("Minimum App Version"));
            _txtMinAppVersion = CreateDarkTextBox("e.g. 6.3.4 (optional)");
            _txtMinAppVersion.Width = 160;
            stack.Children.Add(_txtMinAppVersion);

            stack.Children.Add(CreateFieldLabel("Preview Image"));
            var previewSlot = CreateImageSlot("preview", "Preview Image", 160, 120);
            stack.Children.Add(previewSlot);
        }

        // FX palette rows are "unset = inherit": a row still on this value exports as null, so the
        // palette keeps falling back to filterColor → accentColor (ModService.ResolveFxSlotHex).
        private const string FxRowDefault = "#FF69B4";

        // ─── Theme Section ───────────────────────────────────────
        private void BuildThemeSection()
        {
            var panel = CreateSectionPanel("theme");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Theme Colors"));
            stack.Children.Add(CreateSectionDescription("Colors applied across the entire app UI when your mod is active. Accent is the primary highlight (buttons, links, progress bars). Filter Color tints the screen overlay. Keep sufficient contrast between Background/Panel/Surface or text becomes unreadable."));

            (_swatchAccent, _txtAccentHex) = CreateColorRow(stack, "Accent Color", "#FF69B4");
            (_swatchLight, _txtLightHex) = CreateColorRow(stack, "Light Color", "#FFB6C1");
            (_swatchDark, _txtDarkHex) = CreateColorRow(stack, "Dark Color", "#FF1493");
            (_swatchFilter, _txtFilterHex) = CreateColorRow(stack, "Filter Color", "#FF69B4");

            stack.Children.Add(CreateSubHeader("Background Colors"));
            (_swatchBg, _txtBgHex) = CreateColorRow(stack, "Background Color", "#1A1A2E");
            (_swatchPanel, _txtPanelHex) = CreateColorRow(stack, "Panel Color", "#252542");
            (_swatchSurface, _txtSurfaceHex) = CreateColorRow(stack, "Surface Color", "#1E1E3A");

            stack.Children.Add(CreateSubHeader("Ambient FX Palette"));
            stack.Children.Add(CreateSectionDescription("Optional. Colors for the drifting fog, particle bursts, glow breathing and one-shot flashes. Leave these on the defaults and the FX follow your Filter Color, then your Accent Color — only set them when you want the atmosphere a different color from the UI."));
            (_swatchMist, _txtMistHex) = CreateColorRow(stack, "Mist Color", FxRowDefault);
            (_swatchParticle, _txtParticleHex) = CreateColorRow(stack, "Particle Color", FxRowDefault);
            (_swatchGlow, _txtGlowHex) = CreateColorRow(stack, "Glow Color", FxRowDefault);
            (_swatchFlashTint, _txtFlashTintHex) = CreateColorRow(stack, "Flash Tint", FxRowDefault);

            stack.Children.Add(CreateSubHeader("Preview"));
            _previewStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            for (int i = 0; i < 6; i++)
            {
                _previewStrip.Children.Add(new Border
                {
                    Width = 60,
                    Height = 30,
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 0, 6, 0),
                });
            }
            stack.Children.Add(_previewStrip);
            UpdateThemePreview();
        }

        private (Border swatch, TextBox hexBox) CreateColorRow(StackPanel parent, string label, string defaultHex)
        {
            parent.Children.Add(CreateFieldLabel(label));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

            var swatch = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(4),
                Background = BrushFromHex(defaultHex),
                Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
                BorderThickness = new Thickness(1),
            };
            row.Children.Add(swatch);

            var hexBox = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Width = 100,
                Text = defaultHex,
                Margin = new Thickness(0, 0, 8, 0),
            };
            hexBox.TextChanged += (_, _) =>
            {
                var hex = (hexBox.Text ?? "").Trim();
                if (TryParseHex(hex, out var color))
                {
                    swatch.Background = new SolidColorBrush(color);
                    UpdateThemePreview();
                }
            };
            row.Children.Add(hexBox);

            var pickBtn = new Button
            {
                Content = "Pick",
                Theme = (ControlTheme)this.FindResource("SecondaryButton")!,
                Padding = new Thickness(10, 4, 10, 4),
            };
            pickBtn.Click += async (_, _) =>
            {
                var result = await ShowColorPickerAsync((hexBox.Text ?? "").Trim(), label);
                if (result != null)
                {
                    hexBox.Text = result;
                }
            };
            row.Children.Add(pickBtn);

            parent.Children.Add(row);
            return (swatch, hexBox);
        }

        private void UpdateThemePreview()
        {
            if (_previewStrip == null || _txtAccentHex == null || _txtLightHex == null || _txtDarkHex == null) return;

            var hexes = new[]
            {
                (_txtAccentHex.Text ?? "").Trim(), (_txtLightHex.Text ?? "").Trim(), (_txtDarkHex.Text ?? "").Trim(),
                (_txtBgHex?.Text ?? "#1A1A2E").Trim(), (_txtPanelHex?.Text ?? "#252542").Trim(), (_txtSurfaceHex?.Text ?? "#1E1E3A").Trim()
            };
            for (int i = 0; i < hexes.Length && i < _previewStrip.Children.Count; i++)
            {
                if (_previewStrip.Children[i] is Border b)
                    b.Background = BrushFromHex(hexes[i]);
            }
        }

        // ─── Identity Section ────────────────────────────────────
        private void BuildIdentitySection()
        {
            var panel = CreateSectionPanel("identity");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Identity"));
            stack.Children.Add(CreateSectionDescription("Renames core concepts throughout the app. Companion Name replaces the avatar's name in speech bubbles. User Term is what the companion calls you. Mode Display Name appears in the title bar. Talk To / Takeover labels change the companion interaction buttons."));

            stack.Children.Add(CreateFieldLabel("Companion Name"));
            _txtCompanionName = CreateDarkTextBox("");
            _txtCompanionName.Width = 250;
            stack.Children.Add(_txtCompanionName);

            stack.Children.Add(CreateFieldLabel("User Term"));
            _txtUserTerm = CreateDarkTextBox("");
            _txtUserTerm.Width = 250;
            stack.Children.Add(_txtUserTerm);

            stack.Children.Add(CreateFieldLabel("Mode Display Name"));
            _txtModeDisplayName = CreateDarkTextBox("");
            _txtModeDisplayName.Width = 250;
            stack.Children.Add(_txtModeDisplayName);

            stack.Children.Add(CreateFieldLabel("Talk To Label"));
            _txtTalkToLabel = CreateDarkTextBox("");
            _txtTalkToLabel.Width = 250;
            stack.Children.Add(_txtTalkToLabel);

            stack.Children.Add(CreateFieldLabel("Takeover Label"));
            _txtTakeoverLabel = CreateDarkTextBox("");
            _txtTakeoverLabel.Width = 250;
            stack.Children.Add(_txtTakeoverLabel);

            stack.Children.Add(CreateFieldLabel("Affirmation"));
            _txtAffirmation = CreateDarkTextBox("Shown on the level-up card, e.g. \"Good girl.\"");
            _txtAffirmation.Width = 350;
            stack.Children.Add(_txtAffirmation);

            stack.Children.Add(CreateFieldLabel("Rank Subject"));
            _txtRankSubject = CreateDarkTextBox("What ranks measure, e.g. \"obedience\"");
            _txtRankSubject.Width = 250;
            stack.Children.Add(_txtRankSubject);
        }

        // ─── Image Slots Sections ────────────────────────────────
        private void BuildImageSlotsSection(string key, string header, string description, (string Key, string Name)[] slots)
        {
            var panel = CreateSectionPanel(key);
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader(header));
            stack.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                // Deliberate divergence: the WPF TextBlock here has no TextWrapping (unlike
                // CreateSectionDescription, which does), so all four of these two-line
                // descriptions run off the right edge and are clipped by the ScrollViewer.
                TextWrapping = TextWrapping.Wrap,
            });

            var wrap = new WrapPanel();
            foreach (var (slotKey, name) in slots)
            {
                // Stale note said this needed App.Mods; CoreMods.MakeModAware is the seam, and
                // returns the input unchanged when no mod layer is up.
                var displayName = CoreMods.MakeModAware(name);
                wrap.Children.Add(CreateImageSlot(slotKey, displayName));
            }
            stack.Children.Add(wrap);
        }

        // ─── Avatars Section ─────────────────────────────────────
        private void BuildAvatarsSection()
        {
            var panel = CreateSectionPanel("avatars");
            var stack = new StackPanel();
            panel.Child = stack;
            _avatarSetsParent = stack;

            stack.Children.Add(CreateSectionHeader("Avatars"));
            stack.Children.Add(new TextBlock
            {
                Text = "Toggle which avatar sets your mod supports. Uncheck sets you don't have images for.",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8),
            });

            // Checkbox row for toggling base sets
            var checkboxRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            foreach (var (setLabel, _, setNum) in AvatarSets)
            {
                var cb = new CheckBox
                {
                    Content = $"Set {setNum}: {setLabel}",
                    IsChecked = true,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 16, 4),
                    FontSize = 11,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                var capturedSetNum = setNum;
                // WPF's separate Checked/Unchecked handlers; Avalonia raises one event for both.
                cb.IsCheckedChanged += (_, _) => ToggleAvatarSet(capturedSetNum, cb.IsChecked == true);
                _avatarSetCheckboxes[setNum] = cb;
                checkboxRow.Children.Add(cb);
            }
            stack.Children.Add(checkboxRow);

            // Build each base set with a container for toggling visibility
            foreach (var (setLabel, prefix, setNum) in AvatarSets)
            {
                var container = new StackPanel();
                container.Children.Add(CreateSubHeader($"Set {setNum}: {setLabel}"));

                var wrap = new WrapPanel();
                for (int pose = 1; pose <= 4; pose++)
                {
                    var filename = setNum == 1
                        ? $"avatar_pose{pose}.png"
                        : $"{prefix}_pose{pose}.png";
                    wrap.Children.Add(CreateImageSlot(filename, $"Pose {pose}"));
                }
                container.Children.Add(wrap);
                _avatarSetContainers[setNum] = container;
                stack.Children.Add(container);
            }

            // Add Custom Set button
            var addBtn = new Button
            {
                Content = "+ Add Custom Avatar Set",
                Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                Foreground = BrushFromHex(AccentColorHex),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushFromHex(AccentColorHex),
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addBtn.Click += (_, _) => AddCustomAvatarSet();
            stack.Children.Add(addBtn);
        }

        /// <summary>The active mod's accent, through the CoreMods seam rather than App.Mods. With
        /// no mod layer up this is the built-in CCP default's accent, which is what the WPF call
        /// answered with no mod active.</summary>
        private static string AccentColorHex => CoreMods.AccentColorHex;

        private void ToggleAvatarSet(int setNum, bool enabled)
        {
            if (_avatarSetContainers.TryGetValue(setNum, out var container))
                container.IsVisible = enabled;
        }

        private void AddCustomAvatarSet(int setNum = 0, string? label = null, int unlockLevel = 200)
        {
            if (setNum == 0) setNum = _nextCustomSetNum++;
            if (setNum >= _nextCustomSetNum) _nextCustomSetNum = setNum + 1;

            var container = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

            // Header row with label, level, and remove button
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            headerRow.Children.Add(new TextBlock
            {
                Text = $"Custom Set {setNum}:  Label ",
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            // Real values, not hints: the WPF call passed them as the fake placeholder, which
            // meant an untouched row exported empty. Set as Text so they are actual content.
            var lblBox = CreateDarkTextBox();
            lblBox.Text = label ?? $"Set {setNum}";
            lblBox.Width = 150;
            headerRow.Children.Add(lblBox);

            headerRow.Children.Add(new TextBlock
            {
                Text = "  Unlock Level ",
                Foreground = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            var lvlBox = CreateDarkTextBox();
            lvlBox.Text = unlockLevel.ToString();
            lvlBox.Width = 60;
            headerRow.Children.Add(lvlBox);

            var capturedSetNum = setNum;
            var capturedContainer = container;
            var removeBtn = new Button
            {
                Content = "✕ Remove",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            removeBtn.Click += (_, _) =>
            {
                _avatarSetsParent?.Children.Remove(capturedContainer);
                _customAvatarSets.RemoveAll(c => c.SetNum == capturedSetNum);
            };
            headerRow.Children.Add(removeBtn);
            container.Children.Add(headerRow);

            // 4 pose image slots
            var wrap = new WrapPanel();
            for (int pose = 1; pose <= 4; pose++)
                wrap.Children.Add(CreateImageSlot($"avatar{setNum}_pose{pose}.png", $"Pose {pose}"));
            container.Children.Add(wrap);

            _customAvatarSets.Add((setNum, lblBox, lvlBox, container));

            // Insert before the "Add Custom Set" button (last child)
            if (_avatarSetsParent != null)
                _avatarSetsParent.Children.Insert(_avatarSetsParent.Children.Count - 1, container);
        }

        // ─── Audio Section ──────────────────────────────────────
        private void BuildAudioSection()
        {
            var panel = CreateSectionPanel("audio");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Audio"));
            stack.Children.Add(new TextBlock
            {
                Text = "Replace companion sounds, bubble pops, and add custom voice lines. Voice line filenames become the spoken text.",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });

            // Sub-group A: Companion Sounds (Giggles)
            stack.Children.Add(CreateSubHeader("Companion Sounds"));
            for (int i = 1; i <= 8; i++)
            {
                var key = $"sounds/giggle{i}.wav";
                stack.Children.Add(CreateAudioSlot(key, $"Giggle {i}"));
            }

            // Sub-group B: Bubble Pop Sounds
            stack.Children.Add(CreateSubHeader("Bubble Pop Sounds"));
            foreach (var name in new[] { "Pop", "Pop2", "Pop3" })
            {
                var key = $"sounds/bubbles/{name}.wav";
                stack.Children.Add(CreateAudioSlot(key, name));
            }

            // Sub-group C: Lucky Bubble Chimes
            stack.Children.Add(CreateSubHeader("Lucky Bubble Chimes"));
            for (int i = 1; i <= 3; i++)
            {
                var key = $"sounds/chime{i}.mp3";
                stack.Children.Add(CreateAudioSlot(key, $"Chime {i}"));
            }

            // Sub-group D: Voice Lines
            stack.Children.Add(CreateSubHeader("Voice Lines"));
            stack.Children.Add(new TextBlock
            {
                Text = "Each file's name becomes the text the companion speaks. E.g. \"COMPLY.mp3\" → companion says \"COMPLY\".",
                Foreground = new SolidColorBrush(Color.Parse("#606080")),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            _voiceLinesPanel = new StackPanel();
            var voiceScroll = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _voiceLinesPanel,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(voiceScroll);

            var addVoiceBtn = new Button
            {
                Content = "+ Add Voice Lines",
                Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                Foreground = BrushFromHex(AccentColorHex),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushFromHex(AccentColorHex),
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addVoiceBtn.Click += async (_, _) =>
            {
                var files = await PickFilesAsync("Select voice line audio files", AudioPatterns, multiple: true);
                foreach (var file in files)
                    AddVoiceLineRow(file);
                if (files.Count > 0) UpdateStatusBar();
            };
            stack.Children.Add(addVoiceBtn);
        }

        private static readonly string[] AudioPatterns = { "*.mp3", "*.wav", "*.ogg", "*.flac" };

        private Grid CreateAudioSlot(string resourceKey, string displayName)
        {
            _audioSlots[resourceKey] = null;

            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4),
                Height = 32
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Label
            var label = new TextBlock
            {
                Text = displayName,
                Foreground = new SolidColorBrush(Color.FromRgb(192, 192, 192)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            // Filename display
            var fileLabel = new TextBlock
            {
                Text = "No file",
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
                FontSize = 11,
                FontStyle = FontStyle.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _audioFileLabels[resourceKey] = fileLabel;
            _audioRows[resourceKey] = grid;
            Grid.SetColumn(fileLabel, 1);
            grid.Children.Add(fileLabel);

            // Play button
            var playBtn = new Button
            {
                Content = "▶",
                Width = 28,
                Height = 28,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 12,
                IsVisible = false,
            };
            ToolTip.SetTip(playBtn, "Play preview");
            var capturedKey = resourceKey;
            playBtn.Click += (_, _) => ToggleAudioPreview(capturedKey, playBtn);
            Grid.SetColumn(playBtn, 2);
            grid.Children.Add(playBtn);

            // Browse button
            var browseBtn = new Button
            {
                Content = "Browse",
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 10,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(4, 0, 0, 0)
            };
            browseBtn.Click += async (_, _) =>
            {
                var files = await PickFilesAsync($"Select audio for {displayName}", AudioPatterns, multiple: false);
                if (files.Count > 0)
                    SetAudioSlot(resourceKey, files[0]);
            };
            Grid.SetColumn(browseBtn, 3);
            grid.Children.Add(browseBtn);

            // Clear button
            var clearBtn = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 0),
                IsVisible = false,
            };
            clearBtn.Click += (_, _) => ClearAudioSlot(resourceKey);
            Grid.SetColumn(clearBtn, 4);
            grid.Children.Add(clearBtn);

            return grid;
        }

        private void SetAudioSlot(string key, string filePath)
        {
            _audioSlots[key] = filePath;
            if (_audioFileLabels.TryGetValue(key, out var label))
            {
                label.Text = Path.GetFileName(filePath);
                label.FontStyle = FontStyle.Normal;
                label.Foreground = Brushes.White;
            }

            // Show play and clear buttons
            SetAudioRowButtons(key, true);
            UpdateStatusBar();
        }

        private void ClearAudioSlot(string key)
        {
            _audioSlots[key] = null;
            if (_audioFileLabels.TryGetValue(key, out var label))
            {
                label.Text = Loc.Get("label_no_file");
                label.FontStyle = FontStyle.Italic;
                label.Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128));
            }

            SetAudioRowButtons(key, false);
            UpdateStatusBar();
        }

        /// <summary>
        /// The WPF version reached the row through label.Parent and matched buttons by their
        /// Content glyph. The row is created here, so it is looked up by key; the glyph match
        /// stays, because the play button's Content flips between ▶ and ⏹.
        /// </summary>
        private void SetAudioRowButtons(string key, bool visible)
        {
            if (!_audioRows.TryGetValue(key, out var grid)) return;
            foreach (var child in grid.Children)
            {
                if (child is Button btn && btn.Content is string glyph && (glyph == "▶" || glyph == "⏹" || glyph == "✕"))
                    btn.IsVisible = visible;
            }
        }

        private void AddVoiceLineRow(string filePath)
        {
            // Check for duplicate filenames
            var fileName = Path.GetFileName(filePath);
            if (_voiceLines.Any(v => Path.GetFileName(v.FilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                return;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

            // Play button
            var playBtn = new Button
            {
                Content = "▶",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedPath = filePath;
            playBtn.Click += (_, _) => ToggleAudioPreview(capturedPath, playBtn);
            row.Children.Add(playBtn);

            // Filename (= spoken text)
            row.Children.Add(new TextBlock
            {
                Text = Path.GetFileNameWithoutExtension(filePath),
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });

            // Extension tag
            row.Children.Add(new TextBlock
            {
                Text = Path.GetExtension(filePath),
                Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 128)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            });

            // Remove button
            var removeBtn = new Button
            {
                Content = "✕",
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 9,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedRow = row;
            removeBtn.Click += (_, _) =>
            {
                _voiceLinesPanel?.Children.Remove(capturedRow);
                _voiceLines.RemoveAll(v => v.Row == capturedRow);
                UpdateStatusBar();
            };
            row.Children.Add(removeBtn);

            _voiceLines.Add((filePath, row));
            _voiceLinesPanel?.Children.Add(row);
        }

        private void ToggleAudioPreview(string keyOrPath, Button playBtn)
        {
            // ponytail: needs an audio player, wired when playback moves behind a Core interface.
            // The WPF version drives NAudio's WaveOutEvent/AudioFileReader directly, flips the
            // button glyph to ⏹ while playing and restores it from PlaybackStopped.
        }

        private void StopAudioPreview()
        {
            // ponytail: needs an audio player - see ToggleAudioPreview.
        }

        private void LoadAudioFromResources(string resourcesDir)
        {
            // Load fixed audio slots
            foreach (var key in _audioSlots.Keys.ToList())
            {
                var audioPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(audioPath))
                {
                    var altExt = Path.GetExtension(audioPath).ToLower() == ".wav" ? ".mp3" : ".wav";
                    audioPath = Path.ChangeExtension(audioPath, altExt);
                }
                if (File.Exists(audioPath))
                    SetAudioSlot(key, audioPath);
            }

            // Load voice lines
            var voiceDir = Path.Combine(resourcesDir, "sounds", "flashes_audio");
            if (Directory.Exists(voiceDir))
            {
                foreach (var ext in AudioPatterns)
                    foreach (var file in Directory.GetFiles(voiceDir, ext).OrderBy(f => f))
                        AddVoiceLineRow(file);
            }
        }

        // ─── Browser Section ─────────────────────────────────────
        private void BuildBrowserSection()
        {
            var panel = CreateSectionPanel("browser");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Browser"));
            stack.Children.Add(new TextBlock
            {
                Text = "Configure the embedded browser defaults and video links the companion will suggest.",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });

            // Default URL
            stack.Children.Add(CreateFieldLabel("Default Browser URL"));
            _txtBrowserUrl = CreateDarkTextBox("");
            _txtBrowserUrl.Width = 400;
            stack.Children.Add(_txtBrowserUrl);

            // Site Name
            stack.Children.Add(CreateFieldLabel("Site Name"));
            _txtBrowserSiteName = CreateDarkTextBox("");
            _txtBrowserSiteName.Width = 250;
            stack.Children.Add(_txtBrowserSiteName);

            // Show BambiCloud option
            _chkShowBambiCloud = new CheckBox
            {
                Content = "Show BambiCloud option in browser menu",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 8, 0, 12),
                FontSize = 12
            };
            stack.Children.Add(_chkShowBambiCloud);

            // Video Links
            stack.Children.Add(CreateSubHeader("Video Links"));
            stack.Children.Add(new TextBlock
            {
                Text = "Video name → URL pairs. The companion will suggest these videos and make them clickable in speech bubbles.",
                Foreground = new SolidColorBrush(Color.Parse("#606080")),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            _videoLinksPanel = new StackPanel();
            var linksScroll = new ScrollViewer
            {
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _videoLinksPanel,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(linksScroll);

            var addLinkBtn = new Button
            {
                Content = "+ Add Video Link",
                Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                Foreground = BrushFromHex(AccentColorHex),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushFromHex(AccentColorHex),
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addLinkBtn.Click += (_, _) => AddVideoLinkRow("", "");
            stack.Children.Add(addLinkBtn);
        }

        private void AddVideoLinkRow(string name, string url)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Real values, not hints - same correction as AddCustomAvatarSet.
            var nameBox = CreateDarkTextBox();
            nameBox.Text = name;
            nameBox.Tag = "VideoName";
            nameBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(nameBox, 0);
            row.Children.Add(nameBox);

            var urlBox = CreateDarkTextBox();
            urlBox.Text = url;
            urlBox.Tag = "VideoUrl";
            urlBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            urlBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255));
            Grid.SetColumn(urlBox, 2);
            row.Children.Add(urlBox);

            var removeBtn = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 10,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedRow = row;
            removeBtn.Click += (_, _) =>
            {
                _videoLinksPanel?.Children.Remove(capturedRow);
                _videoLinks.RemoveAll(v => v.Name == nameBox && v.Url == urlBox);
            };
            Grid.SetColumn(removeBtn, 3);
            row.Children.Add(removeBtn);

            _videoLinks.Add((nameBox, urlBox));
            _videoLinksPanel?.Children.Add(row);
        }

        // ─── Triggers Section ────────────────────────────────────
        private void BuildTriggersSection()
        {
            var panel = CreateSectionPanel("triggers");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Triggers"));
            stack.Children.Add(CreateSectionDescription("Text displayed as large fullscreen overlays during sessions. These appear before mandatory videos and during special events. Keep them short and punchy -- they're shown briefly in large centered text."));

            stack.Children.Add(CreateFieldLabel("Freeze Trigger"));
            _txtFreeze = CreateDarkTextBox("");
            _txtFreeze.Width = 350;
            stack.Children.Add(_txtFreeze);

            stack.Children.Add(CreateFieldLabel("Reset Trigger"));
            _txtReset = CreateDarkTextBox("");
            _txtReset.Width = 350;
            stack.Children.Add(_txtReset);

            stack.Children.Add(CreateFieldLabel("Cum & Collapse"));
            _txtCumCollapse = CreateDarkTextBox("");
            _txtCumCollapse.Width = 350;
            stack.Children.Add(_txtCumCollapse);

            stack.Children.Add(CreateFieldLabel("Autonomy On"));
            _txtAutonomyOn = CreateDarkTextBox("");
            _txtAutonomyOn.Width = 350;
            stack.Children.Add(_txtAutonomyOn);
        }

        // ─── Messages Section ────────────────────────────────────
        private void BuildMessagesSection()
        {
            var panel = CreateSectionPanel("messages");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Messages"));
            stack.Children.Add(CreateSectionDescription("System messages shown during minigames and attention checks. These appear as overlays when the user fails a video check or miscounts bubbles. Use \\n for line breaks."));

            stack.Children.Add(CreateFieldLabel("Attention Check Fail"));
            _txtAttentionFail = CreateDarkTextBox(multiline: true, height: 50);
            _txtAttentionFail.Width = 400;
            stack.Children.Add(_txtAttentionFail);

            stack.Children.Add(CreateFieldLabel("Attention Check Mercy"));
            _txtAttentionMercy = CreateDarkTextBox();
            _txtAttentionMercy.Width = 400;
            stack.Children.Add(_txtAttentionMercy);

            stack.Children.Add(CreateFieldLabel("Bubble Count Retry"));
            _txtBubbleRetry = CreateDarkTextBox(multiline: true, height: 50);
            _txtBubbleRetry.Width = 400;
            stack.Children.Add(_txtBubbleRetry);
        }

        // ─── Phrases Section ────────────────────────────────────
        private void BuildPhrasesSection()
        {
            var panel = CreateSectionPanel("phrases");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Phrases"));
            stack.Children.Add(CreateSectionDescription("What the companion says in speech bubbles during different situations. Each category triggers contextually -- Gaming/Browsing/Social fire based on the active window, FlashPre before showing images, LevelUp on rank-up, etc. Use {0} as a placeholder for the detected app/site name in activity categories. Empty categories fall back to defaults."));

            foreach (var cat in PhraseCategories)
            {
                var phraseList = new List<string>();
                _phraseData[cat] = phraseList;

                var phrasePanel = new StackPanel();
                _phrasePanels[cat] = phrasePanel;

                var expander = new Expander
                {
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 4),
                    IsExpanded = false,
                };

                var headerText = new TextBlock
                {
                    Text = $"{FormatCategoryName(cat)} (0 phrases)",
                    Foreground = new SolidColorBrush(Color.Parse("#C0C0C0")),
                    FontSize = 13,
                };
                expander.Header = headerText;

                var body = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };
                body.Children.Add(phrasePanel);

                var addBtn = new Button
                {
                    Content = "+ Add phrase",
                    Theme = (ControlTheme)this.FindResource("SecondaryButton")!,
                    Padding = new Thickness(10, 4, 10, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 0),
                    Tag = cat,
                };
                var capturedCat = cat;
                var capturedExpander = expander;
                addBtn.Click += (_, _) =>
                {
                    AddPhraseRow(capturedCat, "");
                    UpdatePhraseHeader(capturedCat, capturedExpander);
                };
                body.Children.Add(addBtn);

                expander.Content = body;
                expander.Tag = cat;

                stack.Children.Add(expander);
            }
        }

        private void AddPhraseRow(string category, string text)
        {
            if (!_phraseData.ContainsKey(category)) return;
            _phraseData[category].Add(text);

            var panel = _phrasePanels[category];
            var idx = _phraseData[category].Count - 1;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = text,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
            };
            var capturedIdx = idx;
            tb.TextChanged += (_, _) =>
            {
                if (capturedIdx < _phraseData[category].Count)
                    _phraseData[category][capturedIdx] = tb.Text ?? "";
            };
            Grid.SetColumn(tb, 0);
            row.Children.Add(tb);

            var delBtn = new Button
            {
                Content = "×",
                Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 16,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            delBtn.Click += (_, _) =>
            {
                var rowIndex = panel.Children.IndexOf(row);
                if (rowIndex >= 0 && rowIndex < _phraseData[category].Count)
                {
                    _phraseData[category].RemoveAt(rowIndex);
                    panel.Children.Remove(row);
                    // Find the parent Expander to update header
                    UpdatePhraseHeaderByCategory(category);
                }
            };
            Grid.SetColumn(delBtn, 1);
            row.Children.Add(delBtn);

            panel.Children.Add(row);
        }

        private void UpdatePhraseHeader(string category, Expander expander)
        {
            if (expander.Header is TextBlock tb)
                tb.Text = Loc.GetF("mod_phrase_header", FormatCategoryName(category), _phraseData[category].Count);
        }

        private void UpdatePhraseHeaderByCategory(string category)
        {
            // Walk ContentPanel to find the Expander for this category
            if (!_sectionPanels.TryGetValue("phrases", out var sectionBorder)) return;
            if (sectionBorder.Child is not StackPanel sectionStack) return;

            foreach (var child in sectionStack.Children)
            {
                if (child is Expander exp && exp.Tag is string cat && cat == category)
                {
                    UpdatePhraseHeader(category, exp);
                    break;
                }
            }
        }

        private static string FormatCategoryName(string cat)
        {
            // Insert spaces before uppercase letters (camelCase → human-readable)
            return Regex.Replace(cat, @"(?<=[a-z])([A-Z])", " $1");
        }

        // ─── Text Replacements Section ───────────────────────────
        private void BuildReplacementsSection()
        {
            var panel = CreateSectionPanel("replacements");
            var stack = new StackPanel();
            panel.Child = stack;

            stack.Children.Add(CreateSectionHeader("Text Replacements"));
            stack.Children.Add(CreateSectionDescription("Find-and-replace pairs applied across most of the UI - dashboard feature tiles and their popup titles, section headers, button labels, achievement and skill names, quest descriptions, companion speech, tab headers, and more. Matching runs against the app's original English wording and is case-sensitive, so \"Flash Images\" matches but \"flash images\" does not. Longer match strings are applied first to prevent partial replacements. Coverage is broad but not total - a few screens still show the stock wording. This is the most powerful theming tool for re-skinning the app's vocabulary."));

            _replacementsPanel = new StackPanel();
            stack.Children.Add(_replacementsPanel);

            var addBtn = new Button
            {
                Content = "+ Add replacement",
                Theme = (ControlTheme)this.FindResource("SecondaryButton")!,
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
            };
            addBtn.Click += (_, _) => AddReplacementRow("", "");
            stack.Children.Add(addBtn);
        }

        private void AddReplacementRow(string fromText, string toText)
        {
            if (_replacementsPanel == null) return;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var fromBox = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = fromText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
            };
            Grid.SetColumn(fromBox, 0);
            row.Children.Add(fromBox);

            var arrow = new TextBlock
            {
                Text = "→",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(arrow);

            var toBox = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = toText,
                FontSize = 12,
                Padding = new Thickness(6, 4, 6, 4),
            };
            Grid.SetColumn(toBox, 2);
            row.Children.Add(toBox);

            var delBtn = new Button
            {
                Content = "×",
                Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 16,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            delBtn.Click += (_, _) =>
            {
                var idx = _replacementsPanel!.Children.IndexOf(row);
                if (idx >= 0)
                {
                    _replacementsPanel.Children.Remove(row);
                    if (idx < _textReplacements.Count)
                        _textReplacements.RemoveAt(idx);
                }
            };
            Grid.SetColumn(delBtn, 3);
            row.Children.Add(delBtn);

            _replacementsPanel.Children.Add(row);
            _textReplacements.Add((fromBox, toBox));
        }

        // ─── Image Slot Helper ───────────────────────────────────
        private StackPanel CreateImageSlot(string resourceKey, string displayName, double width = 100, double height = 100)
        {
            _imageSlots[resourceKey] = null;
            _imageNames[resourceKey] = displayName;

            var container = new StackPanel
            {
                Width = width + 20,
                Margin = new Thickness(4),
            };

            var borderHolder = new Grid { Width = width, Height = height };

            // Hint image (dimmed default). WPF called ModResourceResolver.ResolveImage, which
            // cannot move to Core - it decodes to a WPF ImageSource. The portable half is
            // CoreModArt.OverridePath plus this head's own avares:// copy.
            var hintImage = new Image
            {
                Opacity = 0.2,
                Stretch = Stretch.Uniform,
                IsHitTestVisible = false,
                Source = TryLoadSlotHint(resourceKey),
            };
            borderHolder.Children.Add(hintImage);

            // Main image (custom loaded)
            var mainImage = new Image
            {
                Stretch = Stretch.Uniform,
                IsVisible = false,
                IsHitTestVisible = false,
            };
            _imageControls[resourceKey] = mainImage;
            borderHolder.Children.Add(mainImage);

            // "+" placeholder text
            var plusText = new TextBlock
            {
                Text = "+",
                Foreground = new SolidColorBrush(Color.Parse("#505070")),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            borderHolder.Children.Add(plusText);

            // Clear button
            var clearBtn = new Button
            {
                Content = "×",
                Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")),
                Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 50)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Width = 18,
                Height = 18,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 2, 0),
                IsVisible = false,
                Padding = new Thickness(0),
            };
            clearBtn.Click += (_, _) => ClearImageSlot(resourceKey);
            borderHolder.Children.Add(clearBtn);

            // ponytail: needs ModCreatorWindow.ArtFraming.cs for the "Frame" affordance, which the
            // WPF head adds here for slots whose path paints a framed surface. SlotIsFramable
            // returns false until that layer lands, so nothing is added.

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252542")),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
                BorderThickness = new Thickness(1),
                Width = width,
                Height = height,
                ClipToBounds = true,
                Child = borderHolder,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            border.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                    BrowseImageForSlot(resourceKey);
            };
            // WPF's AllowDrop + DragOver + Drop. Avalonia routes both through DragDrop's attached
            // events; in 12.x the payload is an IDataTransfer of IStorageItem, not a string[].
            DragDrop.SetAllowDrop(border, true);
            border.AddHandler(DragDrop.DragOverEvent, (object? _, DragEventArgs e) =>
            {
                e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            border.AddHandler(DragDrop.DropEvent, (object? _, DragEventArgs e) => HandleImageDrop(resourceKey, e));

            _imageSlotParts[resourceKey] = (clearBtn, plusText, hintImage);

            container.Children.Add(border);

            // Filename label
            var filename = Path.GetFileName(resourceKey);
            container.Children.Add(new TextBlock
            {
                Text = filename,
                Foreground = new SolidColorBrush(Color.Parse("#606080")),
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = width + 10,
            });

            // Editable name TextBox
            var nameBox = new TextBox
            {
                Theme = DarkTextBoxTheme,
                Text = displayName,
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = width + 10,
                TextAlignment = TextAlignment.Center,
            };
            nameBox.TextChanged += (_, _) => _imageNames[resourceKey] = nameBox.Text ?? "";
            _imageNameBoxes[resourceKey] = nameBox;
            container.Children.Add(nameBox);

            return container;
        }

        /// <summary>
        /// The clear button, "+" glyph and hint image of each slot. WPF stuffed this tuple into
        /// border.Tag and pattern-matched it back out; a dictionary says the same thing without
        /// the cast, and Tag stays free.
        /// </summary>
        /// <summary>
        /// The mod's override first (<see cref="CoreModArt"/>), then this head's own shipped copy
        /// under <c>avares://</c>. Null when neither exists, which is what an unhinted slot looked
        /// like in the WPF head too - the head deliberately ships only a slice of achievements/
        /// and skills/, so most badge slots legitimately have no hint here.
        ///
        /// ponytail: a byte-for-byte twin of TubeFitDialog.TryLoadImage, whose own note asks for a
        /// head-wide helper once a second view wants the two-step. This is that second view, but
        /// hoisting it means editing a file this layer does not own; do it in the layer that owns
        /// both.
        /// </summary>
        private static Bitmap? TryLoadSlotHint(string resourceName)
        {
            var overridePath = CoreModArt.OverridePath(resourceName);
            if (overridePath != null)
            {
                try { if (File.Exists(overridePath)) return new Bitmap(overridePath); }
                catch (Exception ex) { Log.Warning(ex, "[ModCreator] mod override {Path} would not load", overridePath); }
            }

            try
            {
                var uri = new Uri($"avares://CCP.Avalonia/Resources/{resourceName}");
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ModCreator] built-in {Name} would not load", resourceName);
                return null;
            }
        }

        private readonly Dictionary<string, (Button Clear, TextBlock Plus, Image Hint)> _imageSlotParts = new();

        private void SetImageSlot(string key, string filePath, bool validate = true)
        {
            if (!_imageControls.ContainsKey(key)) return;

            if (validate && !PassesImageSlotChecks(key, filePath)) return;

            // A framing describes ONE picture. An author swapping a different file into the same
            // slot must not inherit the crop they chose for the old one -- that is invisible in the
            // editor and only shows up as a mis-framed chip in the app. Programmatic fills are
            // exempt: the load path reads artFraming out of the manifest and THEN fills the slots,
            // so dropping here would wipe exactly what it just read.
            if (validate && _imageSlots.TryGetValue(key, out var previous)
                && !string.Equals(previous, filePath, StringComparison.OrdinalIgnoreCase))
                DropArtFraming(key);

            try
            {
                // WPF's BitmapImage + DecodePixelWidth = 200, which is what keeps a wall of slots
                // from decoding full-size art.
                using var stream = File.OpenRead(filePath);
                var bitmap = Bitmap.DecodeToWidth(stream, 200);

                _imageControls[key].Source = bitmap;
                _imageControls[key].IsVisible = true;
                _imageSlots[key] = filePath;

                if (_imageSlotParts.TryGetValue(key, out var parts))
                {
                    parts.Clear.IsVisible = true;
                    parts.Plus.IsVisible = false;
                    parts.Hint.Opacity = 0;
                }

                UpdateStatusBar();
            }
            catch { /* invalid image file */ }
        }

        /// <summary>
        /// ponytail: needs ModImageSlotRules (ModCreatorWindow.UiArt.cs) plus a message box, wired
        /// when that layer lands. The WPF gate hard-rejects a file whose format does not match the
        /// slot's filename, then asks before accepting anything oversized.
        /// </summary>
        private bool PassesImageSlotChecks(string key, string filePath) => true;

        /// <summary>ponytail: needs ModCreatorWindow.ArtFraming.cs - drops the crop stored for a
        /// slot whose image just changed or was cleared.</summary>
        private void DropArtFraming(string key) { }

        private void ClearImageSlot(string key)
        {
            if (!_imageControls.ContainsKey(key)) return;

            _imageControls[key].Source = null;
            _imageControls[key].IsVisible = false;
            _imageSlots[key] = null;

            // Framing goes with the image. Left behind, it would silently re-attach itself to
            // whatever the author picks next -- and to a manifest that no longer ships the file it
            // was measured against.
            DropArtFraming(key);

            if (_imageSlotParts.TryGetValue(key, out var parts))
            {
                parts.Clear.IsVisible = false;
                parts.Plus.IsVisible = true;
                parts.Hint.Opacity = 0.2;
            }

            UpdateStatusBar();
        }

        private async void BrowseImageForSlot(string key)
        {
            // Lead with the slot's own extension: it is the only one SetImageSlot accepts, so
            // offering "all images" first just walks the author into the rejection dialog.
            var ext = Path.GetExtension(key);
            var patterns = string.IsNullOrEmpty(ext)
                ? new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" }
                : new[] { "*" + ext };

            var files = await PickFilesAsync($"Select image for {Path.GetFileName(key)}", patterns, multiple: false);
            if (files.Count > 0)
                SetImageSlot(key, files[0]);
        }

        private void HandleImageDrop(string key, DragEventArgs e)
        {
            var file = e.DataTransfer.TryGetValue(DataFormat.File)?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(file))
                SetImageSlot(key, file!);
        }

        /// <summary>
        /// WPF's OpenFileDialog (Microsoft.Win32) has no place in a cross-platform head.
        /// StorageProvider is the Avalonia-native equivalent and needs no package.
        /// </summary>
        private async Task<IReadOnlyList<string>> PickFilesAsync(string title, string[] patterns, bool multiple)
        {
            var top = GetTopLevel(this);
            if (top == null) return Array.Empty<string>();

            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = multiple,
                FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = patterns } },
            });

            return picked.Select(f => f.TryGetLocalPath())
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Select(p => p!)
                         .ToList();
        }

        // ─── Color Picker ────────────────────────────────────────
        /// <summary>
        /// WPF opened System.Windows.Forms.ColorDialog. Views/Dialogs/ColorPickerDialog is the
        /// cross-platform twin and returns a Color? with the same "null means Cancel" contract,
        /// so the hex box is left exactly as it was on Cancel. Alpha is dropped, as WPF did.
        /// </summary>
        private async Task<string?> ShowColorPickerAsync(string currentHex, string title)
        {
            TryParseHex(currentHex, out var initial);
            var picked = await ColorPickerDialog.PickAsync(this, initial, title);
            return picked is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
        }

        // ─── Populate Defaults ───────────────────────────────────
        private void PopulateDefaults()
        {
            // New mods start empty — no pre-filled content from the base mod.
            // Users fill in their own identity, triggers, messages, and phrases.
        }

        /// <summary>
        /// Auto-loads the currently active mod's manifest and resources as a starting preset.
        /// Skips built-in mods, which have no installed path.
        ///
        /// <para>WPF read App.Mods.ActiveMod; CoreMods answers the same question on every head.
        /// With no mod layer seeded (this head today) the lookup lands on the built-in CCP
        /// default and returns immediately, so constructing the window touches no disk.</para>
        /// </summary>
        private void LoadActiveModAsPreset()
        {
            try
            {
                var mods = CoreMods.InstalledMods;
                if (!mods.TryGetValue(CoreMods.ActiveModId, out var activeMod)) return;
                if (activeMod.IsBuiltIn || string.IsNullOrEmpty(activeMod.InstalledPath)) return;

                var manifestPath = Path.Combine(activeMod.InstalledPath!, "mod.json");
                if (!File.Exists(manifestPath)) return;

                var manifest = JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(manifestPath));
                if (manifest == null) return;

                PopulateFromManifest(manifest);
                LoadResourcesFrom(Path.Combine(activeMod.InstalledPath!, "resources"));

                _txtStatus.Text = Loc.GetF("mod_loaded_active", manifest.Name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-load active mod as preset");
            }
        }

        /// <summary>
        /// Refills the image slots, the audio slots and the panel partials' side files from an
        /// extracted resources tree. Shared by the active-mod preset and by Load; the WPF head
        /// carried the same eight lines in both.
        /// </summary>
        private void LoadResourcesFrom(string resourcesDir)
        {
            if (!Directory.Exists(resourcesDir)) return;

            foreach (var key in _imageSlots.Keys.ToList())
            {
                var imgPath = Path.Combine(resourcesDir, key.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(imgPath))
                    SetImageSlot(key, imgPath, validate: false);
            }
            LoadAudioFromResources(resourcesDir);
            LoadSideFilesFrom(resourcesDir);
        }

        // ─── Populate From Manifest ──────────────────────────────
        private void PopulateFromManifest(ModManifest manifest)
        {
            // Info
            SetTextBoxValue(_txtModName, manifest.Name);
            SetTextBoxValue(_txtAuthor, manifest.Author);
            SetTextBoxValue(_txtVersion, manifest.Version);
            SetTextBoxValue(_txtDescription, manifest.Description);
            SetTextBoxValue(_txtTags, manifest.Tags != null ? string.Join(", ", manifest.Tags) : "");
            SetTextBoxValue(_txtMinAppVersion, manifest.MinAppVersion);

            // Theme
            if (manifest.Theme != null)
            {
                if (_txtAccentHex != null) _txtAccentHex.Text = manifest.Theme.AccentColor ?? "#FF69B4";
                if (_txtLightHex != null) _txtLightHex.Text = manifest.Theme.AccentLightColor ?? "#FFB6C1";
                if (_txtDarkHex != null) _txtDarkHex.Text = manifest.Theme.AccentDarkColor ?? "#FF1493";
                if (_txtBgHex != null) _txtBgHex.Text = manifest.Theme.BackgroundColor ?? "#1A1A2E";
                if (_txtPanelHex != null) _txtPanelHex.Text = manifest.Theme.PanelColor ?? "#252542";
                if (_txtSurfaceHex != null) _txtSurfaceHex.Text = manifest.Theme.SurfaceColor ?? "#1E1E3A";
                if (_txtFilterHex != null) _txtFilterHex.Text = manifest.Theme.FilterColor ?? "#FF69B4";
            }

            // FX palette
            if (_txtMistHex != null) _txtMistHex.Text = manifest.FxPalette?.MistColor ?? FxRowDefault;
            if (_txtParticleHex != null) _txtParticleHex.Text = manifest.FxPalette?.ParticleColor ?? FxRowDefault;
            if (_txtGlowHex != null) _txtGlowHex.Text = manifest.FxPalette?.GlowColor ?? FxRowDefault;
            if (_txtFlashTintHex != null) _txtFlashTintHex.Text = manifest.FxPalette?.FlashTint ?? FxRowDefault;

            // Identity
            if (manifest.Identity != null)
            {
                SetTextBoxValue(_txtCompanionName, manifest.Identity.CompanionName);
                SetTextBoxValue(_txtUserTerm, manifest.Identity.UserTerm);
                SetTextBoxValue(_txtModeDisplayName, manifest.Identity.ModeDisplayName);
                SetTextBoxValue(_txtTalkToLabel, manifest.Identity.TalkToLabel);
                SetTextBoxValue(_txtTakeoverLabel, manifest.Identity.TakeoverLabel);
                SetTextBoxValue(_txtAffirmation, manifest.Identity.Affirmation);
                SetTextBoxValue(_txtRankSubject, manifest.Identity.RankSubject);
            }

            // Triggers
            if (manifest.Triggers != null)
            {
                SetTextBoxValue(_txtFreeze, manifest.Triggers.Freeze);
                SetTextBoxValue(_txtReset, manifest.Triggers.Reset);
                SetTextBoxValue(_txtCumCollapse, manifest.Triggers.CumAndCollapse);
                SetTextBoxValue(_txtAutonomyOn, manifest.Triggers.AutonomyOn);
            }

            // Messages
            if (manifest.Messages != null)
            {
                SetTextBoxValue(_txtAttentionFail, manifest.Messages.AttentionCheckFail);
                SetTextBoxValue(_txtAttentionMercy, manifest.Messages.AttentionCheckMercy);
                SetTextBoxValue(_txtBubbleRetry, manifest.Messages.BubbleCountRetry);
            }

            // Phrases
            if (manifest.Phrases != null)
            {
                foreach (var (cat, phrases) in manifest.Phrases)
                {
                    if (!_phraseData.ContainsKey(cat)) continue;
                    _phraseData[cat].Clear();
                    _phrasePanels[cat].Children.Clear();

                    foreach (var phrase in phrases)
                        AddPhraseRow(cat, phrase);

                    UpdatePhraseHeaderByCategory(cat);
                }
            }

            // Browser
            if (manifest.Browser != null)
            {
                SetTextBoxValue(_txtBrowserUrl, manifest.Browser.DefaultUrl);
                SetTextBoxValue(_txtBrowserSiteName, manifest.Browser.SiteName);
                if (_chkShowBambiCloud != null)
                    _chkShowBambiCloud.IsChecked = manifest.Browser.ShowBambiCloudOption ?? true;
                if (manifest.Browser.DefaultVideoLinks != null)
                {
                    _videoLinks.Clear();
                    _videoLinksPanel?.Children.Clear();
                    foreach (var (vName, vUrl) in manifest.Browser.DefaultVideoLinks)
                        AddVideoLinkRow(vName, vUrl);
                }
            }

            // Text Replacements
            if (manifest.TextReplacements != null)
            {
                _textReplacements.Clear();
                _replacementsPanel?.Children.Clear();

                foreach (var (from, to) in manifest.TextReplacements)
                    AddReplacementRow(from, to);
            }

            // Supported avatar sets — uncheck sets not in the list
            if (manifest.SupportedAvatarSets != null)
            {
                foreach (var (setNum, cb) in _avatarSetCheckboxes)
                {
                    var supported = manifest.SupportedAvatarSets.Contains(setNum);
                    cb.IsChecked = supported;
                    ToggleAvatarSet(setNum, supported);
                }
            }

            // Custom avatar sets
            if (manifest.CustomAvatarSets != null)
            {
                foreach (var cs in manifest.CustomAvatarSets)
                    AddCustomAvatarSet(cs.SetNumber, cs.Label, cs.UnlockLevel);
            }

            // Manifest sections owned by the panel partials. Art framing is read here, BEFORE the
            // callers go on to fill the image slots from resources/, which is the order that keeps
            // it: SetImageSlot only discards framing for an author-picked swap, not a bulk load.
            PopulatePanelSectionsFromManifest(manifest);

            UpdateStatusBar();
        }

        // ─── Build Manifest From Form ────────────────────────────
        private ModManifest BuildManifestFromForm()
        {
            var name = GetTextBoxValue(_txtModName);
            var manifest = new ModManifest
            {
                Id = SanitizeModId(name),
                Name = name,
                Version = GetTextBoxValue(_txtVersion),
                Author = GetTextBoxValue(_txtAuthor),
                Description = string.IsNullOrWhiteSpace(GetTextBoxValue(_txtDescription)) ? null : GetTextBoxValue(_txtDescription),
            };

            // Tags + minimum app version
            var tags = GetTextBoxValue(_txtTags)
                .Split(new[] { ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            if (tags.Count > 0) manifest.Tags = tags;
            var minVer = GetTextBoxValue(_txtMinAppVersion).Trim();
            if (Version.TryParse(minVer, out _)) manifest.MinAppVersion = minVer;

            // Preview image
            if (_imageSlots.TryGetValue("preview", out var previewPath) && previewPath != null)
                manifest.PreviewImage = "resources/preview" + Path.GetExtension(previewPath);

            // Theme
            var accent = (_txtAccentHex?.Text ?? "#FF69B4").Trim();
            var light = (_txtLightHex?.Text ?? "#FFB6C1").Trim();
            var dark = (_txtDarkHex?.Text ?? "#FF1493").Trim();
            var bg = (_txtBgHex?.Text ?? "#1A1A2E").Trim();
            var panel = (_txtPanelHex?.Text ?? "#252542").Trim();
            var surface = (_txtSurfaceHex?.Text ?? "#1E1E3A").Trim();
            var filter = (_txtFilterHex?.Text ?? "#FF69B4").Trim();
            if (accent != "#FF69B4" || light != "#FFB6C1" || dark != "#FF1493"
                || bg != "#1A1A2E" || panel != "#252542" || surface != "#1E1E3A"
                || filter != accent)
            {
                manifest.Theme = new ModTheme
                {
                    AccentColor = accent,
                    AccentLightColor = light,
                    AccentDarkColor = dark,
                    BackgroundColor = bg != "#1A1A2E" ? bg : null,
                    PanelColor = panel != "#252542" ? panel : null,
                    SurfaceColor = surface != "#1E1E3A" ? surface : null,
                    FilterColor = filter != accent ? filter : null,
                };
            }

            // FX palette — only the rows the creator actually moved off the inherit default.
            var mist = (_txtMistHex?.Text ?? FxRowDefault).Trim();
            var particle = (_txtParticleHex?.Text ?? FxRowDefault).Trim();
            var glow = (_txtGlowHex?.Text ?? FxRowDefault).Trim();
            var flashTint = (_txtFlashTintHex?.Text ?? FxRowDefault).Trim();
            if (mist != FxRowDefault || particle != FxRowDefault
                || glow != FxRowDefault || flashTint != FxRowDefault)
            {
                manifest.FxPalette = new ModFxPalette
                {
                    MistColor = mist != FxRowDefault ? mist : null,
                    ParticleColor = particle != FxRowDefault ? particle : null,
                    GlowColor = glow != FxRowDefault ? glow : null,
                    FlashTint = flashTint != FxRowDefault ? flashTint : null,
                };
            }

            // Identity
            var cn = GetTextBoxValue(_txtCompanionName);
            var ut = GetTextBoxValue(_txtUserTerm);
            var mdn = GetTextBoxValue(_txtModeDisplayName);
            var ttl = GetTextBoxValue(_txtTalkToLabel);
            var tol = GetTextBoxValue(_txtTakeoverLabel);
            var aff = GetTextBoxValue(_txtAffirmation);
            var rank = GetTextBoxValue(_txtRankSubject);
            if (!string.IsNullOrEmpty(cn) || !string.IsNullOrEmpty(ut) || !string.IsNullOrEmpty(mdn)
                || !string.IsNullOrEmpty(ttl) || !string.IsNullOrEmpty(tol)
                || !string.IsNullOrEmpty(aff) || !string.IsNullOrEmpty(rank))
            {
                manifest.Identity = new ModIdentity
                {
                    CompanionName = string.IsNullOrEmpty(cn) ? null : cn,
                    UserTerm = string.IsNullOrEmpty(ut) ? null : ut,
                    ModeDisplayName = string.IsNullOrEmpty(mdn) ? null : mdn,
                    TalkToLabel = string.IsNullOrEmpty(ttl) ? null : ttl,
                    TakeoverLabel = string.IsNullOrEmpty(tol) ? null : tol,
                    Affirmation = string.IsNullOrEmpty(aff) ? null : aff,
                    RankSubject = string.IsNullOrEmpty(rank) ? null : rank,
                };
            }

            // Triggers
            var freeze = GetTextBoxValue(_txtFreeze);
            var reset = GetTextBoxValue(_txtReset);
            var cum = GetTextBoxValue(_txtCumCollapse);
            var auto = GetTextBoxValue(_txtAutonomyOn);
            if (!string.IsNullOrEmpty(freeze) || !string.IsNullOrEmpty(reset)
                || !string.IsNullOrEmpty(cum) || !string.IsNullOrEmpty(auto))
            {
                manifest.Triggers = new ModTriggers
                {
                    Freeze = string.IsNullOrEmpty(freeze) ? null : freeze,
                    Reset = string.IsNullOrEmpty(reset) ? null : reset,
                    CumAndCollapse = string.IsNullOrEmpty(cum) ? null : cum,
                    AutonomyOn = string.IsNullOrEmpty(auto) ? null : auto,
                };
            }

            // Messages
            var af = GetTextBoxValue(_txtAttentionFail);
            var am = GetTextBoxValue(_txtAttentionMercy);
            var br = GetTextBoxValue(_txtBubbleRetry);
            if (!string.IsNullOrEmpty(af) || !string.IsNullOrEmpty(am) || !string.IsNullOrEmpty(br))
            {
                manifest.Messages = new ModMessages
                {
                    AttentionCheckFail = string.IsNullOrEmpty(af) ? null : af,
                    AttentionCheckMercy = string.IsNullOrEmpty(am) ? null : am,
                    BubbleCountRetry = string.IsNullOrEmpty(br) ? null : br,
                };
            }

            // Phrases — include all categories that have content
            var phrases = new Dictionary<string, string[]>();
            foreach (var (cat, list) in _phraseData)
            {
                var filtered = list.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                if (filtered.Length > 0)
                    phrases[cat] = filtered;
            }
            if (phrases.Count > 0)
                manifest.Phrases = phrases;

            // Text Replacements
            var replacements = new Dictionary<string, string>();
            foreach (var (fromBox, toBox) in _textReplacements)
            {
                var from = (fromBox.Text ?? "").Trim();
                var to = (toBox.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(from) && !replacements.ContainsKey(from))
                    replacements[from] = to;
            }
            if (replacements.Count > 0)
                manifest.TextReplacements = replacements;

            // Browser
            var browserUrl = GetTextBoxValue(_txtBrowserUrl);
            var siteName = GetTextBoxValue(_txtBrowserSiteName);
            var showBambi = _chkShowBambiCloud?.IsChecked;
            var vidLinks = new Dictionary<string, string>();
            foreach (var (nameBox, urlBox) in _videoLinks)
            {
                var vName = (nameBox.Text ?? "").Trim();
                var vUrl = (urlBox.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(vName) && !string.IsNullOrEmpty(vUrl) && !vidLinks.ContainsKey(vName))
                    vidLinks[vName] = vUrl;
            }
            if (!string.IsNullOrEmpty(browserUrl) || !string.IsNullOrEmpty(siteName)
                || showBambi == false || vidLinks.Count > 0)
            {
                manifest.Browser = new ModBrowser
                {
                    DefaultUrl = string.IsNullOrEmpty(browserUrl) ? null : browserUrl,
                    SiteName = string.IsNullOrEmpty(siteName) ? null : siteName,
                    ShowBambiCloudOption = showBambi == false ? false : null,
                    DefaultVideoLinks = vidLinks.Count > 0 ? vidLinks : null,
                };
            }

            // Supported avatar sets — only write if some are unchecked
            var enabledSets = _avatarSetCheckboxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .OrderBy(x => x)
                .ToList();
            // Also include custom set numbers
            foreach (var cs in _customAvatarSets)
                enabledSets.Add(cs.SetNum);
            if (enabledSets.Count < _avatarSetCheckboxes.Count + _customAvatarSets.Count || _customAvatarSets.Count > 0)
                manifest.SupportedAvatarSets = enabledSets.Distinct().OrderBy(x => x).ToList();

            // Custom avatar sets
            if (_customAvatarSets.Count > 0)
            {
                manifest.CustomAvatarSets = _customAvatarSets.Select(cs => new CustomAvatarSet
                {
                    SetNumber = cs.SetNum,
                    Label = (cs.LabelBox.Text ?? "").Trim(),
                    UnlockLevel = int.TryParse((cs.LevelBox.Text ?? "").Trim(), out var lv) ? lv : 200
                }).ToList();
            }

            // Manifest sections owned by the panel partials.
            ApplyPanelSectionsToManifest(manifest);

            return manifest;
        }

        // ─── Export ──────────────────────────────────────────────
        private async void BtnExport_Click()
        {
            // Validate required fields
            var name = GetTextBoxValue(_txtModName);
            var author = GetTextBoxValue(_txtAuthor);
            if (string.IsNullOrWhiteSpace(name))
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_validation_error"), Loc.Get("msg_mod_name_is_required"));
                NavigateToSection("info");
                _txtModName?.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(author))
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_validation_error"), Loc.Get("msg_author_is_required"));
                NavigateToSection("info");
                _txtAuthor?.Focus();
                return;
            }

            var manifest = BuildManifestFromForm();

            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Mod Package",
                SuggestedFileName = $"{manifest.Id}.ccpmod",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CCP Mod Files") { Patterns = new[] { "*.ccpmod" } },
                },
            });
            var savePath = target?.TryGetLocalPath();
            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), $"ccpmod_export_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                var resourcesDir = Path.Combine(tempDir, "resources");
                Directory.CreateDirectory(resourcesDir);

                // Write manifest
                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                File.WriteAllText(Path.Combine(tempDir, "mod.json"), json);

                // Copy filled image slots, then filled audio slots
                foreach (var (key, filePath) in _imageSlots)
                    if (filePath != null) CopyIntoResources(resourcesDir, key, filePath);
                foreach (var (key, audioPath) in _audioSlots)
                    if (audioPath != null) CopyIntoResources(resourcesDir, key, audioPath);

                // Copy voice line files
                if (_voiceLines.Count > 0)
                {
                    var voiceDir = Path.Combine(resourcesDir, "sounds", "flashes_audio");
                    Directory.CreateDirectory(voiceDir);
                    foreach (var (srcPath, _) in _voiceLines)
                    {
                        if (!File.Exists(srcPath)) continue;
                        File.Copy(srcPath, Path.Combine(voiceDir, Path.GetFileName(srcPath)), overwrite: true);
                    }
                }

                // Barks / mantras / event audio / portraits / emotes.
                WriteSideFilesTo(resourcesDir);

                // Create ZIP
                if (File.Exists(savePath)) File.Delete(savePath!);
                ZipFile.CreateFromDirectory(tempDir, savePath!);

                // Cleanup temp
                try { Directory.Delete(tempDir, recursive: true); } catch { }

                _txtStatus.Text = Loc.GetF("mod_exported_filename", Path.GetFileName(savePath!));
                await MessageDialog.ShowAsync(this, Loc.Get("title_export_complete"),
                    Loc.GetF("msg_mod_exported_successfully", savePath!));
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_export_error"), Loc.GetF("msg_export_failed", ex.Message));
            }
        }

        private static void CopyIntoResources(string resourcesDir, string resourceKey, string sourcePath)
        {
            var destPath = Path.Combine(resourcesDir, resourceKey.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null) Directory.CreateDirectory(destDir);
            File.Copy(sourcePath, destPath, overwrite: true);
        }

        // ─── Load ────────────────────────────────────────────────
        private async void BtnLoad_Click()
        {
            var picked = await PickFilesAsync("Load Mod Package", new[] { "*.ccpmod" }, multiple: false);
            if (picked.Count == 0) return;

            try
            {
                CleanupTempDir();
                _loadedTempDir = Path.Combine(Path.GetTempPath(), $"ccpmod_load_{Guid.NewGuid():N}");
                ZipFile.ExtractToDirectory(picked[0], _loadedTempDir);

                var manifestPath = Path.Combine(_loadedTempDir, "mod.json");
                if (!File.Exists(manifestPath))
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("title_load_error"),
                        Loc.Get("msg_invalid_mod_package_mod_json_not_found"));
                    CleanupTempDir();
                    return;
                }

                var manifest = JsonConvert.DeserializeObject<ModManifest>(File.ReadAllText(manifestPath));
                if (manifest == null)
                {
                    await MessageDialog.ShowAsync(this, Loc.Get("title_load_error"), Loc.Get("msg_failed_to_parse_mod_json"));
                    CleanupTempDir();
                    return;
                }

                // Clear all image slots first
                foreach (var key in _imageSlots.Keys.ToList())
                    ClearImageSlot(key);
                ClearSideFileState();

                PopulateFromManifest(manifest);
                LoadResourcesFrom(Path.Combine(_loadedTempDir, "resources"));

                NavigateToSection("info");
                _txtStatus.Text = Loc.GetF("mod_loaded", manifest.Name);
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_load_error"), Loc.GetF("msg_load_failed", ex.Message));
            }
        }

        // ─── Reset ───────────────────────────────────────────────
        private async void BtnReset_Click()
        {
            var confirmed = await MessageDialog.ConfirmAsync(this, Loc.Get("title_confirm_reset"),
                Loc.Get("msg_reset_all_fields_to_defaults_this_cannot_be_u"));
            if (!confirmed) return;

            // Clear all fields
            SetTextBoxValue(_txtModName, "");
            SetTextBoxValue(_txtAuthor, "");
            if (_txtVersion != null) _txtVersion.Text = "1.0.0";
            SetTextBoxValue(_txtDescription, "");

            // Reset theme
            if (_txtAccentHex != null) _txtAccentHex.Text = "#FF69B4";
            if (_txtLightHex != null) _txtLightHex.Text = "#FFB6C1";
            if (_txtDarkHex != null) _txtDarkHex.Text = "#FF1493";
            if (_txtBgHex != null) _txtBgHex.Text = "#1A1A2E";
            if (_txtPanelHex != null) _txtPanelHex.Text = "#252542";
            if (_txtSurfaceHex != null) _txtSurfaceHex.Text = "#1E1E3A";
            if (_txtFilterHex != null) _txtFilterHex.Text = "#FF69B4";

            // Clear identity
            SetTextBoxValue(_txtCompanionName, "");
            SetTextBoxValue(_txtUserTerm, "");
            SetTextBoxValue(_txtModeDisplayName, "");
            SetTextBoxValue(_txtTalkToLabel, "");
            SetTextBoxValue(_txtTakeoverLabel, "");

            // Clear triggers
            SetTextBoxValue(_txtFreeze, "");
            SetTextBoxValue(_txtReset, "");
            SetTextBoxValue(_txtCumCollapse, "");
            SetTextBoxValue(_txtAutonomyOn, "");

            // Clear all image slots
            foreach (var key in _imageSlots.Keys.ToList())
                ClearImageSlot(key);

            // Clear replacements
            _textReplacements.Clear();
            _replacementsPanel?.Children.Clear();

            // Clear all phrases
            foreach (var (cat, phrases) in _phraseData)
            {
                phrases.Clear();
                if (_phrasePanels.TryGetValue(cat, out var panel))
                    panel.Children.Clear();
                UpdatePhraseHeaderByCategory(cat);
            }

            // Clear audio
            StopAudioPreview();
            foreach (var key in _audioSlots.Keys.ToList())
                ClearAudioSlot(key);
            _voiceLines.Clear();
            _voiceLinesPanel?.Children.Clear();

            // New metadata fields + panel partials
            SetTextBoxValue(_txtTags, "");
            SetTextBoxValue(_txtMinAppVersion, "");
            SetTextBoxValue(_txtAffirmation, "");
            SetTextBoxValue(_txtRankSubject, "");
            ClearSideFileState();

            NavigateToSection("info");
            UpdateStatusBar();
            _txtStatus.Text = Loc.Get("label_reset_to_defaults");
        }

        // ─── Status Bar ──────────────────────────────────────────
        private void UpdateStatusBar()
        {
            var filled = _imageSlots.Count(kv => kv.Value != null);
            var total = _imageSlots.Count;
            var audioFilled = _audioSlots.Count(kv => kv.Value != null);
            var phraseCount = _phraseData.Values.Sum(l => l.Count(p => !string.IsNullOrWhiteSpace(p)));
            _txtStatus.Text = Loc.GetF("mod_status_bar", filled, total, audioFilled, _voiceLines.Count, phraseCount, _textReplacements.Count);
        }

        // ─── Helpers ─────────────────────────────────────────────
        private static string SanitizeModId(string name)
        {
            var id = name.ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9\-]", "-");
            id = Regex.Replace(id, @"-+", "-");
            id = id.Trim('-');
            if (string.IsNullOrEmpty(id)) id = "custom-mod";
            return id;
        }

        private static bool TryParseHex(string hex, out Color color)
        {
            color = Colors.HotPink;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.TryParse(hex, out color);
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            if (TryParseHex(hex, out var c))
                return new SolidColorBrush(c);
            return new SolidColorBrush(Colors.HotPink);
        }

        /// <summary>Where the last loaded .ccpmod was unzipped, so the window can delete it on
        /// the next load and on close.</summary>
        private string? _loadedTempDir;

        private void CleanupTempDir()
        {
            if (string.IsNullOrEmpty(_loadedTempDir)) return;
            try { if (Directory.Exists(_loadedTempDir)) Directory.Delete(_loadedTempDir, recursive: true); } catch { }
            _loadedTempDir = null;
        }
    }
}
