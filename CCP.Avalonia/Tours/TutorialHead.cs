using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Window = global::Avalonia.Controls.Window;

namespace ConditioningControlPanel.Avalonia.Tours
{
    /// <summary>
    /// The head half of the tour engine: WPF <c>App.Tutorial</c> plus <c>MainWindow.StartTutorial</c>
    /// (MainWindow/MainWindow.Settings.cs:540). Owns the one <see cref="TutorialService"/>, seeds the
    /// <see cref="CoreTutorial"/> seam every page already calls, and puts the overlay on the shell
    /// for the tours that walk the shell.
    ///
    /// Tours hosted by another window (Mod Creator, the Deeper editor and its New Enhancement dialog)
    /// only START here: that window shows its own <see cref="TutorialOverlay"/> over itself, as on WPF.
    /// </summary>
    internal static class TutorialHead
    {
        internal static TutorialService Service { get; } = new();

        /// <summary>Tours whose host window is not the shell. WPF starts these from that window.</summary>
        internal static readonly HashSet<TutorialType> HostedElsewhere = new()
        {
            TutorialType.Modding,
            TutorialType.DeeperEditor,
            TutorialType.DeeperEditorInteractiveHT, TutorialType.DeeperEditorInteractiveHTPart2,
            TutorialType.DeeperEditorInteractiveLocalAudio, TutorialType.DeeperEditorInteractiveLocalAudioPart2,
            TutorialType.DeeperEditorInteractiveLocalVideo, TutorialType.DeeperEditorInteractiveLocalVideoPart2,
        };

        /// <summary>
        /// Tour targets WPF 7.1.5 names that no control on this head carries. A step pointing at one
        /// still shows its card (centred, unspotlit: the overlay's own fallback), it is never skipped.
        /// Pinned by TutorialTargetsTests: a name leaves this list the day its control gets the name.
        /// "BtnProgression" is dead on WPF 7.1.5 too.
        /// </summary>
        internal static readonly string[] TargetsNotOnThisHead =
        {
            "BtnProgression",
            "TutorialActionIntensityField",
            "TutorialTriggerTimeField",
        };

        private static bool _seeded;
        private static TutorialOverlay? _shellOverlay;
        private static readonly ConditionalWeakTable<TutorialStep, CoreTutorial.Step> Projected = new();

        /// <summary>Idempotent. Called once at startup (App.axaml.cs) and by tests.</summary>
        internal static void Seed()
        {
            CoreTutorial.IsActiveProvider = () => Service.IsActive;
            CoreTutorial.CurrentStepProvider = () => Service.IsActive && Service.CurrentStep is { } s ? Project(s) : null;
            CoreTutorial.CurrentStepIndexProvider = () => Service.IsActive ? Service.CurrentStepIndex : 0;
            CoreTutorial.TotalStepsProvider = () => Service.IsActive ? Service.TotalSteps : 0;
            CoreTutorial.NextAction = Service.Next;
            CoreTutorial.PreviousAction = Service.Previous;
            CoreTutorial.SkipAction = Service.Skip;
            CoreTutorial.StartAction = StartByName;

            if (_seeded) return;      // the seam is re-pointed every call, the events are wired once
            _seeded = true;

            Service.StepChanged += (s, step) => CoreTutorial.RaiseStepChanged(s, Project(step));
            Service.TutorialFinished += (s, e) => CoreTutorial.RaiseFinished(s, e.Completed);
        }

        /// <summary>The seam's Start: a WPF TutorialType name. An unknown name starts nothing.</summary>
        internal static void StartByName(string name)
        {
            if (!Enum.TryParse<TutorialType>(name, ignoreCase: false, out var type) || !Enum.IsDefined(type)) return;
            if (HostedElsewhere.Contains(type)) { Service.Start(type); return; }
            StartOnShell(type);
        }

        /// <summary>WPF MainWindow.StartTutorial: one tour at a time, tab callbacks bound to the
        /// shell the overlay sits on, then the overlay.</summary>
        internal static void StartOnShell(TutorialType type)
        {
            if (_shellOverlay != null) return;
            var shell = TutorialHeadHooks.Shell;
            if (shell == null) return;

            Service.ConfigureCallbacks(
                showSettings: () => shell.ShowTab("settings"),
                showPresets: () => shell.ShowTab("presets"),
                showProgression: () => shell.ShowTab("progression"),
                showAchievements: () => shell.ShowTab("achievements"),
                showCompanion: () => shell.ShowTab("companion"),
                // WPF routes the dead "patreon" key to the App Info popup; no step asks for it
                // since the account cards moved to Settings, Account.
                showPatreon: () => shell.ShowTab("appsettings"),
                showAwareness: () => shell.ShowTab("awareness"),
                showDeeper: () => shell.ShowTab("deeper"),
                showTab: key => shell.ShowTab(key));

            Service.Start(type);
            if (!Service.IsActive) return;

            var overlay = new TutorialOverlay(shell);
            _shellOverlay = overlay;
            overlay.Closed += (_, _) => { if (ReferenceEquals(_shellOverlay, overlay)) _shellOverlay = null; };
            overlay.Show();
        }

        /// <summary>Panic: a tour ends at once, by the abandon route (never latched as walked).</summary>
        internal static void OnPanic()
        {
            try { if (Service.IsActive) Service.Skip(); } catch { /* panic never throws */ }
        }

        internal const string PanicSurfaceId = "tour";

        /// <summary>Puts the tour stop in the panic registry at runtime, as LeashTaskHost.HookPanic
        /// does, right after "intake". Idempotent.</summary>
        internal static void HookPanic()
        {
            var all = PanicSurfaces.All.ToList();
            if (all.Any(s => s.Id == PanicSurfaceId)) return;
            var at = all.FindIndex(s => s.Id == "intake") + 1;
            all.Insert(at, new PanicSurfaces.Surface(PanicSurfaceId, _ => OnPanic()));
            PanicSurfaces.All = all.ToArray();
        }

        /// <summary>One snapshot per head step, so the overlay's "is this still my step" check holds.</summary>
        internal static CoreTutorial.Step Project(TutorialStep s) => Projected.GetValue(s, static step => new CoreTutorial.Step
        {
            Id = step.Id,
            Title = step.Title,
            Description = step.Description,
            Icon = step.Icon,
            TargetElementName = step.TargetElementName,
            TargetWindowTypeName = step.TargetWindowTypeName,
            TextPosition = (CoreTutorial.StepPosition)(int)step.TextPosition,
            Advance = (CoreTutorial.AdvanceTrigger)(int)step.AdvanceTrigger,
            AdvanceEventName = step.AdvanceEventName,
            AllowManualSkip = step.AllowManualSkip,
            IsFollowUpCard = step.IsFollowUpCard,
            BlockBackgroundClicks = step.BlockBackgroundClicks,
            FollowUp1Text = step.FollowUpButton1Text,
            FollowUp2Text = step.FollowUpButton2Text,
            FollowUp3Text = step.FollowUpButton3Text,
            FollowUp1 = step.FollowUpAction1 == null ? null : () => step.FollowUpAction1(step),
            FollowUp2 = step.FollowUpAction2 == null ? null : () => step.FollowUpAction2(step),
            FollowUp3 = step.FollowUpAction3 == null ? null : () => step.FollowUpAction3(step),
        });

        /// <summary>The head step behind the seam's current card (the overlay needs the parts that do
        /// not cross: the prep action, the advance values, the tab).</summary>
        internal static TutorialStep? CurrentHeadStep => Service.IsActive ? Service.CurrentStep : null;

        /// <summary>Every step list by tour, for tests and the target audit.</summary>
        internal static IEnumerable<(TutorialType Tour, TutorialStep Step)> AllSteps()
        {
            var probe = new TutorialService();
            foreach (var type in Enum.GetValues<TutorialType>())
                foreach (var step in probe.StepsFor(type))
                    yield return (type, step);
        }

        /// <summary>Test seam: forget the shell overlay (a test shell closed under it).</summary>
        internal static void ResetForTests()
        {
            try { if (Service.IsActive) Service.Skip(); } catch { }
            _shellOverlay = null;
        }
    }
}
