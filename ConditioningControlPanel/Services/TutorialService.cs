using System;
using System.Collections.Generic;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Types of tutorials available in the app
    /// </summary>
    public enum TutorialType
    {
        FullTour,       // Complete app tour (original behavior)
        GettingStarted, // Quick overview
        Settings,       // Settings tab features
        Presets,        // Presets tab
        Progression,    // Progression tab
        Achievements,   // Achievements tab
        Companion,      // Companion tab
        Patreon,        // Patreon exclusives tab
        Avatar,         // Avatar companion
        Modding,        // Mod creation guide
        Awareness,      // Awareness Engine (keyword triggers + OCR)
        Deeper,         // Deeper tab (universal media enhancement)
        DeeperEditor,   // Deeper editor coachmarks (targets the editor window)
        DeeperEditorInteractiveHT, // Interactive on-rails HypnoTube walkthrough — Part 1 (NewEnhancementDialog → click Create)
        DeeperEditorInteractiveHTPart2, // Part 2 — runs in DeeperEditorWindow after dialog hands off
        DeeperEditorInteractiveLocalAudio, // Interactive on-rails Local Audio walkthrough - Part 1
        DeeperEditorInteractiveLocalAudioPart2, // Part 2 - runs in DeeperEditorWindow (audio mode: waveform preview, audio-only triggers)
        DeeperEditorInteractiveLocalVideo, // Interactive on-rails Local Video walkthrough - Part 1
        DeeperEditorInteractiveLocalVideoPart2 // Part 2 - runs in DeeperEditorWindow (video mode: showcases AttentionLost gaze trigger)
    }

    public class TutorialService
    {
        private List<TutorialStep> _currentSteps;
        private int _currentStepIndex = 0;
        private TutorialType _currentTutorialType = TutorialType.FullTour;

        // Callbacks for tab navigation
        private Action? _showSettings;
        private Action? _showPresets;
        private Action? _showProgression;
        private Action? _showAchievements;
        private Action? _showCompanion;
        private Action? _showPatreon;
        private Action? _showAwareness;
        private Action? _showDeeper;

        public event EventHandler<TutorialStep>? StepChanged;
        public event EventHandler? TutorialStarted;
        public event EventHandler? TutorialCompleted;

        public TutorialStep? CurrentStep =>
            _currentStepIndex >= 0 && _currentStepIndex < _currentSteps.Count
                ? _currentSteps[_currentStepIndex]
                : null;

        public int CurrentStepIndex => _currentStepIndex;
        public int TotalSteps => _currentSteps.Count;
        public IReadOnlyList<TutorialStep> CurrentSteps => _currentSteps;
        public bool IsActive { get; private set; }
        public bool IsFirstStep => _currentStepIndex == 0;
        public bool IsLastStep => _currentStepIndex == _currentSteps.Count - 1;
        public TutorialType CurrentTutorialType => _currentTutorialType;

        public TutorialService()
        {
            _currentSteps = CreateFullTourSteps();
        }

        /// <summary>
        /// Configure OnActivate callbacks with MainWindow actions
        /// </summary>
        public void ConfigureCallbacks(
            Action showSettings,
            Action showPresets,
            Action showProgression,
            Action showAchievements,
            Action showCompanion,
            Action showPatreon,
            Action? showAwareness = null,
            Action? showDeeper = null)
        {
            _showSettings = showSettings;
            _showPresets = showPresets;
            _showProgression = showProgression;
            _showAchievements = showAchievements;
            _showCompanion = showCompanion;
            _showPatreon = showPatreon;
            _showAwareness = showAwareness;
            _showDeeper = showDeeper;
        }

        /// <summary>
        /// Get the steps for a specific tutorial type
        /// </summary>
        private List<TutorialStep> GetStepsForTutorial(TutorialType type)
        {
            return type switch
            {
                TutorialType.FullTour => CreateFullTourSteps(),
                TutorialType.GettingStarted => CreateGettingStartedSteps(),
                TutorialType.Settings => CreateSettingsSteps(),
                TutorialType.Presets => CreatePresetsSteps(),
                TutorialType.Progression => CreateProgressionSteps(),
                TutorialType.Achievements => CreateAchievementsSteps(),
                TutorialType.Companion => CreateCompanionSteps(),
                TutorialType.Patreon => CreatePatreonSteps(),
                TutorialType.Avatar => CreateAvatarSteps(),
                TutorialType.Modding => CreateModdingSteps(),
                TutorialType.Awareness => CreateAwarenessSteps(),
                TutorialType.Deeper => CreateDeeperSteps(),
                TutorialType.DeeperEditor => CreateDeeperEditorSteps(),
                TutorialType.DeeperEditorInteractiveHT => CreateDeeperEditorInteractiveHTSteps(),
                TutorialType.DeeperEditorInteractiveHTPart2 => CreateDeeperEditorInteractiveHTPart2Steps(),
                TutorialType.DeeperEditorInteractiveLocalAudio => CreateDeeperEditorInteractiveLocalAudioSteps(),
                TutorialType.DeeperEditorInteractiveLocalAudioPart2 => CreateDeeperEditorInteractiveLocalAudioPart2Steps(),
                TutorialType.DeeperEditorInteractiveLocalVideo => CreateDeeperEditorInteractiveLocalVideoSteps(),
                TutorialType.DeeperEditorInteractiveLocalVideoPart2 => CreateDeeperEditorInteractiveLocalVideoPart2Steps(),
                _ => CreateFullTourSteps()
            };
        }

        /// <summary>
        /// Start a specific tutorial
        /// </summary>
        public void Start(TutorialType type = TutorialType.FullTour)
        {
            _currentTutorialType = type;
            _currentSteps = GetStepsForTutorial(type);
            ApplyCallbacksToSteps();

            _currentStepIndex = 0;
            IsActive = true;
            TutorialStarted?.Invoke(this, EventArgs.Empty);

            if (CurrentStep != null)
            {
                CurrentStep.OnActivate?.Invoke();
                StepChanged?.Invoke(this, CurrentStep);
            }
        }

        /// <summary>
        /// Start the full tour (original behavior)
        /// </summary>
        public void Start()
        {
            Start(TutorialType.FullTour);
        }

        private void ApplyCallbacksToSteps()
        {
            foreach (var step in _currentSteps)
            {
                // Apply callbacks based on step requirements
                if (step.RequiresTab != null)
                {
                    var tabAction = step.RequiresTab switch
                    {
                        "settings" => _showSettings,
                        "presets" => _showPresets,
                        "progression" => _showProgression,
                        "achievements" => _showAchievements,
                        "companion" => _showCompanion,
                        "patreon" => _showPatreon,
                        "awareness" => _showAwareness,
                        "deeper" => _showDeeper,
                        _ => null
                    };

                    // Compose with any custom OnActivate the step set in its constructor
                    // (e.g. demo-fire steps that need the tab switch AND a side-effect).
                    if (tabAction != null)
                    {
                        var existingActivate = step.OnActivate;
                        if (existingActivate == null)
                        {
                            step.OnActivate = tabAction;
                        }
                        else
                        {
                            step.OnActivate = () => { tabAction(); existingActivate(); };
                        }
                    }
                }
            }
        }

        public void Next()
        {
            if (!IsActive) return;

            if (_currentStepIndex < _currentSteps.Count - 1)
            {
                _currentStepIndex++;
                CurrentStep?.OnActivate?.Invoke();
                StepChanged?.Invoke(this, CurrentStep!);
            }
            else
            {
                Complete();
            }
        }

        public void Previous()
        {
            if (!IsActive || _currentStepIndex <= 0) return;

            _currentStepIndex--;
            CurrentStep?.OnActivate?.Invoke();
            StepChanged?.Invoke(this, CurrentStep!);
        }

        public void Skip()
        {
            Complete();
        }

        private void Complete()
        {
            IsActive = false;
            TutorialCompleted?.Invoke(this, EventArgs.Empty);
        }

        #region Tutorial Step Definitions

        private List<TutorialStep> CreateFullTourSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "welcome",
                    Icon = "~",
                    Title = "Welcome to Conditioning Control Panel!",
                    Description = "This quick tour will show you how to use the app effectively. " +
                                  "You can restart this tutorial anytime using the ? button in the top right.",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "avatar_intro",
                    Icon = "<3",
                    Title = "Meet Your Companion",
                    Description = "Your avatar companion lives in the tube! Click her to chat, right-click for quick options. " +
                                  "She evolves as you level up!",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "settings_tab",
                    Icon = ">",
                    Title = "Settings Tab",
                    Description = "This is your main configuration area. Toggle features on/off, " +
                                  "adjust frequencies, opacity, and more.",
                    TargetElementName = "BtnSettings",
                    RequiresTab = "settings",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "presets_intro",
                    Icon = ">",
                    Title = "Presets & Sessions",
                    Description = "Save your settings as presets, or run timed sessions with crafted experiences.",
                    TargetElementName = "BtnPresets",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "progression_intro",
                    Icon = ">",
                    Title = "Progression",
                    Description = "Gain XP and level up to unlock new features. Check the Progression tab for details.",
                    TargetElementName = "BtnProgression",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "help_button",
                    Icon = "?",
                    Title = "Need Help?",
                    Description = "Click the ? button anytime to see detailed guides for each feature. " +
                                  "You can also start focused tutorials for specific tabs!",
                    TargetElementName = "BtnMainHelp",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "start_button",
                    Icon = ">",
                    Title = "Ready to Begin?",
                    Description = "Click the START button to begin your conditioning session. " +
                                  "All your configured effects will activate. Click again to stop.",
                    TargetElementName = "BtnStart",
                    TextPosition = TutorialStepPosition.Top
                }
            };
        }

        private List<TutorialStep> CreateGettingStartedSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "gs_welcome",
                    Icon = "~",
                    Title = "Getting Started",
                    Description = "Let's quickly cover the basics of Conditioning Control Panel!",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "gs_start",
                    Icon = ">",
                    Title = "The START Button",
                    Description = "The big START button at the bottom starts/stops all your configured effects. " +
                                  "When running, effects like flashes, videos, and subliminals will trigger based on your settings.",
                    TargetElementName = "BtnStart",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "gs_hover",
                    Icon = "?",
                    Title = "Hover for Help",
                    Description = "Hover over any slider, checkbox, or button to see a tooltip explaining what it does. " +
                                  "This is the fastest way to learn!",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "gs_assets",
                    Icon = ">",
                    Title = "Add Your Own Content",
                    Description = "Add images to 'assets/images' for flashes, and videos to 'assets/videos'. " +
                                  "Use the folder button to open the assets folder directly.",
                    TargetElementName = "BtnOpenAssets",
                    RequiresTab = "settings",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "gs_done",
                    Icon = "<3",
                    Title = "You're Ready!",
                    Description = "That's the basics! Explore the tabs to discover more features, " +
                                  "or click the ? button for detailed guides on each section.",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreateSettingsSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "set_intro",
                    Icon = "⚙",
                    Title = "Settings Tab Guide",
                    Description = "The Settings tab is where you configure all your conditioning effects. " +
                                  "Let's explore each section!",
                    RequiresTab = "settings",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "set_flash",
                    Icon = "⚡",
                    Title = "Flash Images",
                    Description = "Flash images appear randomly on screen. Configure:\n" +
                                  "• Enable/Disable the feature\n" +
                                  "• Per Hour: How many flash events per hour\n" +
                                  "• Images: How many images per flash event\n" +
                                  "• Clickable: Click to dismiss or click-through\n" +
                                  "• Hydra Mode: Clicking spawns more images!",
                    RequiresTab = "settings",
                    TargetElementName = "FlashSection",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "set_visuals",
                    Icon = "👁",
                    Title = "Visuals Settings",
                    Description = "Customize how flash images look:\n" +
                                  "• Size: Scale images up or down\n" +
                                  "• Opacity: Make images more transparent\n" +
                                  "• Fade: Smooth fade in/out animation\n" +
                                  "• Duration: How long images stay visible",
                    RequiresTab = "settings",
                    TargetElementName = "VisualsSection",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "set_video",
                    Icon = "🎬",
                    Title = "Videos",
                    Description = "Mandatory video popups that demand attention:\n" +
                                  "• Per Hour: How often videos play\n" +
                                  "• Force Focus: Bring video to front\n" +
                                  "• Attention Targets: Click targets to dismiss\n" +
                                  "Add videos to 'assets/videos' folder.",
                    RequiresTab = "settings",
                    TargetElementName = "VideoSection",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "set_audio",
                    Icon = "🔊",
                    Title = "Audio Settings",
                    Description = "Control audio behavior:\n" +
                                  "• Audio Ducking: Lower other audio during videos\n" +
                                  "• Video Volume: Control video playback volume\n" +
                                  "• Moans: Enable/configure moaning sounds",
                    RequiresTab = "settings",
                    TargetElementName = "AudioSection",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "set_subliminal",
                    Icon = "💭",
                    Title = "Subliminals",
                    Description = "Quick text messages that flash on screen:\n" +
                                  "• Frequency: How often they appear\n" +
                                  "• Duration: How long they're visible\n" +
                                  "• Customize text in the Subliminals section",
                    RequiresTab = "settings",
                    TargetElementName = "SubliminalSection",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "set_system",
                    Icon = "⚙",
                    Title = "System Settings",
                    Description = "Application behavior settings:\n" +
                                  "• Auto-start on Windows startup\n" +
                                  "• Start minimized to tray\n" +
                                  "• Custom assets folder location\n" +
                                  "• Open assets folder to add content",
                    RequiresTab = "settings",
                    TargetElementName = "SystemSection",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "set_overlays",
                    Icon = "🌀",
                    Title = "Overlays & Effects",
                    Description = "Screen effects unlock as you level up:\n" +
                                  "• Brain Drain: Blur/distortion effect (Lvl 10)\n" +
                                  "• Edge Effects: Screen edge animations (Lvl 5)\n" +
                                  "• Bouncing Text: Text that bounces around (Lvl 60)\n" +
                                  "• Bubbles: Pop bubbles for XP! (Lvl 20)\n" +
                                  "Check the Progression tab to see all unlocks!",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "set_done",
                    Icon = "✓",
                    Title = "Settings Complete!",
                    Description = "Now you know all the settings! Remember:\n" +
                                  "• Hover over any control for details\n" +
                                  "• Use 'Test Now' buttons to preview effects\n" +
                                  "• Save your setup as a Preset!",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreatePresetsSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "pre_intro",
                    Icon = "💾",
                    Title = "Presets & Sessions Guide",
                    Description = "The Presets tab lets you save configurations and run timed sessions.",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pre_save",
                    Icon = "💾",
                    Title = "Saving Presets",
                    Description = "Click 'Save Current as Preset' to save your current settings.\n" +
                                  "Give it a name and description. Load presets anytime to restore settings.",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pre_sessions",
                    Icon = "🎯",
                    Title = "Sessions",
                    Description = "Sessions are timed experiences with scripted effects.\n" +
                                  "• Click a session to see details\n" +
                                  "• Sessions bypass level requirements\n" +
                                  "• Great for trying new features!",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pre_editor",
                    Icon = "✏",
                    Title = "Session Editor",
                    Description = "Create your own sessions!\n" +
                                  "• Drag feature icons onto the timeline\n" +
                                  "• Green = start, Red = stop\n" +
                                  "• Export and share with others",
                    TargetElementName = "BtnCreateSession",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "pre_import",
                    Icon = "📂",
                    Title = "Import & Export",
                    Description = "Drag .session.json files onto the app to import.\n" +
                                  "Use the Export button to share your sessions.",
                    RequiresTab = "presets",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreateProgressionSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "prog_intro",
                    Icon = "📊",
                    Title = "Progression Guide",
                    Description = "Track your progress and unlock new features!",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "prog_xp",
                    Icon = "⭐",
                    Title = "XP & Leveling",
                    Description = "Gain XP by:\n" +
                                  "• Running the engine (1 XP/minute)\n" +
                                  "• Completing sessions\n" +
                                  "• Popping bubbles\n" +
                                  "• Clicking flash images\n" +
                                  "Level up to unlock new features!",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "prog_unlocks",
                    Icon = "🔓",
                    Title = "Feature Unlocks",
                    Description = "Features unlock at specific levels:\n" +
                                  "• Level 5: Edge overlay\n" +
                                  "• Level 10: Brain Drain, Moans\n" +
                                  "• Level 20: Bubbles\n" +
                                  "• And many more up to Level 75!",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "prog_scheduler",
                    Icon = "📅",
                    Title = "Scheduler",
                    Description = "Set automatic start times:\n" +
                                  "• Choose active hours\n" +
                                  "• Select days of the week\n" +
                                  "• App auto-starts during scheduled times",
                    TargetElementName = "SchedulerPanel",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "prog_ramp",
                    Icon = "📈",
                    Title = "Intensity Ramp",
                    Description = "Gradually increase intensity over time:\n" +
                                  "• Start at lower intensity\n" +
                                  "• Ramp up to your settings\n" +
                                  "• Great for longer sessions!",
                    TargetElementName = "RampPanel",
                    RequiresTab = "progression",
                    TextPosition = TutorialStepPosition.Left
                }
            };
        }

        private List<TutorialStep> CreateAchievementsSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "ach_intro",
                    Icon = "🏆",
                    Title = "Achievements Guide",
                    Description = "Unlock achievements by reaching milestones!",
                    RequiresTab = "achievements",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ach_types",
                    Icon = "🏆",
                    Title = "Achievement Types",
                    Description = "Different ways to earn achievements:\n" +
                                  "• Session completion milestones\n" +
                                  "• Total runtime goals\n" +
                                  "• Feature usage achievements\n" +
                                  "• Level milestones\n" +
                                  "• Special hidden achievements",
                    RequiresTab = "achievements",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ach_view",
                    Icon = "👁",
                    Title = "Viewing Achievements",
                    Description = "Click on any achievement tile to see details.\n" +
                                  "Locked achievements show hints on how to unlock them.\n" +
                                  "Try to collect them all!",
                    RequiresTab = "achievements",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreateCompanionSteps()
        {
            // OnActivate helper: idempotently reveal the companion roster tray.
            // Don't call BtnSwitchCompanion_Click — it's a toggle and would hide
            // the tray if the user had already opened it before launching the tour.
            Action revealRoster = () =>
            {
                try
                {
                    var win = Application.Current?.MainWindow as MainWindow;
                    if (win == null) return;
                    var tray = win.FindName("CompanionRosterTray") as FrameworkElement;
                    if (tray != null) tray.Visibility = Visibility.Visible;
                }
                catch { /* tour never blocks on UI quirks */ }
            };

            bool aiUnlocked = App.HasCloudIdentity;

            var steps = new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "comp_intro",
                    Icon = "💗",
                    Title = "Meet Your Companion",
                    Description = "This tab is where she lives.\n\n" +
                                  "She's the voice in your speech bubbles, the personality behind your AI chats, " +
                                  "and the one keeping score of your XP. There's a lot in here — let's walk it together.",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "comp_hero",
                    Icon = "✨",
                    Title = "Active Companion",
                    Description = "Up here you see who's currently active: her avatar, her name, " +
                                  "her level, and the XP bar toward her next level.\n\n" +
                                  "The two pills on the right show whether AI is on (Off / Cloud / Local) " +
                                  "and whether window-Awareness mode is running.",
                    RequiresTab = "companion",
                    TargetElementName = "TxtActiveCompanionName",
                    TextPosition = TutorialStepPosition.Bottom
                },
                new TutorialStep
                {
                    Id = "comp_switch",
                    Icon = "🔄",
                    Title = "Five Companions",
                    Description = "There are five companions, and each one gives you a different XP bonus " +
                                  "(Pink Filter, Autonomy, XP Drain, Strict Mode, Session Completion).\n\n" +
                                  "Hit Switch any time to swap between them — your XP for each is tracked separately.",
                    RequiresTab = "companion",
                    TargetElementName = "BtnSwitchCompanion",
                    TextPosition = TutorialStepPosition.Right,
                    OnActivate = revealRoster
                },
                new TutorialStep
                {
                    Id = "comp_roster",
                    Icon = "🎭",
                    Title = "The Roster — Two Clicks",
                    Description = "Two different clicks live on each card:\n\n" +
                                  "• Click the card itself → switch to that companion.\n" +
                                  "• Click the small 🎭 button → assign an AI personality to her without switching.\n\n" +
                                  "Each card also shows that companion's level and her XP bonus.",
                    RequiresTab = "companion",
                    TargetElementName = "CompanionRosterTray",
                    TextPosition = TutorialStepPosition.Top,
                    OnActivate = revealRoster
                },
                new TutorialStep
                {
                    Id = "comp_chat_shortcut",
                    Icon = "💬",
                    Title = "Chat Hotkey",
                    Description = "She has a global hotkey for chat — Ctrl+T by default, anywhere on your machine.\n\n" +
                                  "Click this button to rebind it to whatever combo you like. Useful when " +
                                  "Ctrl+T collides with another app you use a lot.",
                    RequiresTab = "companion",
                    TargetElementName = "BtnChatShortcut",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "comp_avatar",
                    Icon = "👁",
                    Title = "Avatar Window Controls",
                    Description = "Three quick toggles for her avatar window:\n\n" +
                                  "• Show Avatar — pop her on or off your screen.\n" +
                                  "• Mute — silence her speech and sound effects.\n" +
                                  "• Detach (the button next to Switch) — float her free of the main window so " +
                                  "you can drag her anywhere.",
                    RequiresTab = "companion",
                    TargetElementName = "ChkAvatarEnabledCompanion",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "comp_customize",
                    Icon = "🎨",
                    Title = "Personality Editor",
                    Description = "Customize opens the full personality editor — her tone, her quirks, " +
                                  "the system prompt that drives every AI reply.\n\n" +
                                  "The \"Open Advanced Personality\" button down in AI Brain opens the same editor.",
                    RequiresTab = "companion",
                    TargetElementName = "BtnCustomizeCompanion",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "comp_ai_brain_intro",
                    Icon = "🧠",
                    Title = "AI Brain",
                    Description = aiUnlocked
                        ? "AI Brain is what gives her real conversation. She'll reply to you, react to " +
                          "what's on your screen, and chime in unprompted.\n\n" +
                          "Three things to set up here: which provider, which capabilities, and how spicy."
                        : "AI Brain is what gives her real conversation — replies, reactions, unprompted chimes.\n\n" +
                          "Right now it's locked because you're not signed in. " +
                          "Sign in with Discord or Patreon (Patreon tab) and the section unlocks. " +
                          "AI is free for all signed-in users.",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                }
            };

            if (aiUnlocked)
            {
                steps.Add(new TutorialStep
                {
                    Id = "comp_ai_provider",
                    Icon = "📡",
                    Title = "Pick a Provider",
                    Description = "Three modes:\n\n" +
                                  "• Off — no AI, just her phrase library.\n" +
                                  "• Cloud — easiest. Talks to our proxy. Free for signed-in users.\n" +
                                  "• Local — runs Ollama on your own machine. Fully private, but you install Ollama once.\n\n" +
                                  "Pick Local and the model name + host fields appear below, along with a Setup wizard " +
                                  "that walks you through Ollama install.",
                    RequiresTab = "companion",
                    TargetElementName = "AiProviderRadioGroup",
                    TextPosition = TutorialStepPosition.Top
                });
                steps.Add(new TutorialStep
                {
                    Id = "comp_capabilities",
                    Icon = "💡",
                    Title = "Capabilities",
                    Description = "Two switches that decide what she's allowed to do:\n\n" +
                                  "• AI Chat — turns on real AI replies in chat and bubbles.\n" +
                                  "• Awareness Mode — she watches which window/program is active and reacts. " +
                                  "Flipping it on reveals a cooldown slider so she doesn't spam.",
                    RequiresTab = "companion",
                    TargetElementName = "ChkAiChat",
                    TextPosition = TutorialStepPosition.Right
                });
                steps.Add(new TutorialStep
                {
                    Id = "comp_slut_mode",
                    Icon = "🌶",
                    Title = "Slut Mode",
                    Description = "Flips her to the spicier personality variant — same companion, dirtier mouth.\n\n" +
                                  "Toggle on or off whenever; it doesn't change her level or XP.",
                    RequiresTab = "companion",
                    TargetElementName = "ChkSlutMode",
                    TextPosition = TutorialStepPosition.Right
                });
            }
            else
            {
                steps.Add(new TutorialStep
                {
                    Id = "comp_ai_locked",
                    Icon = "🔒",
                    Title = "Login Unlocks AI",
                    Description = "Everything in AI Brain — provider choice, chat replies, awareness, slut mode — " +
                                  "lives behind this lock until you sign in.\n\n" +
                                  "Discord login or Patreon login both work. Once you're in, replay this tour and " +
                                  "we'll cover all the AI controls.",
                    RequiresTab = "companion",
                    TargetElementName = "AiFeaturesLockOverlay",
                    TextPosition = TutorialStepPosition.Top
                });
            }

            steps.Add(new TutorialStep
            {
                Id = "comp_timing",
                Icon = "⏱",
                Title = "How Often, How Long",
                Description = "Two timing sliders in the Behavior panel on the right:\n\n" +
                              "• Idle Giggle Interval (5–300s) — how often she chimes in unprompted.\n" +
                              "• Bubble Duration (1–10s) — how long each speech bubble stays on screen.\n\n" +
                              "If she feels too chatty, raise the giggle interval. If you can't read fast enough, " +
                              "raise the bubble duration.",
                RequiresTab = "companion",
                TargetElementName = "SliderIdleIntervalCompanion",
                TextPosition = TutorialStepPosition.Left
            });
            steps.Add(new TutorialStep
            {
                Id = "comp_triggers",
                Icon = "⚡",
                Title = "Trigger Mode",
                Description = "Trigger Mode rotates a list of phrases at a fixed interval — handy for mantra-style " +
                              "drilling without typing anything yourself.\n\n" +
                              "Flip it on and a panel appears with the rotation interval and an Edit Triggers button " +
                              "where you can add or remove phrases.",
                RequiresTab = "companion",
                TargetElementName = "ChkTriggerModeCompanion",
                TextPosition = TutorialStepPosition.Left
            });
            steps.Add(new TutorialStep
            {
                Id = "comp_phrases_community",
                Icon = "📚",
                Title = "Phrases & Community Prompts",
                Description = "Three more knobs in this column:\n\n" +
                              "• Manage Phrases — her speech bubble library, with on/off per phrase.\n" +
                              "• Phrase Presets — save a phrase config and load it later.\n" +
                              "• Community Prompts — Browse / Import / Export AI personalities other users have made.\n" +
                              "• Hypnotube Links — comma-separated video URLs she's allowed to suggest (2000-char cap).",
                RequiresTab = "companion",
                TargetElementName = "BtnManagePhrases",
                TextPosition = TutorialStepPosition.Left
            });
            steps.Add(new TutorialStep
            {
                Id = "comp_done",
                Icon = "❤",
                Title = "You're Set",
                Description = "That's the whole tab.\n\n" +
                              "• Want to replay this tour? The 🎓 button next to the tab title runs it again.\n" +
                              "• It's also in the ? menu (top-right) under Companion.\n" +
                              "• Most controls have hover tooltips with extra detail.\n\n" +
                              "Click Finish — go play.",
                RequiresTab = "companion",
                TextPosition = TutorialStepPosition.Center
            });

            return steps;
        }

        private List<TutorialStep> CreatePatreonSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "pat_intro",
                    Icon = "💎",
                    Title = "Patreon Exclusives Guide",
                    Description = "Special features for Patreon supporters!",
                    RequiresTab = "patreon",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pat_login",
                    Icon = "🔑",
                    Title = "Logging In",
                    Description = "Click 'Login with Patreon' to connect your account.\n" +
                                  "Your subscription tier unlocks corresponding features automatically.",
                    RequiresTab = "patreon",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pat_ai",
                    Icon = "🤖",
                    Title = "AI Chat",
                    Description = "Chat with your AI companion!\n" +
                                  "• Double-click the avatar to chat\n" +
                                  "• She remembers conversation context\n" +
                                  "• Personality adapts to your interactions",
                    RequiresTab = "patreon",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pat_awareness",
                    Icon = "👁",
                    Title = "Window Awareness",
                    Description = "Your companion knows what you're doing:\n" +
                                  "• Detects active windows\n" +
                                  "• Comments on your activity\n" +
                                  "• Privacy: Only window titles are read",
                    RequiresTab = "patreon",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "pat_slut",
                    Icon = "🔥",
                    Title = "Slut Mode",
                    Description = "Enable explicit AI responses:\n" +
                                  "• More provocative messages\n" +
                                  "• Adult-themed interactions\n" +
                                  "• Toggle on/off anytime",
                    RequiresTab = "patreon",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreateAvatarSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "ava_intro",
                    Icon = "💗",
                    Title = "Avatar Companion Guide",
                    Description = "Everything about your avatar companion!",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ava_tube",
                    Icon = "🔮",
                    Title = "The Avatar Tube",
                    Description = "Your companion lives in the tube on the right side.\n" +
                                  "She's always there watching and reacting to what happens in the app.",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ava_click",
                    Icon = "👆",
                    Title = "Interacting",
                    Description = "• Single click: Open chat (if enabled)\n" +
                                  "• Double click: Quick chat\n" +
                                  "• Right click: Quick menu (Start, Trigger, Slut Mode)",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ava_detach",
                    Icon = "📌",
                    Title = "Detaching",
                    Description = "Click the 'Detach' button to pop out the avatar:\n" +
                                  "• Drag her anywhere on screen\n" +
                                  "• Resize with Ctrl+Scroll or Arrow keys\n" +
                                  "• Click 'Attach' to return to main window",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ava_evolution",
                    Icon = "🌟",
                    Title = "Evolution",
                    Description = "Your avatar evolves as you level up!\n" +
                                  "Different appearance stages unlock at:\n" +
                                  "• Level 1, 10, 25, 50, 75\n" +
                                  "Keep leveling to see all forms!",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "ava_animations",
                    Icon = "✨",
                    Title = "Animations",
                    Description = "Your avatar reacts to events:\n" +
                                  "• Blinks and idles\n" +
                                  "• Reacts to flashes and videos\n" +
                                  "• Shows emotions during interactions",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        private List<TutorialStep> CreateModdingSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "mod_welcome",
                    Icon = "\uD83D\uDD27",
                    Title = "Welcome to the Mod Creator!",
                    Description = "This tool lets you build a complete mod visually \u2014 no manual file editing needed.\n\n" +
                                  "We'll walk through each tab so you know exactly what everything does. " +
                                  "You can reopen this guide anytime with the ? button in the title bar.",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_info",
                    Icon = "\u2139",
                    Title = "Info",
                    Description = "Start here \u2014 give your mod a name, author, version, and description.\n\n" +
                                  "Add a preview image that shows in the mod manager so people know what your mod looks like at a glance.",
                    RequiresTab = "mod:info",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_theme",
                    Icon = "\uD83C\uDFA8",
                    Title = "Theme",
                    Description = "Pick your mod's color scheme. Click any color swatch to open a color picker, or type hex codes directly.\n\n" +
                                  "The preview strip at the top shows all your colors together so you can see how they look as a set.",
                    RequiresTab = "mod:theme",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_identity",
                    Icon = "\uD83E\uDD16",
                    Title = "Identity",
                    Description = "Define who the companion is. Change their name, what they call the user, the mode name, and button labels.\n\n" +
                                  "These labels appear throughout the entire app when your mod is active.",
                    RequiresTab = "mod:identity",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_achievements",
                    Icon = "\uD83C\uDFC6",
                    Title = "Achievements",
                    Description = "Drag and drop custom achievement icons onto each slot. These replace the default badges.\n\n" +
                                  "Leave slots empty to keep the originals \u2014 you only need to replace what you want to change.",
                    RequiresTab = "mod:achievements",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_features",
                    Icon = "\u26A1",
                    Title = "Features",
                    Description = "Replace feature icons (flash, video, subliminal, etc.) with your own artwork.\n\n" +
                                  "Drag PNG images onto any slot. These are the icons that appear on the main tabs and UI controls.",
                    RequiresTab = "mod:features",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_skills",
                    Icon = "\uD83C\uDF32",
                    Title = "Skills",
                    Description = "Custom skill tree icons. Each slot maps to a skill in the progression system.\n\n" +
                                  "Drag and drop your own icons to give the skill tree a completely different look.",
                    RequiresTab = "mod:skills",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_avatars",
                    Icon = "\uD83D\uDC64",
                    Title = "Avatars",
                    Description = "Your mod can have up to 7 avatar sets with 4 poses each (Standby, Active, Alert, Override).\n\n" +
                                  "Drag images to customize your companion's appearance at each evolution stage.",
                    RequiresTab = "mod:avatars",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_uiassets",
                    Icon = "\uD83D\uDDBC",
                    Title = "UI Assets",
                    Description = "Replace bubbles, tube frames, logo, speech bubbles, and card art.\n\n" +
                                  "These are the decorative elements throughout the app. Drop your images onto any slot to replace them.",
                    RequiresTab = "mod:uiassets",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_triggers",
                    Icon = "\uD83D\uDCA5",
                    Title = "Triggers",
                    Description = "Customize trigger text \u2014 what happens on Freeze, Reset, Collapse, and Autonomy events.\n\n" +
                                  "Type your own text for each trigger to match your mod's theme.",
                    RequiresTab = "mod:triggers",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_messages",
                    Icon = "\uD83D\uDCE2",
                    Title = "Messages",
                    Description = "Set the companion's attention check messages and bubble count retry text.\n\n" +
                                  "These show up during interactive moments when the app needs the user's attention.",
                    RequiresTab = "mod:messages",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_phrases",
                    Icon = "\uD83D\uDCAC",
                    Title = "Phrases",
                    Description = "The big one! Add phrases for every situation \u2014 greetings, idle chatter, gaming, browsing, level ups, and more.\n\n" +
                                  "Click a category to expand it, then add as many phrases as you want. " +
                                  "The companion picks randomly from your list.",
                    RequiresTab = "mod:phrases",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_tab_replacements",
                    Icon = "\uD83D\uDD04",
                    Title = "Text Replacements",
                    Description = "Find-and-replace across the entire UI. Map words to your mod's equivalent.\n\n" +
                                  "These apply everywhere automatically \u2014 every label, phrase, and message gets substituted.",
                    RequiresTab = "mod:replacements",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "mod_export",
                    Icon = "\uD83D\uDCE5",
                    Title = "Export Your Mod",
                    Description = "When you're done, click 'Export as .ccpmod' at the bottom. Your mod is packaged into a single file ready to share!\n\n" +
                                  "You can also load an existing .ccpmod to edit it using the 'Load .ccpmod' button.",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        // Awareness Engine tutorial \u2014 narrated around setting up a "good boy" clicker
        // so each section has a concrete reason to exist in the user's head, not just
        // a description. The script is intentionally non-technical: the engine, OCR,
        // cooldowns, scroll dedup, and the action editor are all explained in plain
        // language. Step 11 fires a synthetic trigger so users actually SEE the
        // engine react instead of just being told what it would do.
        private List<TutorialStep> CreateAwarenessSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "aw_intro",
                    Icon = "\uD83D\uDC41",
                    Title = "The Awareness Engine",
                    Description = "The Awareness Engine watches your screen and your typing for keywords " +
                                  "you choose, then reacts \u2014 a sound, a glow, a praise line from your companion, " +
                                  "anything you want.\n\n" +
                                  "Let's walk through it by setting up a simple \"good boy\" clicker.",
                    RequiresTab = "awareness",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "aw_master",
                    Icon = "\uD83D\uDD18",
                    Title = "Master Switch",
                    Description = "This is the master switch. Off = nothing happens at all.\n\n" +
                                  "The dot turns green when the engine is on and listening.",
                    RequiresTab = "awareness",
                    TargetElementName = "ChkAwarenessMaster",
                    TextPosition = TutorialStepPosition.Bottom
                },
                new TutorialStep
                {
                    Id = "aw_pulse",
                    Icon = "\uD83D\uDCE1",
                    Title = "Live Pulse Feed",
                    Description = "Every time a keyword fires, it lands here \u2014 your live receipt.\n\n" +
                                  "If you're ever wondering \"did the engine actually catch that?\", " +
                                  "this feed tells you in real time. We'll come back here in a moment.",
                    RequiresTab = "awareness",
                    TargetElementName = "AwarenessPulseFeed",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "aw_sources_ocr",
                    Icon = "\uD83D\uDDA5",
                    Title = "Screen OCR",
                    Description = "Screen OCR scans your monitors every few seconds and reads the text on them.\n\n" +
                                  "This is how the engine catches words on web pages, chat windows, " +
                                  "captions, anything visible \u2014 even if you didn't type it yourself.\n\n" +
                                  "It runs entirely on your computer. Nothing is sent anywhere.",
                    RequiresTab = "awareness",
                    TargetElementName = "ChkAwarenessOcr",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "aw_sources_keyboard",
                    Icon = "\u2328",
                    Title = "Keyboard Watching",
                    Description = "Keyboard mode watches what you type, even outside this app.\n\n" +
                                  "Either source works on its own \u2014 you can use OCR, keyboard, or both at once. " +
                                  "Most people leave both on.",
                    RequiresTab = "awareness",
                    TargetElementName = "ChkAwarenessKeyboard",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "aw_safety_ownui",
                    Icon = "\uD83D\uDEE1",
                    Title = "Ignore Own UI",
                    Description = "This skips the app's own windows during OCR scans.\n\n" +
                                  "Without it, the engine would read your settings text and trigger on it. Leave this on.",
                    RequiresTab = "awareness",
                    TargetElementName = "ChkAwarenessIgnoreOwnUi",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "aw_safety_loop",
                    Icon = "\uD83D\uDD01",
                    Title = "Loop Protection",
                    Description = "Some triggers flash the keyword back on screen. Without protection, " +
                                  "OCR would re-read it and the trigger would fire forever.\n\n" +
                                  "Loop Protection mutes a keyword for a few seconds after it fires, " +
                                  "across all sources. Leave this on too.",
                    RequiresTab = "awareness",
                    TargetElementName = "ChkAwarenessLoopProtection",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "aw_scroll_note",
                    Icon = "\uD83D\uDCDC",
                    Title = "What About Scrolling?",
                    Description = "When you scroll a page, OCR sees the same words again \u2014 you might worry that " +
                                  "scrolling would spam the engine with false fires.\n\n" +
                                  "It doesn't. The engine waits for a word to stay in the same spot for two scans " +
                                  "before counting it. Scrolling, redraws, and cursor movement are filtered out automatically.\n\n" +
                                  "There's nothing here for you to configure \u2014 it just works.",
                    RequiresTab = "awareness",
                    TargetElementName = "AwarenessPulseFeed",
                    TextPosition = TutorialStepPosition.Bottom
                },
                new TutorialStep
                {
                    Id = "aw_cd_global",
                    Icon = "\u23F1",
                    Title = "Global Cooldown",
                    Description = "Global cooldown = the gap between any two reactions, regardless of which keyword fired.\n\n" +
                                  "If multiple words land at once and you'd rather feel one click than five, " +
                                  "raise this slider. Most people start around 10 seconds and tune from there.",
                    RequiresTab = "awareness",
                    TargetElementName = "SliderAwarenessGlobalCooldown",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "aw_cd_sameword",
                    Icon = "\uD83D\uDD52",
                    Title = "Same-Word Cooldown",
                    Description = "Same-word cooldown = the gap before the same keyword can fire again.\n\n" +
                                  "If \"good boy\" appears five times on a page, only the first one fires. " +
                                  "Other keywords can still fire normally during that window.\n\n" +
                                  "Crank both cooldowns up if you ever feel overloaded \u2014 it's the gentlest way to dial things back.",
                    RequiresTab = "awareness",
                    TargetElementName = "SliderAwarenessSameWordCooldown",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "aw_demo_fire",
                    Icon = "\u2728",
                    Title = "Watch It Catch a Word",
                    Description = "Watch the pulse feed \u2014 the engine just simulated a fire for the word \"good boy\".\n\n" +
                                  "That's exactly what happens when OCR or your keyboard catches one of your keywords " +
                                  "in real life. If your companion is attached, she may have said something too.",
                    RequiresTab = "awareness",
                    TargetElementName = "AwarenessPulseFeed",
                    TextPosition = TutorialStepPosition.Right,
                    OnActivate = () =>
                    {
                        try { App.KeywordTriggers?.FireDemoTrigger("good boy", "Tutorial"); }
                        catch { /* tutorial demo never blocks the tour */ }
                    }
                },
                new TutorialStep
                {
                    Id = "aw_presets",
                    Icon = "\uD83C\uDF81",
                    Title = "Preset Packs",
                    Description = "The easiest way to start: install a preset pack.\n\n" +
                                  "Each pack bundles a set of keywords and the responses that fire when they're caught. " +
                                  "The Puppy Pet pack already includes \"good boy\" with a clicker sound and a praise line \u2014 " +
                                  "pick it if you want a one-click clicker setup, or browse the others.",
                    RequiresTab = "awareness",
                    TargetElementName = "AwarenessPresetItems",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "aw_highlight",
                    Icon = "\uD83C\uDFA8",
                    Title = "Highlight Glow",
                    Description = "When a keyword is caught on-screen, the engine paints a glow around it " +
                                  "so you actually see the recognition happen.\n\n" +
                                  "Pick a color you like \u2014 pink by default. The \"visible in screen capture\" toggle " +
                                  "decides whether OBS / streaming software sees the glow too.",
                    RequiresTab = "awareness",
                    TargetElementName = "AwarenessHighlightColorPanel",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "aw_advanced",
                    Icon = "\uD83D\uDD27",
                    Title = "Advanced Editor",
                    Description = "When you want more control \u2014 swap the clicker sound, change the praise line, " +
                                  "add XP per fire, send time to a Chaster lock \u2014 open the Advanced editor.\n\n" +
                                  "Hit Next to peek inside.",
                    RequiresTab = "awareness",
                    TargetElementName = "LnkAwarenessAdvanced",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "aw_editor_open",
                    Icon = "\uD83C\uDFAF",
                    Title = "Inside the Editor",
                    Description = "Each row in the editor is one keyword, with a stack of responses below it:\n\n" +
                                  "\uD83D\uDD0A sound clip   \u2728 glow   \uD83D\uDCAC avatar line   \u2B50 XP\n" +
                                  "\uD83D\uDCF3 haptic   \u23F1 extend session   \uD83D\uDD12 Chaster time\n\n" +
                                  "Add, remove, retune any of them \u2014 your changes save the moment you close.\n\n" +
                                  "We'll pop the editor open with the Puppy preset for you when you finish this tour.",
                    RequiresTab = "awareness",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "aw_done",
                    Icon = "\u2764",
                    Title = "You're Set",
                    Description = "That's the whole tour.\n\n" +
                                  "\u2022 Privacy: nothing leaves your machine \u2014 OCR runs locally on Windows.\n" +
                                  "\u2022 Feeling overloaded later? Raise the two cooldown sliders.\n" +
                                  "\u2022 Want this tour again? It's in the ? button at the top right.\n\n" +
                                  "Click Finish \u2014 the editor will open so you can play.",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        // Deeper tab tour. Targets element names in the Deeper tab; the
        // RequiresTab="deeper" flips into the tab via the showDeeper callback.
        private List<TutorialStep> CreateDeeperSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "dp_intro",
                    Icon = "\ud83c\udf0a",
                    Title = Loc.Get("deeper_tut_tab_intro_title"),
                    Description = Loc.Get("deeper_tut_tab_intro_body"),
                    RequiresTab = "deeper",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "dp_player",
                    Icon = "\u25b6",
                    Title = Loc.Get("deeper_tut_tab_player_title"),
                    Description = Loc.Get("deeper_tut_tab_player_body"),
                    RequiresTab = "deeper",
                    TargetElementName = "BtnDeeperOpenPlayer",
                    TextPosition = TutorialStepPosition.Bottom
                },
                new TutorialStep
                {
                    Id = "dp_new",
                    Icon = "\u2728",
                    Title = Loc.Get("deeper_tut_tab_new_title"),
                    Description = Loc.Get("deeper_tut_tab_new_body"),
                    RequiresTab = "deeper",
                    TargetElementName = "BtnDeeperNewEnhancement",
                    TextPosition = TutorialStepPosition.Bottom
                },
                new TutorialStep
                {
                    Id = "dp_library",
                    Icon = "\ud83d\udcda",
                    Title = Loc.Get("deeper_tut_tab_library_title"),
                    Description = Loc.Get("deeper_tut_tab_library_body"),
                    RequiresTab = "deeper",
                    TargetElementName = "DeeperLibraryCard",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "dp_recent",
                    Icon = "\ud83d\udd52",
                    Title = Loc.Get("deeper_tut_tab_recent_title"),
                    Description = Loc.Get("deeper_tut_tab_recent_body"),
                    RequiresTab = "deeper",
                    TargetElementName = "DeeperRecentCard",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "dp_done",
                    Icon = "\u2764",
                    Title = Loc.Get("deeper_tut_tab_done_title"),
                    Description = Loc.Get("deeper_tut_tab_done_body"),
                    RequiresTab = "deeper",
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        // Deeper editor coachmarks. These steps target element names that live
        // inside the editor *window*, so the consumer must construct the
        // TutorialOverlay against the editor window (not MainWindow). No
        // RequiresTab \u2014 the editor is its own surface.
        private List<TutorialStep> CreateDeeperEditorSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "de_intro",
                    Icon = "\ud83c\udfa8",
                    Title = Loc.Get("deeper_tut_ed_intro_title"),
                    Description = Loc.Get("deeper_tut_ed_intro_body"),
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "de_timeline",
                    Icon = "\u23f1",
                    Title = Loc.Get("deeper_tut_ed_timeline_title"),
                    Description = Loc.Get("deeper_tut_ed_timeline_body"),
                    TargetElementName = "TimelineCanvas",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "de_preview",
                    Icon = "\ud83d\udc41",
                    Title = Loc.Get("deeper_tut_ed_preview_title"),
                    Description = Loc.Get("deeper_tut_ed_preview_body"),
                    TargetElementName = "BtnPreview",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "de_metadata",
                    Icon = "\ud83d\udcdd",
                    Title = Loc.Get("deeper_tut_ed_metadata_title"),
                    Description = Loc.Get("deeper_tut_ed_metadata_body"),
                    TargetElementName = "TxtMetaName",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "de_rules",
                    Icon = "\ud83d\udd17",
                    Title = Loc.Get("deeper_tut_ed_rules_title"),
                    Description = Loc.Get("deeper_tut_ed_rules_body"),
                    TargetElementName = "RulesList",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "de_selected",
                    Icon = "\ud83d\udc49",
                    Title = Loc.Get("deeper_tut_ed_selected_title"),
                    Description = Loc.Get("deeper_tut_ed_selected_body"),
                    TargetElementName = "SelectedPlaceholder",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "de_save",
                    Icon = "\ud83d\udcbe",
                    Title = Loc.Get("deeper_tut_ed_save_title"),
                    Description = Loc.Get("deeper_tut_ed_save_body"),
                    TargetElementName = "TxtValidationSummary",
                    TextPosition = TutorialStepPosition.Top
                },
                new TutorialStep
                {
                    Id = "de_done",
                    Icon = "\u2764",
                    Title = Loc.Get("deeper_tut_ed_done_title"),
                    Description = Loc.Get("deeper_tut_ed_done_body"),
                    TextPosition = TutorialStepPosition.Center
                }
            };
        }

        // Interactive on-rails HypnoTube walkthrough. Spans NewEnhancementDialog
        // -> DeeperEditorWindow, gating on real user interactions and emitting
        // events through TutorialEventBus. Produces a saved .ccpenh.json with
        // 1 Haptic effect at 5s and 1 TimeReached -> ScreenShake rule at 15s.
        // Part 1: lives entirely inside NewEnhancementDialog. One step that
        // ends when the user clicks Create. The dialog then closes, the
        // editor opens, and DeeperEditorWindow.Loaded starts Part 2 with a
        // fresh overlay. Splitting avoids the cross-window state machine.
        private List<TutorialStep> CreateDeeperEditorInteractiveHTSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "iht_create",
                    Icon = "\ud83c\udfac",
                    Title = Loc.Get("deeper_itut_ht_step1_title"),
                    Description = Loc.Get("deeper_itut_ht_step1_body"),
                    TargetElementName = "BtnCreate",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
                }
            };
        }

        // Part 2: runs in DeeperEditorWindow. Started by the editor's Loaded
        // handler when TutorialEventBus.PendingPart2Tutorial points at this
        // type. No step uses TargetWindowTypeName because everything lives in
        // one window now.
        private List<TutorialStep> CreateDeeperEditorInteractiveHTPart2Steps()
        {
            return new List<TutorialStep>
            {
                // Phase 2 - editor metadata + preview
                new TutorialStep
                {
                    Id = "iht_metadata",
                    Icon = "\ud83d\udcdd",
                    Title = Loc.Get("deeper_itut_ht_step2_title"),
                    Description = Loc.Get("deeper_itut_ht_step2_body"),
                    TargetElementName = "TxtMetaName",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.Manual
                },
                new TutorialStep
                {
                    Id = "iht_lock",
                    Icon = "\ud83d\udd12",
                    Title = Loc.Get("deeper_itut_ht_step3_title"),
                    Description = Loc.Get("deeper_itut_ht_step3_body"),
                    TargetElementName = "BtnCreatorLockToggle",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.Manual
                },
                new TutorialStep
                {
                    Id = "iht_play",
                    Icon = "\u25b6",
                    Title = Loc.Get("deeper_itut_ht_step4_title"),
                    Description = Loc.Get("deeper_itut_ht_step4_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "iht_pause",
                    Icon = "\u23f8",
                    Title = Loc.Get("deeper_itut_ht_step5_title"),
                    Description = Loc.Get("deeper_itut_ht_step5_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 3 - Add Haptic effect
                new TutorialStep
                {
                    Id = "iht_addeffect",
                    Icon = "\u2728",
                    Title = Loc.Get("deeper_itut_ht_step6_title"),
                    Description = Loc.Get("deeper_itut_ht_step6_body"),
                    TargetElementName = "BtnAddEffectHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "EffectAdded"
                },
                new TutorialStep
                {
                    Id = "iht_intensity",
                    Icon = "\ud83c\udf9a",
                    Title = Loc.Get("deeper_itut_ht_step7_title"),
                    Description = Loc.Get("deeper_itut_ht_step7_body"),
                    TargetElementName = "SliderHapticIntensity",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSliderAtLeast,
                    AdvanceMinValue = 0.3,
                    AdvanceMaxValue = 0.7
                },
                new TutorialStep
                {
                    Id = "iht_pattern",
                    Icon = "\ud83c\udf0a",
                    Title = Loc.Get("deeper_itut_ht_step8_title"),
                    Description = Loc.Get("deeper_itut_ht_step8_body"),
                    TargetElementName = "CmbHapticPattern",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    // Empty AdvanceValue \u2192 any pattern selection advances.
                    // (Stock patterns don't visibly differ in the editor without
                    // hitting Test, so demanding a specific name was a trap.)
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "iht_test",
                    Icon = "\ud83c\udfae",
                    Title = Loc.Get("deeper_itut_ht_step9_title"),
                    Description = Loc.Get("deeper_itut_ht_step9_body"),
                    TargetElementName = "BtnTestHaptic",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 4 - Add Rule
                new TutorialStep
                {
                    Id = "iht_addrule",
                    Icon = "\ud83d\udd17",
                    Title = Loc.Get("deeper_itut_ht_step10_title"),
                    Description = Loc.Get("deeper_itut_ht_step10_body"),
                    TargetElementName = "BtnAddRuleHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "RuleAdded"
                },
                new TutorialStep
                {
                    Id = "iht_ruletime",
                    Icon = "\u23f1",
                    Title = Loc.Get("deeper_itut_ht_step11_title"),
                    Description = Loc.Get("deeper_itut_ht_step11_body"),
                    TargetElementName = "TutorialTriggerTimeField",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
                    AdvanceValue = "15"
                },
                new TutorialStep
                {
                    Id = "iht_ruleaction",
                    Icon = "\u26a1",
                    Title = Loc.Get("deeper_itut_ht_step12_title"),
                    Description = Loc.Get("deeper_itut_ht_step12_body"),
                    TargetElementName = "CmbActionType",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AdvanceValue = "screen_shake",
                    // Combo items show a localized friendly name in Content
                    // ("Shake the screen"), with the raw type ("screen_shake")
                    // in Tag. Match by Tag so the comparison is stable across
                    // languages and friendly-name tweaks.
                    MatchByTag = true
                },
                new TutorialStep
                {
                    Id = "iht_actionintensity",
                    Icon = "\ud83c\udf9a",
                    Title = Loc.Get("deeper_itut_ht_step13_title"),
                    Description = Loc.Get("deeper_itut_ht_step13_body"),
                    TargetElementName = "TutorialActionIntensityField",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
                    AdvanceValue = "0.7"
                },

                // Phase 5 - Save
                new TutorialStep
                {
                    Id = "iht_save",
                    Icon = "\ud83d\udcbe",
                    Title = Loc.Get("deeper_itut_ht_step14_title"),
                    Description = Loc.Get("deeper_itut_ht_step14_body"),
                    TargetElementName = "BtnEditorSave",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
                },
                new TutorialStep
                {
                    Id = "iht_savedialog",
                    Icon = "\ud83d\udcbe",
                    Title = Loc.Get("deeper_itut_ht_step15_title"),
                    Description = Loc.Get("deeper_itut_ht_step15_body"),
                    TextPosition = TutorialStepPosition.Center,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "FileSaved",
                    AllowManualSkip = true,
                    // The OS save dialog is on top of the editor \u2014 let clicks
                    // pass through the dim so the user can pick a filename and
                    // hit Save without our overlay eating their input.
                    BlockBackgroundClicks = false
                },

                // Phase 6 - Follow-up card
                new TutorialStep
                {
                    Id = "iht_done",
                    Icon = "\ud83c\udf89",
                    Title = Loc.Get("deeper_itut_ht_step16_title"),
                    Description = Loc.Get("deeper_itut_ht_step16_body"),
                    TextPosition = TutorialStepPosition.Center,
                    IsFollowUpCard = true,
                    FollowUpButton1Text = Loc.Get("deeper_itut_ht_followup_open_folder"),
                    FollowUpAction1 = step =>
                    {
                        try
                        {
                            var path = TutorialEventBus.LastSavedEnhancementPath;
                            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"/select,\"{path}\"",
                                    UseShellExecute = true
                                });
                            }
                        }
                        catch { }
                        try { App.Tutorial?.Skip(); } catch { }
                    },
                    FollowUpButton2Text = Loc.Get("deeper_itut_ht_followup_open_player"),
                    FollowUpAction2 = step =>
                    {
                        try
                        {
                            if (App.DeeperPlayer != null && App.DeeperHost != null)
                            {
                                var owner = Application.Current?.MainWindow;
                                var win = new ConditioningControlPanel.Views.Deeper.EnhancementPlayerWindow(
                                    App.DeeperPlayer, App.DeeperHost);
                                if (owner != null) win.Owner = owner;
                                win.Show();
                            }
                        }
                        catch { }
                        try { App.Tutorial?.Skip(); } catch { }
                    },
                    FollowUpButton3Text = Loc.Get("deeper_itut_ht_followup_done"),
                    FollowUpAction3 = step =>
                    {
                        try { App.Tutorial?.Skip(); } catch { }
                    }
                }
            };
        }

        // Local Audio interactive walkthrough. Mirrors the HT flow's two-part
        // shape but anchors on a user-picked .mp3/.wav file instead of a
        // pre-filled URL, and showcases audio-mode editor differences
        // (waveform preview, no gaze/attention triggers, Pause action).
        // Part 1 lives in NewEnhancementDialog: pick a file via Browse, then
        // Create. Two steps because the user needs to actually choose a file -
        // unlike HT, we can't pre-fill the source.
        private List<TutorialStep> CreateDeeperEditorInteractiveLocalAudioSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "iaud_browse",
                    Icon = "📁",
                    Title = Loc.Get("deeper_itut_audio_step1_title"),
                    Description = Loc.Get("deeper_itut_audio_step1_body"),
                    TargetElementName = "BtnBrowse",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true,
                    // Let clicks pass through the dim - the OS file picker
                    // opens on this click and lives outside our overlay; we
                    // can't have the dim eat the user's interactions while
                    // they navigate the picker (or want to type a path
                    // directly into TxtSource instead of using Browse).
                    BlockBackgroundClicks = false
                },
                new TutorialStep
                {
                    Id = "iaud_create",
                    Icon = "✨",
                    Title = Loc.Get("deeper_itut_audio_step2_title"),
                    Description = Loc.Get("deeper_itut_audio_step2_body"),
                    TargetElementName = "BtnCreate",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    // Click-through too: user may need to re-Browse or edit
                    // TxtSource if they cancelled the picker on step 1.
                    BlockBackgroundClicks = false
                }
            };
        }

        // Local Audio Part 2 - runs in DeeperEditorWindow (audio mode).
        // Same overall arc as HT Part 2 but with audio-specific framing in
        // the body copy (waveform replaces the video preview), and a Pause
        // action on the rule instead of screen_shake to teach a different
        // action while keeping the click count identical.
        private List<TutorialStep> CreateDeeperEditorInteractiveLocalAudioPart2Steps()
        {
            return new List<TutorialStep>
            {
                // Phase 1 - metadata
                new TutorialStep
                {
                    Id = "iaud_metadata",
                    Icon = "📝",
                    Title = Loc.Get("deeper_itut_audio_step3_title"),
                    Description = Loc.Get("deeper_itut_audio_step3_body"),
                    TargetElementName = "TxtMetaName",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.Manual
                },

                // Phase 2 - preview
                new TutorialStep
                {
                    Id = "iaud_play",
                    Icon = "▶",
                    Title = Loc.Get("deeper_itut_audio_step4_title"),
                    Description = Loc.Get("deeper_itut_audio_step4_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "iaud_pause",
                    Icon = "⏸",
                    Title = Loc.Get("deeper_itut_audio_step5_title"),
                    Description = Loc.Get("deeper_itut_audio_step5_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 3 - Add Haptic effect
                new TutorialStep
                {
                    Id = "iaud_addeffect",
                    Icon = "✨",
                    Title = Loc.Get("deeper_itut_audio_step6_title"),
                    Description = Loc.Get("deeper_itut_audio_step6_body"),
                    TargetElementName = "BtnAddEffectHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "EffectAdded"
                },
                new TutorialStep
                {
                    Id = "iaud_intensity",
                    Icon = "🎚",
                    Title = Loc.Get("deeper_itut_audio_step7_title"),
                    Description = Loc.Get("deeper_itut_audio_step7_body"),
                    TargetElementName = "SliderHapticIntensity",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSliderAtLeast,
                    AdvanceMinValue = 0.3,
                    AdvanceMaxValue = 0.7
                },
                new TutorialStep
                {
                    Id = "iaud_pattern",
                    Icon = "🌊",
                    Title = Loc.Get("deeper_itut_audio_step8_title"),
                    Description = Loc.Get("deeper_itut_audio_step8_body"),
                    TargetElementName = "CmbHapticPattern",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "iaud_test",
                    Icon = "🎮",
                    Title = Loc.Get("deeper_itut_audio_step9_title"),
                    Description = Loc.Get("deeper_itut_audio_step9_body"),
                    TargetElementName = "BtnTestHaptic",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 4 - Add Rule (TimeReached → pause)
                new TutorialStep
                {
                    Id = "iaud_addrule",
                    Icon = "🔗",
                    Title = Loc.Get("deeper_itut_audio_step10_title"),
                    Description = Loc.Get("deeper_itut_audio_step10_body"),
                    TargetElementName = "BtnAddRuleHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "RuleAdded"
                },
                new TutorialStep
                {
                    Id = "iaud_ruletime",
                    Icon = "⏱",
                    Title = Loc.Get("deeper_itut_audio_step11_title"),
                    Description = Loc.Get("deeper_itut_audio_step11_body"),
                    TargetElementName = "TutorialTriggerTimeField",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
                    AdvanceValue = "8"
                },
                new TutorialStep
                {
                    Id = "iaud_ruleaction",
                    Icon = "⏸",
                    Title = Loc.Get("deeper_itut_audio_step12_title"),
                    Description = Loc.Get("deeper_itut_audio_step12_body"),
                    TargetElementName = "CmbActionType",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AdvanceValue = "pause",
                    MatchByTag = true,
                    AllowManualSkip = true
                },

                // Phase 5 - Save
                new TutorialStep
                {
                    Id = "iaud_save",
                    Icon = "💾",
                    Title = Loc.Get("deeper_itut_audio_step13_title"),
                    Description = Loc.Get("deeper_itut_audio_step13_body"),
                    TargetElementName = "BtnEditorSave",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
                },
                new TutorialStep
                {
                    Id = "iaud_savedialog",
                    Icon = "💾",
                    Title = Loc.Get("deeper_itut_audio_step14_title"),
                    Description = Loc.Get("deeper_itut_audio_step14_body"),
                    TextPosition = TutorialStepPosition.Center,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "FileSaved",
                    AllowManualSkip = true,
                    BlockBackgroundClicks = false
                },

                // Phase 6 - Follow-up card
                BuildInteractiveDoneCard(
                    "iaud_done",
                    Loc.Get("deeper_itut_audio_step15_title"),
                    Loc.Get("deeper_itut_audio_step15_body"))
            };
        }

        // Local Video interactive walkthrough. Same Part-1 shape as Local Audio
        // (Browse → Create), then Part 2 showcases video's unique trigger:
        // AttentionLost. Action stays screen_shake to mirror HT - the teaching
        // delta is the trigger choice, not the action.
        private List<TutorialStep> CreateDeeperEditorInteractiveLocalVideoSteps()
        {
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "ivid_browse",
                    Icon = "📁",
                    Title = Loc.Get("deeper_itut_video_step1_title"),
                    Description = Loc.Get("deeper_itut_video_step1_body"),
                    TargetElementName = "BtnBrowse",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true,
                    // See iaud_browse: dim must not eat clicks while the OS
                    // file picker is open and modal to the dialog underneath.
                    BlockBackgroundClicks = false
                },
                new TutorialStep
                {
                    Id = "ivid_create",
                    Icon = "✨",
                    Title = Loc.Get("deeper_itut_video_step2_title"),
                    Description = Loc.Get("deeper_itut_video_step2_body"),
                    TargetElementName = "BtnCreate",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    BlockBackgroundClicks = false
                }
            };
        }

        // Local Video Part 2 - runs in DeeperEditorWindow (video mode).
        // Identical scaffolding to Audio Part 2 except the rule uses a video-
        // only AttentionLost trigger (no time field) → screen_shake action,
        // which teaches the gaze-aware rule path that's the point of using
        // video over audio in the first place.
        private List<TutorialStep> CreateDeeperEditorInteractiveLocalVideoPart2Steps()
        {
            return new List<TutorialStep>
            {
                // Phase 1 - metadata
                new TutorialStep
                {
                    Id = "ivid_metadata",
                    Icon = "📝",
                    Title = Loc.Get("deeper_itut_video_step3_title"),
                    Description = Loc.Get("deeper_itut_video_step3_body"),
                    TargetElementName = "TxtMetaName",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.Manual
                },

                // Phase 2 - preview
                new TutorialStep
                {
                    Id = "ivid_play",
                    Icon = "▶",
                    Title = Loc.Get("deeper_itut_video_step4_title"),
                    Description = Loc.Get("deeper_itut_video_step4_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "ivid_pause",
                    Icon = "⏸",
                    Title = Loc.Get("deeper_itut_video_step5_title"),
                    Description = Loc.Get("deeper_itut_video_step5_body"),
                    TargetElementName = "BtnPlayPause",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 3 - Add Haptic effect (same as HT/Audio for parallelism)
                new TutorialStep
                {
                    Id = "ivid_addeffect",
                    Icon = "✨",
                    Title = Loc.Get("deeper_itut_video_step6_title"),
                    Description = Loc.Get("deeper_itut_video_step6_body"),
                    TargetElementName = "BtnAddEffectHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "EffectAdded"
                },
                new TutorialStep
                {
                    Id = "ivid_intensity",
                    Icon = "🎚",
                    Title = Loc.Get("deeper_itut_video_step7_title"),
                    Description = Loc.Get("deeper_itut_video_step7_body"),
                    TargetElementName = "SliderHapticIntensity",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSliderAtLeast,
                    AdvanceMinValue = 0.3,
                    AdvanceMaxValue = 0.7
                },
                new TutorialStep
                {
                    Id = "ivid_pattern",
                    Icon = "🌊",
                    Title = Loc.Get("deeper_itut_video_step8_title"),
                    Description = Loc.Get("deeper_itut_video_step8_body"),
                    TargetElementName = "CmbHapticPattern",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "ivid_test",
                    Icon = "🎮",
                    Title = Loc.Get("deeper_itut_video_step9_title"),
                    Description = Loc.Get("deeper_itut_video_step9_body"),
                    TargetElementName = "BtnTestHaptic",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
                    AllowManualSkip = true
                },

                // Phase 4 - Add Rule, switch trigger to AttentionLost (gaze)
                new TutorialStep
                {
                    Id = "ivid_addrule",
                    Icon = "🔗",
                    Title = Loc.Get("deeper_itut_video_step10_title"),
                    Description = Loc.Get("deeper_itut_video_step10_body"),
                    TargetElementName = "BtnAddRuleHero",
                    TextPosition = TutorialStepPosition.Bottom,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "RuleAdded"
                },
                new TutorialStep
                {
                    Id = "ivid_ruletrigger",
                    Icon = "👁",
                    Title = Loc.Get("deeper_itut_video_step11_title"),
                    Description = Loc.Get("deeper_itut_video_step11_body"),
                    TargetElementName = "CmbTriggerType",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AdvanceValue = "attention_lost",
                    MatchByTag = true,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "ivid_ruleaction",
                    Icon = "⚡",
                    Title = Loc.Get("deeper_itut_video_step12_title"),
                    Description = Loc.Get("deeper_itut_video_step12_body"),
                    TargetElementName = "CmbActionType",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
                    AdvanceValue = "screen_shake",
                    MatchByTag = true,
                    AllowManualSkip = true
                },
                new TutorialStep
                {
                    Id = "ivid_actionintensity",
                    Icon = "🎚",
                    Title = Loc.Get("deeper_itut_video_step13_title"),
                    Description = Loc.Get("deeper_itut_video_step13_body"),
                    TargetElementName = "TutorialActionIntensityField",
                    TextPosition = TutorialStepPosition.Left,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
                    AdvanceValue = "0.7"
                },

                // Phase 5 - Save
                new TutorialStep
                {
                    Id = "ivid_save",
                    Icon = "💾",
                    Title = Loc.Get("deeper_itut_video_step14_title"),
                    Description = Loc.Get("deeper_itut_video_step14_body"),
                    TargetElementName = "BtnEditorSave",
                    TextPosition = TutorialStepPosition.Top,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
                },
                new TutorialStep
                {
                    Id = "ivid_savedialog",
                    Icon = "💾",
                    Title = Loc.Get("deeper_itut_video_step15_title"),
                    Description = Loc.Get("deeper_itut_video_step15_body"),
                    TextPosition = TutorialStepPosition.Center,
                    AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
                    AdvanceEventName = "FileSaved",
                    AllowManualSkip = true,
                    BlockBackgroundClicks = false
                },

                // Phase 6 - Follow-up card
                BuildInteractiveDoneCard(
                    "ivid_done",
                    Loc.Get("deeper_itut_video_step16_title"),
                    Loc.Get("deeper_itut_video_step16_body"))
            };
        }

        // Shared "your enhancement is saved" follow-up card with Open Folder /
        // Open Player / Done buttons. The HT flow has its own copy of this
        // because it predates the helper; new flows route through here so the
        // three buttons stay consistent across all interactive walkthroughs.
        private static TutorialStep BuildInteractiveDoneCard(string id, string title, string body)
        {
            return new TutorialStep
            {
                Id = id,
                Icon = "🎉",
                Title = title,
                Description = body,
                TextPosition = TutorialStepPosition.Center,
                IsFollowUpCard = true,
                FollowUpButton1Text = Loc.Get("deeper_itut_ht_followup_open_folder"),
                FollowUpAction1 = step =>
                {
                    try
                    {
                        var path = TutorialEventBus.LastSavedEnhancementPath;
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{path}\"",
                                UseShellExecute = true
                            });
                        }
                    }
                    catch { }
                    try { App.Tutorial?.Skip(); } catch { }
                },
                FollowUpButton2Text = Loc.Get("deeper_itut_ht_followup_open_player"),
                FollowUpAction2 = step =>
                {
                    try
                    {
                        if (App.DeeperPlayer != null && App.DeeperHost != null)
                        {
                            var owner = Application.Current?.MainWindow;
                            var win = new ConditioningControlPanel.Views.Deeper.EnhancementPlayerWindow(
                                App.DeeperPlayer, App.DeeperHost);
                            if (owner != null) win.Owner = owner;
                            win.Show();
                        }
                    }
                    catch { }
                    try { App.Tutorial?.Skip(); } catch { }
                },
                FollowUpButton3Text = Loc.Get("deeper_itut_ht_followup_done"),
                FollowUpAction3 = step =>
                {
                    try { App.Tutorial?.Skip(); } catch { }
                }
            };
        }

        #endregion
    }
}
