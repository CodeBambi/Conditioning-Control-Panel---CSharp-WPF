using System;
using System.Windows;

namespace ConditioningControlPanel.Models
{
    public enum TutorialStepPosition
    {
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    public enum TutorialAdvanceTrigger
    {
        Manual,
        OnButtonClick,
        OnTextEquals,
        OnSelectionEquals,
        OnSliderAtLeast,
        OnEvent
    }

    public class TutorialStep
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public string? TargetElementName { get; set; }
        public string? RequiresTab { get; set; }
        public TutorialStepPosition TextPosition { get; set; } = TutorialStepPosition.Bottom;
        public Action? OnActivate { get; set; }

        // Invoked by TutorialOverlay.UpdateSpotlight just before it tries to
        // locate TargetElementName, so a step can guarantee its target is
        // measurable. The editor uses this to ExpandMetadataDrawer() before
        // any step that points at a field inside the (default-collapsed)
        // metadata drawer — without it, FindElementByName returns the element
        // but GetElementBounds reports 0,0/0x0 and the overlay's retry timer
        // never converges (worst failure mode from the tutorials recon).
        public Action<Window>? PrepareTargetWindowAction { get; set; }

        // --- Interactive on-rails support ---

        public TutorialAdvanceTrigger AdvanceTrigger { get; set; } = TutorialAdvanceTrigger.Manual;

        public string? AdvanceValue { get; set; }

        public double AdvanceMinValue { get; set; }

        public double AdvanceMaxValue { get; set; } = double.NaN;

        public string? AdvanceEventName { get; set; }

        public bool AllowManualSkip { get; set; } = false;

        public string? TargetWindowTypeName { get; set; }

        public bool IsFollowUpCard { get; set; } = false;

        public bool MatchByTag { get; set; } = false;

        // True (default): the dim overlay absorbs clicks outside any spotlight
        // hole or card. False: clicks pass through the dim, useful when the
        // user needs to interact with something on top of the overlay (e.g.
        // an OS file dialog while we wait for a FileSaved event).
        public bool BlockBackgroundClicks { get; set; } = true;

        public Action<TutorialStep>? FollowUpAction1 { get; set; }
        public Action<TutorialStep>? FollowUpAction2 { get; set; }
        public Action<TutorialStep>? FollowUpAction3 { get; set; }
        public string? FollowUpButton1Text { get; set; }
        public string? FollowUpButton2Text { get; set; }
        public string? FollowUpButton3Text { get; set; }
    }
}
