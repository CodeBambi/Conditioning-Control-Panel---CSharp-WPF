namespace ConditioningControlPanel.Models.CommandData
{
    /// <summary>
    /// Compact, AI-friendly summary of a single command attempted in the previous turn.
    /// Included in the enrichment context so the model knows whether its effect requests
    /// were honored, blocked, or failed.
    /// </summary>
    public record CommandOutcome(
        string Command,
        bool Succeeded,
        string? Outcome);
}
