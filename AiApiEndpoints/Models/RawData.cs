namespace AiApiEndpoints.Models;

public class RawData
{
    public Guid Id { get; set; }
    
    public IBaseModel Data { get; set; }
}