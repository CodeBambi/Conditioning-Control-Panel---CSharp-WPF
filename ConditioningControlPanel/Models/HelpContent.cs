using System.Collections.Generic;

namespace ConditioningControlPanel.Models
{
    public class HelpContent
    {
        public string SectionId { get; set; } = "";
        public string Icon { get; set; } = "?";
        public string Title { get; set; } = "";
        public string WhatItDoes { get; set; } = "";
        public List<string> Tips { get; set; } = new();
        public string HowItWorks { get; set; } = "";

        public bool HasTips => Tips?.Count > 0;
        public bool HasHowItWorks => !string.IsNullOrEmpty(HowItWorks);
    }
}
