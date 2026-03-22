namespace AiApiEndpoints.Models;

public class Triggers : IBaseModel
{
    public required string Trigger { get; set; }
    public string? Description { get; set; }
}