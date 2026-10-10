using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.KeywordTriggers;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>Lane w2: the screen-read half of WPF KeywordTriggerService (CheckOcrWords): whole-token
    /// matching, the consecutive-scan gate, one fire per on-screen instance, phrases as one box, the
    /// app-scope and access gates, own-window exclusion.</summary>
    public class KeywordTriggerEngineOcrTests
    {
        private static (KeywordTriggerEngine Engine, AppSettings Settings, List<KeywordFire> Fires) Make(
            int confirmScans, params KeywordTrigger[] triggers)
        {
            var s = new AppSettings { KeywordTriggersEnabled = true, OcrConfirmationScans = confirmScans, OcrHighlightAll = true };
            s.KeywordGlobalCooldownSeconds = 0;
            s.AwarenessLoopProtectionEnabled = false;   // the echo mute runs on the real clock
            s.KeywordPerKeywordCooldownSeconds = 0;
            s.KeywordTriggers = new List<KeywordTrigger>(triggers);
            foreach (var t in triggers) t.CooldownSeconds = 0;
            var now = new DateTime(2026, 10, 10, 12, 0, 0);
            var e = new KeywordTriggerEngine { HasAccess = () => true, Settings = () => s, Now = () => now };
            var fires = new List<KeywordFire>();
            e.Dispatch += fires.Add;
            e.Start();
            return (e, s, fires);
        }

        /// <summary>The settings clamp every cooldown to at least a second, and the echo mute runs on
        /// the real clock: move the engine's clock on and wait the mute out.</summary>
        private static void PastTheCooldowns(KeywordTriggerEngine e)
        {
            var later = e.Now().AddMinutes(1);
            e.Now = () => later;
            System.Threading.Thread.Sleep(1100);
        }

        private static OcrWordHit W(string text, int x = 100, int y = 100) => new(text, x, y, 40, 16);

        [Fact]
        public void WholeToken_Only_And_Edge_Punctuation_Is_Stripped()
        {
            Assert.True(KeywordTriggerEngine.IsWholeWordMatch("sit.", "sit"));
            Assert.True(KeywordTriggerEngine.IsWholeWordMatch("'SIT',", "sit"));
            Assert.False(KeywordTriggerEngine.IsWholeWordMatch("sitting", "sit"));
            Assert.False(KeywordTriggerEngine.IsWholeWordMatch("intensity", "sit"));
            Assert.False(KeywordTriggerEngine.IsWholeWordMatch("...", "sit"));
        }

        [Fact]
        public void Phrase_Matches_Consecutive_Tokens_As_One_Union_Box()
        {
            var hits = new List<OcrWordHit>
            {
                new("be", 10, 10, 20, 12), new("good", 40, 12, 50, 14), new("girl,", 100, 10, 40, 12), new("good", 300, 300, 50, 14),
            };
            var found = KeywordTriggerEngine.FindMatchedWords("good girl", hits);
            var one = Assert.Single(found!);
            Assert.Equal("good girl", one.Text);
            Assert.Equal((40, 10, 100, 16), (one.X, one.Y, one.Width, one.Height));
        }

        [Fact]
        public void Fires_Only_After_The_Required_Consecutive_Scans()
        {
            var (e, _, fires) = Make(2, KeywordTriggerEngine.NewCustomTrigger("obey"));
            e.CheckOcrWords(new[] { W("obey"), W("other", 400, 400) });
            Assert.Empty(fires);
            Assert.True(e.NeedsOcrConfirmation);
            e.CheckOcrWords(new[] { W("obey") });
            var fire = Assert.Single(fires);
            Assert.Equal("OCR", fire.Source);
            Assert.Equal("obey", Assert.Single(fire.MatchedWords!).Text);
            Assert.False(e.NeedsOcrConfirmation);
        }

        [Fact]
        public void A_Scan_Without_The_Word_Resets_The_Streak()
        {
            var (e, _, fires) = Make(2, KeywordTriggerEngine.NewCustomTrigger("obey"));
            e.CheckOcrWords(new[] { W("obey") });
            e.CheckOcrWords(new[] { W("nothing") });
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Empty(fires);
        }

        [Fact]
        public void One_Instance_Fires_Once_While_It_Stays_And_Again_After_It_Left()
        {
            var (e, _, fires) = Make(1, KeywordTriggerEngine.NewCustomTrigger("drop"));
            e.CheckOcrWords(new[] { W("drop") });
            PastTheCooldowns(e);
            e.CheckOcrWords(new[] { W("drop", 110, 105) });   // nudged inside the 120 px bucket: the same instance
            Assert.Single(fires);
            e.CheckOcrWords(new[] { W("gone") });
            e.CheckOcrWords(new[] { W("drop") });
            Assert.Equal(2, fires.Count);
        }

        [Fact]
        public void A_New_Instance_Far_Away_Fires_While_The_First_Stays_Guarded()
        {
            var (e, _, fires) = Make(1, KeywordTriggerEngine.NewCustomTrigger("drop"));
            e.CheckOcrWords(new[] { W("drop") });
            PastTheCooldowns(e);
            e.CheckOcrWords(new[] { W("drop"), W("drop", 900, 700) });
            Assert.Equal(2, fires.Count);
            var word = Assert.Single(fires[1].MatchedWords!);
            Assert.Equal(900, word.X);
        }

        [Fact]
        public void Random_Subset_Mode_Fires_At_Least_One_Of_The_Candidates()
        {
            var (e, s, fires) = Make(1, KeywordTriggerEngine.NewCustomTrigger("drop"));
            s.OcrHighlightAll = false;
            e.NextRandom = (min, _) => min;
            e.CheckOcrWords(new[] { W("drop"), W("drop", 900, 700), W("drop", 500, 900) });
            Assert.Single(Assert.Single(fires).MatchedWords!);
        }

        [Fact]
        public void No_Access_Master_Off_Or_Global_Cooldown_Never_Fires()
        {
            var (e, s, fires) = Make(1, KeywordTriggerEngine.NewCustomTrigger("obey"));
            e.HasAccess = () => false;
            e.CheckOcrWords(new[] { W("obey") });
            e.HasAccess = () => true;
            s.KeywordTriggersEnabled = false;
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Empty(fires);
            s.KeywordTriggersEnabled = true;
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Single(fires);
            s.KeywordGlobalCooldownSeconds = 30;
            e.CheckOcrWords(new[] { W("obey", 900, 900) });
            Assert.Single(fires);
        }

        [Fact]
        public void An_Excluded_Foreground_App_Drops_The_Whole_Scan_And_Its_Streak()
        {
            var (e, s, fires) = Make(2, KeywordTriggerEngine.NewCustomTrigger("obey"));
            s.KeywordTriggerAppScope = AwarenessAppScope.ExceptListed;
            s.KeywordTriggerApps = new List<string> { "bank" };
            var app = "notepad";
            e.ForegroundResolver = () => new ForegroundApp(app, false);
            e.CheckOcrWords(new[] { W("obey") });
            app = "bank";
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Empty(fires);
            app = "notepad";
            e.CheckOcrWords(new[] { W("obey") });   // the streak restarted: one scan is not enough
            Assert.Empty(fires);
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Single(fires);
        }

        [Fact]
        public void Regex_Triggers_Are_Not_Matched_Against_The_Screen()
        {
            var t = KeywordTriggerEngine.NewCustomTrigger("ob.y");
            t.MatchType = KeywordMatchType.Regex;
            var (e, _, fires) = Make(1, t);
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Empty(fires);
        }

        [Fact]
        public void Own_Windows_Are_Excluded_By_Intersection()
        {
            var words = new List<OcrWordHit> { W("inside", 110, 110), W("edge", 280, 100), W("outside", 500, 500) };
            var kept = KeywordTriggerEngine.ExcludeOwnWindows(words, new[] { (100, 100, 200, 200) });
            Assert.Equal("outside", Assert.Single(kept).Text);
        }

        [Fact]
        public void Stopped_Engine_Ignores_A_Scan()
        {
            var (e, _, fires) = Make(1, KeywordTriggerEngine.NewCustomTrigger("obey"));
            e.Stop();
            e.CheckOcrWords(new[] { W("obey") });
            Assert.Empty(fires);
        }
    }
}
