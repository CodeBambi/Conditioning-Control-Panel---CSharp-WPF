using System.Collections.Generic;

namespace ConditioningControlPanel.Models.CommandData
{
    public record GetBackToMe(
        int Delay,
        string Token,
        List<AiCommandData>? Commands,
        string? Text,
        bool JsonOnly
    ) : IAiCommandData
    {
        string? IAiCommandData.Token => Token;
    }
}
