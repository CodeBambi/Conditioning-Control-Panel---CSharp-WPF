using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    public class TutorialService
    {
        private readonly List<TutorialStep> _steps;
        private int _currentStepIndex = 0;

        public event EventHandler<TutorialStep>? StepChanged;
        public event EventHandler? TutorialStarted;
        public event EventHandler? TutorialCompleted;

        public TutorialStep? CurrentStep =>
            _currentStepIndex >= 0 && _currentStepIndex < _steps.Count
                ? _steps[_currentStepIndex]
                : null;

        public int CurrentStepIndex => _currentStepIndex;
        public int TotalSteps => _steps.Count;
        public bool IsActive { get; private set; }
        public bool IsFirstStep => _currentStepIndex == 0;
        public bool IsLastStep => _currentStepIndex == _steps.Count - 1;

        public TutorialService()
        {
            _steps = CreateTutorialSteps();
        }

        /// <summary>
        /// Configure OnActivate callbacks with MainWindow actions
        /// </summary>
        public void ConfigureCallbacks(
            Action showSettings,
            Action showPresets,
            Action showProgression,
            Action showAchievements,
            Action openSessionEditor,
            Action openLinktree)
        {
            foreach (var step in _steps)
            {
                switch (step.Id)
                {
                    case "settings_tab":
                        step.OnActivate = showSettings;
                        break;
                    case "scheduler":
                    case "ramp":
                        step.OnActivate = showProgression;
                        break;
                    case "presets":
                    case "sessions":
                        step.OnActivate = showPresets;
                        break;
                    case "session_editor":
                        step.OnActivate = () => { showPresets(); openSessionEditor(); };
                        break;
                    case "level_system":
                        step.OnActivate = showProgression;
                        break;
                    case "achievements":
                        step.OnActivate = showAchievements;
                        break;
                    case "support":
                        step.OnActivate = showSettings;
                        break;
                }
            }
        }

        private List<TutorialStep> CreateTutorialSteps()
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
                    TargetElementName = null,
                    TextPosition = TutorialStepPosition.Center
                },
                new TutorialStep
                {
                    Id = "settings_tab",
                    Icon = ">",
                    Title = "Settings Tab",
                    Description = "This is your main configuration area. Toggle features on/off, " +
                                  "adjust frequencies, opacity, and more. Hover over any setting to see what it does.",
                    TargetElementName = "BtnSettings",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "scheduler",
                    Icon = ">",
                    Title = "Scheduler",
                    Description = "Set up automatic start times! The scheduler can automatically begin your session " +
                                  "during specific hours and days. Perfect for building a consistent routine.",
                    TargetElementName = "SchedulerPanel",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "ramp",
                    Icon = ">",
                    Title = "Intensity Ramp",
                    Description = "Gradually increase effect intensity over time. Start gentle and build up - " +
                                  "the ramp multiplies your settings over the duration you choose. Great for longer sessions!",
                    TargetElementName = "RampPanel",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "presets",
                    Icon = ">",
                    Title = "Presets",
                    Description = "Save your favorite settings as presets so you can quickly switch between configurations. " +
                                  "Create different presets for different moods or activities!",
                    TargetElementName = "BtnPresets",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "sessions",
                    Icon = ">",
                    Title = "Sessions - Try All Features!",
                    Description = "Sessions are timed experiences with carefully crafted effect sequences. " +
                                  "Sessions are a good way to test features you haven't already unlocked - " +
                                  "they bypass level requirements so you can preview everything!",
                    TargetElementName = "SessionsPanel",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "session_editor",
                    Icon = ">",
                    Title = "Session Editor",
                    Description = "Create your own custom sessions! Design timed experiences with specific effect " +
                                  "combinations, phases, and intensities. Share them with the community or keep them private.",
                    TargetElementName = "BtnCreateSession",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "level_system",
                    Icon = ">",
                    Title = "Level & XP System",
                    Description = "Gain XP by using the app and running sessions. New features unlock as you level up. " +
                                  "Check the Progression tab to see all unlockable features and your current progress.",
                    TargetElementName = "BtnProgression",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "achievements",
                    Icon = ">",
                    Title = "Achievements",
                    Description = "Unlock achievements by reaching milestones, using features, and completing sessions. " +
                                  "Can you collect them all? Check your progress here!",
                    TargetElementName = "BtnAchievements",
                    TextPosition = TutorialStepPosition.Right
                },
                new TutorialStep
                {
                    Id = "help_button",
                    Icon = "?",
                    Title = "Need Help?",
                    Description = "Click this button anytime to see the help overlay with tips and instructions. " +
                                  "You can also restart this tutorial from there!",
                    TargetElementName = "BtnMainHelp",
                    TextPosition = TutorialStepPosition.Left
                },
                new TutorialStep
                {
                    Id = "support",
                    Icon = "<3",
                    Title = "Enjoying the App?",
                    Description = "If you're enjoying Conditioning Control Panel, please consider supporting the project! " +
                                  "Your support helps fund development of new features and keeps the app free for everyone.",
                    TargetElementName = "BtnSettings",
                    TextPosition = TutorialStepPosition.Right
                }
            };
        }

        public void Start()
        {
            _currentStepIndex = 0;
            IsActive = true;
            TutorialStarted?.Invoke(this, EventArgs.Empty);
            if (CurrentStep != null)
            {
                CurrentStep.OnActivate?.Invoke();
                StepChanged?.Invoke(this, CurrentStep);
            }
        }

        public void Next()
        {
            if (!IsActive) return;

            if (_currentStepIndex < _steps.Count - 1)
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
    }
}
