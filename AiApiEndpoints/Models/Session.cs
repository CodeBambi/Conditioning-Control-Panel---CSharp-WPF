namespace AiApiEndpoints.Models;

public class Session : IBaseModel
{
    public bool IsPrivate => true;
    public bool AllowLocal => true;
}