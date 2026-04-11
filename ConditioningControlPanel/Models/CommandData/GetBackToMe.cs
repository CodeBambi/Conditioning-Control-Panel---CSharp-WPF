namespace ConditioningControlPanel.Models.CommandData;

public record GetBackToMe(
    int Delay,
    string Token, 
    List<AiCommandData>? Commands, 
    string? Text, 
    bool JsonOnly,
    bool Stop = false
): IAiCommandData;