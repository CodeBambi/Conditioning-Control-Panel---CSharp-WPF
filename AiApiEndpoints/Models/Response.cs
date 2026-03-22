namespace AiApiEndpoints.Models;

public class Response
{
    public required RawData Data { get; set; }
    public required Session Session { get; set; }
    public bool IsDone { get; set; }
}