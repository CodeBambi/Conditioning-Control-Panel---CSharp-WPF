namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// One enumerable bark line, surfaced to the Phrase Manager. Lifted out of
    /// <c>BarkService</c> (where it was a nested type) so the Phrase Manager on a head without a
    /// bark engine can still name the shape it reads through <see cref="CoreBark.AllLines"/>.
    /// Nothing referenced it as <c>BarkService.BarkLineInfo</c>, so the lift is source-compatible.
    /// </summary>
    public readonly record struct BarkLineInfo(
        string LineId, string RuleId, string Trigger, string Text, string? AudioFileName, string? AudioFolder);
}
