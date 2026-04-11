using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.CommandData;

public record HapticCommandData 
(
     double Intensity,
    int Duration
) : IAiCommandData;