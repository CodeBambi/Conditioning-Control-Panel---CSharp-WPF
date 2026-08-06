using System;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The plain implementation of <see cref="IAwarenessAppChipVm"/> — a label, a tooltip and one
    /// command. Used by the mock gallery and by the runtime viewmodel; there is nothing to specialise.
    /// </summary>
    public sealed class AwarenessAppChip : IAwarenessAppChipVm
    {
        public AwarenessAppChip(string label, string actionTip, ICommand? action = null)
        {
            Label = label ?? string.Empty;
            ActionTip = actionTip ?? string.Empty;
            ActionCommand = action ?? CompanionRelayCommand.NoOp("awareness.appChip");
        }

        /// <summary>Convenience for the runtime VM: wraps a plain callback.</summary>
        public AwarenessAppChip(string label, string actionTip, Action action)
            : this(label, actionTip, new CompanionRelayCommand(action)) { }

        public string Label { get; }
        public string ActionTip { get; }
        public ICommand ActionCommand { get; }
    }
}
