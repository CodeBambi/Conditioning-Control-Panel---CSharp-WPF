using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>platform#1: the typed-word engine ported from WPF KeywordTriggerService (buffer, whole word,
    /// cooldowns, preset priority, merge de-dup, app scope).</summary>
    public class KeywordTriggerEngineTests
    {
        private static (KeywordTriggerEngine Engine, AppSettings Settings, List<KeywordFire> Fires, List<string> Fired)
            Make(params KeywordTrigger[] triggers)
        {
            var s = new AppSettings { KeywordTriggersEnabled = true };
            s.KeywordTriggers = new List<KeywordTrigger>(triggers);
            var now = new DateTime(2026, 10, 9, 12, 0, 0);
            var e = new KeywordTriggerEngine { HasAccess = () => true, Settings = () => s, Now = () => now };
            var fires = new List<KeywordFire>();
            var fired = new List<string>();
            e.Dispatch += fires.Add;
            e.TriggerFired += (t, _) => fired.Add(t.Keyword);
            e.Start();
            return (e, s, fires, fired);
        }

        private static void Type(KeywordTriggerEngine e, string text)
        {
            foreach (var c in text)
                if (c == ' ') e.OnKey(KeywordBufferKey.Space); else e.OnChar(c);
        }

        [Fact]
        public void TypedWord_Fires_WholeWordOnly()
        {
            var (e, _, fires, fired) = Make(KeywordTriggerEngine.NewCustomTrigger("sit"));
            Type(e, "intensity ");
            Assert.Empty(fires);
            Type(e, "now sit");
            Assert.Single(fires);
            Assert.Equal(new[] { "sit" }, fired);
            Assert.Equal("", e.BufferText);   // cleared after a fire
        }

        [Fact]
        public void Backspace_And_Clear_Edit_The_Buffer()
        {
            var (e, _, fires, _) = Make(KeywordTriggerEngine.NewCustomTrigger("drop"));
            Type(e, "drox");
            e.OnKey(KeywordBufferKey.Backspace);
            e.OnKey(KeywordBufferKey.Clear);
            Type(e, "p");
            Assert.Empty(fires);
        }

        [Fact]
        public void Master_Off_Or_No_Access_Never_Fires()
        {
            var (e, s, fires, _) = Make(KeywordTriggerEngine.NewCustomTrigger("obey"));
            s.KeywordTriggersEnabled = false;
            Type(e, "obey");
            s.KeywordTriggersEnabled = true;
            e.HasAccess = () => false;
            Type(e, " obey");
            Assert.Empty(fires);
        }

        [Fact]
        public void Global_Cooldown_And_Mute_Hold_A_Second_Fire()
        {
            var (e, _, fires, _) = Make(KeywordTriggerEngine.NewCustomTrigger("obey"));
            Type(e, "obey");
            Type(e, " obey");
            Assert.Single(fires);
            Assert.True(e.IsKeywordMuted("obey"));
        }

        [Fact]
        public void Two_Triggers_Merge_Once_Preset_First_With_Deduped_Actions()
        {
            var custom = KeywordTriggerEngine.NewCustomTrigger("good boy");
            var preset = KeywordTriggerEngine.NewCustomTrigger("boy");
            preset.Id = "preset:puppy:boy";
            var (e, _, fires, fired) = Make(custom, preset);
            e.CheckTextForMatches("such a good   boy");
            var fire = Assert.Single(fires);
            Assert.Equal(new[] { "boy", "good boy" }, fired);
            Assert.Equal("boy", fire.Trigger.Keyword);
            Assert.Equal(fire.Actions.Count, new HashSet<string>(System.Linq.Enumerable.Select(fire.Actions, KeywordTriggerEngine.DedupKey)).Count);
            Assert.Equal(2, fire.Fired.Count);
        }

        [Fact]
        public void AppScope_OnlyListed_Fails_Closed_And_Allows_Listed()
        {
            var (e, s, fires, _) = Make(KeywordTriggerEngine.NewCustomTrigger("obey"));
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            s.KeywordTriggerApps = KeywordTriggerEngine.ParseAppList("discord.exe; Chrome,discord");
            Assert.Equal(new[] { "discord", "Chrome" }, s.KeywordTriggerApps);
            e.ForegroundResolver = () => null;
            Type(e, "obey");
            Assert.Empty(fires);
            e.ForegroundResolver = () => new ForegroundApp("Discord", false);
            Type(e, " obey");
            Assert.Single(fires);
        }

        [Fact]
        public void Import_Skips_Existing_Keywords()
        {
            var s = new AppSettings();
            s.KeywordTriggers = new List<KeywordTrigger> { KeywordTriggerEngine.NewCustomTrigger("Obey") };
            s.CustomTriggers = new List<string> { "obey", "sink", " " };
            var imported = KeywordTriggerEngine.ImportFromCustomTriggers(s);
            var t = Assert.Single(imported);
            Assert.Equal("sink", t.Keyword);
            Assert.NotEmpty(t.Actions);
        }
    }
}
