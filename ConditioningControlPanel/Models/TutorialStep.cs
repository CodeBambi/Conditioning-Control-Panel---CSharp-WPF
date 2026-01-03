using System;

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

    public class TutorialStep
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public string? TargetElementName { get; set; }
        public TutorialStepPosition TextPosition { get; set; } = TutorialStepPosition.Bottom;
        public Action? OnActivate { get; set; }
    }
}
