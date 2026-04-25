namespace ConditioningControlPanel.Models.CommandData
{
    public record Subliminal(
        string Text,
        int Opacity
    ) : IAiCommandData;
}
