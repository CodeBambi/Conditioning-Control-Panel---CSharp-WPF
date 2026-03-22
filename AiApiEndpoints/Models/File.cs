namespace AiApiEndpoints.Models;

public class File : IBaseModel
{
    public string Title { get; set; }
    public string FileName { get; set; }
    public string FileType { get; set; }
    public List<string> Triggers { get; set; }
    public List<string> Links { get; set; }
    public List<string> LocalPaths { get; set; }
}