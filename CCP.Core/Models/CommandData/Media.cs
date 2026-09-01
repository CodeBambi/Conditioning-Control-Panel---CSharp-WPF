namespace ConditioningControlPanel.Models.CommandData
{
    public record Media(
        string Title,
        string Path,
        bool Random = false
    ) : IAiCommandData;
}
