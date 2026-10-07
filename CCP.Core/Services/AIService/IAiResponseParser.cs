using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.AIService
{
    public interface IAiResponseParser
    {
        ParsedAiResponse Parse(string response);

        /// <summary>
        /// Strips leaked context-metadata tags (e.g. an echoed
        /// <c>[Category: … | App: … | Title: … | Duration: …]</c>) from user-visible text.
        /// Must run BEFORE output moderation: the echoed Title carries the user's raw
        /// window/tab title, which must not be able to trip the guard and land a false
        /// model-output hit in the compliance log. <see cref="Parse"/> already applies this
        /// to <see cref="ParsedAiResponse.CleanText"/>; this entry point exists for the
        /// no-effects path that never calls Parse.
        /// </summary>
        string SanitizeVisibleText(string? response);
    }

    public class ParsedAiResponse
    {
        public string CleanText { get; set; } = string.Empty;
        public List<AiCommandData> Commands { get; set; } = new();
    }
}
