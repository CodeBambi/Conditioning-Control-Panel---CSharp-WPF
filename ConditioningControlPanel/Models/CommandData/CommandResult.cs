namespace ConditioningControlPanel.Models.CommandData
{
    /// <summary>
    /// Describes what happened when an AI-emitted effect command was executed.
    /// </summary>
    public enum CommandResultStatus
    {
        /// <summary>The command was dispatched and completed normally.</summary>
        Executed,

        /// <summary>The command was dropped by a gate (master toggle off, effect disabled, cap reached, invalid data).</summary>
        Rejected,

        /// <summary>The executor ran but threw or otherwise failed to apply the effect.</summary>
        Failed,

        /// <summary>The command was a no-op because the desired state was already active (e.g. video already playing).</summary>
        NoOp
    }

    /// <summary>
    /// Typed result returned by <see cref="Services.Commands.ICommand.ExecuteAsync"/> and
    /// consumed by <see cref="Services.Commands.AiCommandService"/> to build
    /// <see cref="CommandOutcome"/> records for the AI context.
    /// </summary>
    public record CommandResult(
        string CommandType,
        CommandResultStatus Status,
        string? Reason = null,
        string? ParameterSummary = null);
}
