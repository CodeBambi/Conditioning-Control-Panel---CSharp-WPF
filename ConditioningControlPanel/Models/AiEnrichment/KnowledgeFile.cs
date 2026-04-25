using System.Collections.Generic;

namespace ConditioningControlPanel.Models.AiEnrichment
{
    public class KnowledgeFile : IBaseModel
    {
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public List<string> Triggers { get; set; } = new();
        public List<string> Links { get; set; } = new();
        public List<string> LocalPaths { get; set; } = new();
    }
}
