using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The tutorial seam: what a coach-mark overlay needs to draw a tour, and the verbs that move
    /// it. The tour ITSELF - twenty-two step lists, the tab callbacks, the completion ledger -
    /// stays in the head that owns them (<c>Services/TutorialService.cs</c> on Windows), because
    /// every step is a sentence about that head's controls and half of them invoke head-side
    /// navigation on activation.
    ///
    /// <para>What crosses is a per-step SNAPSHOT plus the cursor over the list. That is enough for
    /// an overlay to render a card, place it, spotlight a named control, count the steps and walk
    /// forwards, backwards or out - and it is deliberately not enough to author a tour, which is
    /// the half that is not portable.</para>
    ///
    /// <para><b>What is NOT here, and why.</b> Nothing that names a window or a control type:
    /// <c>TutorialStep.PrepareTargetWindowAction</c> is an <c>Action&lt;System.Windows.Window&gt;</c>
    /// on Windows and would have to be a different type per head; the spotlight cut-out, the input
    /// region and the sidebar door expansion are pixels and OS calls, so they stay in the overlay.
    /// The auto-advance triggers cross only as the <see cref="AdvanceTrigger"/> ENUM - the actual
    /// subscription hooks a real control on a real head, and that head calls <see cref="Next"/>
    /// when it fires.</para>
    ///
    /// <para><b>Unseeded is "no tour".</b> Not active, no current step, index 0, 0 steps, first step
    /// and not last. <see cref="Next"/>/<see cref="Previous"/>/<see cref="Skip"/>/<see cref="Start"/>
    /// are silent no-ops. That is the truth on a head with no tutorial service, not a placebo: an
    /// overlay reading this seam draws nothing and a "bail while a tour is running" gate stays
    /// open. Nothing here throws, on any path.</para>
    ///
    /// <para>The <see cref="Step"/> snapshot and its two enums are NESTED on purpose. The Windows
    /// head's own <c>TutorialStep</c> / <c>TutorialStepPosition</c> / <c>TutorialAdvanceTrigger</c>
    /// live in <c>ConditioningControlPanel.Models</c> and are used unqualified from inside
    /// <c>namespace ConditioningControlPanel.Services</c>. C# searches enclosing namespaces before
    /// using-directives, so a top-level <c>ConditioningControlPanel.TutorialStep</c> declared here
    /// would silently rebind every bare mention of it in that 3,196-line file. Nested, they can
    /// only be named as <c>CoreTutorial.Step</c>.</para>
    /// </summary>
    public static class CoreTutorial
    {
        /// <summary>Where the card sits relative to the spotlit control. Mirrors
        /// <c>Models.TutorialStepPosition</c>.</summary>
        public enum StepPosition { Top, Bottom, Left, Right, Center }

        /// <summary>What moves the tour off this step. Mirrors <c>Models.TutorialAdvanceTrigger</c>.
        /// Only <see cref="Manual"/> is acted on in Core - it decides whether the card shows a Next
        /// button; the rest tell the head's overlay which control to watch.</summary>
        public enum AdvanceTrigger { Manual, OnButtonClick, OnTextEquals, OnSelectionEquals, OnSliderAtLeast, OnEvent }

        /// <summary>
        /// One card's worth of tour, as data. A SNAPSHOT: the head may project a fresh instance on
        /// every read, so compare steps by <see cref="CurrentStepIndex"/>, never by reference.
        /// </summary>
        public sealed class Step
        {
            public string Id { get; init; } = "";
            public string Title { get; init; } = "";
            public string Description { get; init; } = "";
            public string Icon { get; init; } = "";

            /// <summary>The x:Name of the control to spotlight, or null for a centred card.</summary>
            public string? TargetElementName { get; init; }

            /// <summary>Type name of the window the target lives in, when it is not the one the
            /// overlay is attached to. The retarget itself is head work.</summary>
            public string? TargetWindowTypeName { get; init; }

            public StepPosition TextPosition { get; init; } = StepPosition.Bottom;
            public AdvanceTrigger Advance { get; init; } = AdvanceTrigger.Manual;

            /// <summary>The card offers "Skip step" as well as waiting for the trigger.</summary>
            public bool AllowManualSkip { get; init; }

            /// <summary>A branch card: no Skip/Next row, a stack of follow-up buttons instead.</summary>
            public bool IsFollowUpCard { get; init; }

            /// <summary>True (default): the dim absorbs clicks outside the hole and the card.
            /// False when the user must work something ON TOP of the overlay.</summary>
            public bool BlockBackgroundClicks { get; init; } = true;

            public string? FollowUp1Text { get; init; }
            public string? FollowUp2Text { get; init; }
            public string? FollowUp3Text { get; init; }

            /// <summary>Already bound to their own step by the head that projected them, so they
            /// take no argument where the head's originals take the step.</summary>
            public Action? FollowUp1 { get; init; }
            public Action? FollowUp2 { get; init; }
            public Action? FollowUp3 { get; init; }
        }

        // ---- Seams the head assigns at startup ------------------------------------------

        public static volatile Func<bool>? IsActiveProvider;
        public static volatile Func<Step?>? CurrentStepProvider;
        public static volatile Func<int>? CurrentStepIndexProvider;
        public static volatile Func<int>? TotalStepsProvider;
        public static volatile Action? NextAction;
        public static volatile Action? PreviousAction;
        public static volatile Action? SkipAction;

        /// <summary>Start a tour by name - the head's tutorial-type name, e.g. "UpgradeTour". A
        /// string and not an enum so Core carries no second copy of a list only the head can act
        /// on; the head parses it and ignores a name it does not know.</summary>
        public static volatile Action<string>? StartAction;

        // ---- Readers --------------------------------------------------------------------

        /// <summary>A tour is on screen right now. False unseeded, which is what keeps a
        /// "not while a tutorial is running" gate open on a head that runs none.</summary>
        public static bool IsActive
        {
            get { try { return IsActiveProvider?.Invoke() ?? false; } catch { return false; } }
        }

        /// <summary>The card being shown, or null when no tour is running.</summary>
        public static Step? CurrentStep
        {
            get { try { return CurrentStepProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>Zero-based cursor into the current tour. 0 unseeded.</summary>
        public static int CurrentStepIndex
        {
            get { try { return CurrentStepIndexProvider?.Invoke() ?? 0; } catch { return 0; } }
        }

        /// <summary>How many cards the current tour has. 0 unseeded, which is what lets a caller
        /// tell "no tour" from "a one-card tour".</summary>
        public static int TotalSteps
        {
            get { try { return TotalStepsProvider?.Invoke() ?? 0; } catch { return 0; } }
        }

        /// <summary>Nothing to go back to. True unseeded (index 0), so a Previous button starts
        /// hidden rather than offering a move that does nothing.</summary>
        public static bool IsFirstStep => CurrentStepIndex <= 0;

        /// <summary>The last card, so Next finishes the tour rather than advancing. False when
        /// there is no tour at all - "Finish" over nothing is the state lie to avoid.</summary>
        public static bool IsLastStep
        {
            get { var n = TotalSteps; return n > 0 && CurrentStepIndex >= n - 1; }
        }

        // ---- Verbs ----------------------------------------------------------------------

        /// <summary>Advance, or finish on the last card. Silent when no tour is running.</summary>
        public static void Next() { try { NextAction?.Invoke(); } catch { /* a tour never blocks on UI quirks */ } }

        /// <summary>Back one card. Silent on the first card and when no tour is running.</summary>
        public static void Previous() { try { PreviousAction?.Invoke(); } catch { /* see Next */ } }

        /// <summary>Abandon the tour. Never latches it as completed - see the head's ledger.</summary>
        public static void Skip() { try { SkipAction?.Invoke(); } catch { /* see Next */ } }

        /// <summary>Start the named tour. A no-op when the head has no such tour, and unseeded.</summary>
        public static void Start(string tourName)
        {
            if (string.IsNullOrWhiteSpace(tourName)) return;
            try { StartAction?.Invoke(tourName); } catch { /* see Next */ }
        }

        // ---- Events the head forwards ---------------------------------------------------

        /// <summary>The tour moved to a different card. Raised on the thread the head raised its
        /// own event on - synchronously, because an overlay must have the new spotlight in place
        /// before the user's next click, and a Post would reintroduce exactly that race.</summary>
        public static event EventHandler<Step>? StepChanged;

        /// <summary>The tour ended, by any route. The bool is <c>true</c> only for the last card
        /// walked off the end; Escape, Skip and teardown all report <c>false</c>.</summary>
        public static event EventHandler<bool>? Finished;

        /// <summary>Head entry point for <see cref="StepChanged"/>. Swallows a subscriber's fault:
        /// the Windows tutorial service invokes its own StepChanged unguarded, so a throwing Core
        /// subscriber must not be able to break a WPF tour.</summary>
        public static void RaiseStepChanged(object? sender, Step step)
        {
            try { StepChanged?.Invoke(sender, step); } catch { /* a subscriber's fault is not the tour's */ }
        }

        /// <summary>Head entry point for <see cref="Finished"/>. Swallows - see
        /// <see cref="RaiseStepChanged"/>.</summary>
        public static void RaiseFinished(object? sender, bool completed)
        {
            try { Finished?.Invoke(sender, completed); } catch { /* see above */ }
        }
    }
}
