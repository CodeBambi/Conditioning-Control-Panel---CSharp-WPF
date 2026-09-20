using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Deeper
{
    public class EnhancementLibraryEntry
    {
        public string FilePath { get; set; } = "";
        public string Name { get; set; } = "";
        public string Creator { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string MediaSource { get; set; } = "";
        public DateTime LastModified { get; set; }
        // Hardware-gating tags detected by EnhancementAutoTagger at save time;
        // surfaced by the catalogue browser so downloaders can see equipment
        // requirements at a glance. May be empty for files saved by older builds.
        public List<string> AutoTags { get; set; } = new();
        // Media length in seconds; 0 when nobody knows yet. Filled from the
        // enhancement file's media_duration, with head-owned cache/probe backfill
        // allowed after the list shows.
        public double DurationSeconds { get; set; }
    }
}
