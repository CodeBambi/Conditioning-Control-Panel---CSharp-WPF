using System;
using System.Collections.Generic;
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
        Modding         // Mod creation guide
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
            Action showPatreon)
        {
            _showSettings = showSettings;
            _showPresets = showPresets;
            _showProgression = showProgression;
            _showAchievements = showAchievements;
            _showCompanion = showCompanion;
            _showPatreon = showPatreon;
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
                    step.OnActivate = step.RequiresTab switch
                    {
                        "settings" => _showSettings,
                        "presets" => _showPresets,
                        "progression" => _showProgression,
                        "achievements" => _showAchievements,
                        "companion" => _showCompanion,
                        "patreon" => _showPatreon,
                        _ => null
                    };
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
            return new List<TutorialStep>
            {
                new TutorialStep
                {
                    Id = "comp_intro",
                    Icon = "💗",
                    Title = "Companion Tab Guide",
                    Description = "Configure your AI companion's behavior and appearance!",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "comp_speech",
                    Icon = "💬",
                    Title = "Speech Bubbles",
                    Description = "Configure what your companion says:\n" +
                                  "• Enable/disable speech bubbles\n" +
                                  "• Adjust frequency and duration\n" +
                                  "• Customize message categories",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "comp_triggers",
                    Icon = "⚡",
                    Title = "Trigger Messages",
                    Description = "Set up trigger responses:\n" +
                                  "• Messages on flash appearance\n" +
                                  "• Video start/end messages\n" +
                                  "• Custom trigger words",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "comp_personality",
                    Icon = "🎭",
                    Title = "AI Personality",
                    Description = "Customize your companion's AI personality:\n" +
                                  "• Adjust speaking style\n" +
                                  "• Set personality traits\n" +
                                  "• Configure response themes",
                    RequiresTab = "companion",
                    TextPosition = TutorialStepPosition.Center
                }
            };
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

        #endregion
    }
}
