using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.AIService
{
    public interface IAiResponseParser
    {
        ParsedAiResponse Parse(string response);
    }

    public class ParsedAiResponse
    {
        public string CleanText { get; set; } = string.Empty;
        public List<AiCommandData> Commands { get; set; } = new();
    }
}
