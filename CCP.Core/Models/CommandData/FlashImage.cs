namespace ConditioningControlPanel.Models.CommandData
{
    public record FlashImage(
        int Amount,
        int Duration,
        int Size,
        int Opacity
    ) : IAiCommandData;
}
