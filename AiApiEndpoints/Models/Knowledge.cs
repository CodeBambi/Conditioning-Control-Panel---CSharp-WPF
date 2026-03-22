namespace AiApiEndpoints.Models;

public class Knowledge
{
    public List<File> Files { get; set; }
    public List<Triggers> Triggers { get; set; }
    public List<Kinks> Kinks { get; set; }
}