using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.CommandData;

public record HapticCommand 
(
     double Intensity,
    int Duration
) : AICommandData;