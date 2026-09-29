using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// The personality is remembered per mod, like the look (tester, 6.11.3: the look came back
    /// after a mod switch and back, the personality did not).
    /// </summary>
    public class ModPersonalityPicksTests
    {
        private static Func<string, bool> Offers(params string[] ids) => id => Array.IndexOf(ids, id) >= 0;

        [Fact]
        public void TripThroughAnotherModPutsThePickedPersonalityBack()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ModPersonalityPicks.Store(map, "builtin-bambisleep", "bambi_brat");
            ModPersonalityPicks.Store(map, "builtin-sissyhypno", "sissy_coach");

            // Back in Bambi Sleep, the global id still says the Sissy pick.
            var back = ModPersonalityPicks.ForModSwitch(map, "builtin-bambisleep", "sissy_coach",
                Offers("bambi_default", "bambi_brat"));
            Assert.Equal("bambi_brat", back);
        }

        [Fact]
        public void NothingStoredLeavesTheCurrentPersonalityAlone()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Assert.Null(ModPersonalityPicks.ForModSwitch(map, "builtin-locked", "whatever", Offers("whatever")));
            Assert.Null(ModPersonalityPicks.ForModSwitch(null, "builtin-locked", "whatever", Offers("whatever")));
        }

        [Fact]
        public void AStoredPickTheModNoLongerOffersIsIgnored()
        {
            var map = new Dictionary<string, string> { ["builtin-locked"] = "gone" };
            Assert.Null(ModPersonalityPicks.ForModSwitch(map, "builtin-locked", "circe", Offers("circe")));
        }

        [Fact]
        public void TheActivePickNeedsNoSwitch()
        {
            var map = new Dictionary<string, string> { ["builtin-locked"] = "circe" };
            Assert.Null(ModPersonalityPicks.ForModSwitch(map, "builtin-locked", "circe", Offers("circe")));
        }

        [Fact]
        public void BlankIdsAreNeverStored()
        {
            var map = new Dictionary<string, string>();
            ModPersonalityPicks.Store(map, "", "x");
            ModPersonalityPicks.Store(map, "mod", " ");
            ModPersonalityPicks.Store(map, null, "x");
            Assert.Empty(map);
        }

        [Fact]
        public void TheMapSurvivesASettingsRoundTrip()
        {
            var s = new AppSettings();
            s.ModPersonalityPreset["Builtin-Locked"] = "circe_soft";
            var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
            Assert.Equal("circe_soft", ModPersonalityPicks.StoredFor(back.ModPersonalityPreset, "builtin-locked", null));
        }
    }

    /// <summary>ccp-bugs #1304: Mind Wipe kept firing after it was switched off or the session ended.</summary>
    public class MindWipeRunRuleTests
    {
        [Fact]
        public void SwitchingItOffStopsARunningMindWipe()
        {
            var p = MindWipeRunRule.ForFlags(engineRunning: true, sessionRunning: false, enabled: false,
                loop: false, serviceRunning: true, looping: false);
            Assert.True(p.Stop);
            Assert.False(p.Start);
        }

        [Fact]
        public void SwitchingItOffStopsALoopLeftPlayingWithTheEngineOff()
        {
            var p = MindWipeRunRule.ForFlags(engineRunning: false, sessionRunning: false, enabled: false,
                loop: true, serviceRunning: false, looping: true);
            Assert.True(p.Stop);
        }

        [Fact]
        public void SwitchingItOnOnlyStartsWhileTheEngineRuns()
        {
            Assert.True(MindWipeRunRule.ForFlags(true, false, true, false, false, false).Start);
            Assert.True(MindWipeRunRule.ForFlags(false, false, true, false, false, false).IsNothing);
        }

        [Fact]
        public void TheLoopBoxNeverStartsALoopWithTheEngineOff()
        {
            var p = MindWipeRunRule.ForFlags(engineRunning: false, sessionRunning: false, enabled: true,
                loop: true, serviceRunning: false, looping: false);
            Assert.False(p.StartLoop);
        }

        [Fact]
        public void TheLoopFollowsItsBoxWhileRunning()
        {
            Assert.True(MindWipeRunRule.ForFlags(true, false, true, true, true, false).StartLoop);
            Assert.True(MindWipeRunRule.ForFlags(true, false, true, false, true, true).StopLoop);
        }

        [Fact]
        public void ARunningSessionOwnsMindWipe()
        {
            Assert.True(MindWipeRunRule.ForFlags(true, true, false, false, true, true).IsNothing);
        }
    }

    /// <summary>Lockdown forbids pausing, never resuming (Discord ticket: stranded after the blink stop).</summary>
    public class LockdownPauseRuleTests
    {
        [Theory]
        [InlineData(true, false, true)]    // Lockdown, running: pausing refused
        [InlineData(true, true, false)]    // Lockdown, paused: resuming allowed
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        public void OnlyThePauseDirectionIsRefused(bool lockdown, bool paused, bool refused)
            => Assert.Equal(refused, LockdownPauseRule.RefusesPauseButton(lockdown, paused));
    }

    /// <summary>ccp-bugs #1310: "localhost.12345" never linked to Intiface.</summary>
    public class ButtplugUrlTests
    {
        [Theory]
        [InlineData("localhost.12345", "ws://localhost:12345")]
        [InlineData("ws://localhost.12345", "ws://localhost:12345")]
        [InlineData("127.0.0.1.12345", "ws://127.0.0.1:12345")]
        [InlineData("ws://192.168.1.20.12345/", "ws://192.168.1.20:12345/")]
        [InlineData("localhost:12345", "ws://localhost:12345")]
        [InlineData(" ws://127.0.0.1:12345 ", "ws://127.0.0.1:12345")]
        [InlineData("http://localhost:12345", "ws://localhost:12345")]
        [InlineData("ws://192.168.1.20", "ws://192.168.1.20:12345")]
        [InlineData("ws://intiface.local", "ws://intiface.local:12345")]
        [InlineData("wss://intiface.example", "wss://intiface.example")]
        [InlineData("", ButtplugUrl.Default)]
        [InlineData(null, ButtplugUrl.Default)]
        public void TypedAddressesAreCleanedUp(string? typed, string expected)
        {
            Assert.Equal(expected, ButtplugUrl.Normalize(typed, out var error));
            Assert.Null(error);
        }

        [Theory]
        [InlineData("ftp://localhost:12345")]
        [InlineData("local host:12345")]
        [InlineData("ws://")]
        // A slash typo leaves the scheme behind as the host: "ws", port 80.
        [InlineData("ws:/localhost:12345")]
        [InlineData("ws//localhost:12345")]
        [InlineData("wss:/localhost:12345")]
        [InlineData("http:/localhost:12345")]
        public void UnusableAddressesComeBackWithAReason(string typed)
        {
            Assert.Null(ButtplugUrl.Normalize(typed, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        /// <summary>No port used to mean ws's port 80, where Intiface never listens, and a bare
        /// number was read as the IPv4 address 0.0.48.57.</summary>
        [Theory]
        [InlineData("12345", "ws://127.0.0.1:12345")]
        [InlineData("localhost", "ws://localhost:12345")]
        [InlineData("127.0.0.1", "ws://127.0.0.1:12345")]
        [InlineData("ws://localhost", "ws://localhost:12345")]
        [InlineData("ws://localhost/", "ws://localhost:12345/")]
        public void AMissingPortIsIntifacesOwn(string typed, string expected)
        {
            Assert.Equal(expected, ButtplugUrl.Normalize(typed, out var error));
            Assert.Null(error);
            Assert.Equal(12345, new Uri(expected).Port);
        }

        [Fact]
        public void AnExistingPortIsNeverRewritten()
        {
            Assert.Equal("ws://localhost:12345", ButtplugUrl.Normalize("ws://localhost:12345", out _));
            Assert.Equal("ws://a.b.99:12345", ButtplugUrl.Normalize("ws://a.b.99:12345", out _));
        }
    }
}
