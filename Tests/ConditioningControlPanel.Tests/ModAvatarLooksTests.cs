using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// The companion look is remembered per mod (tester, 6.11.1): Infection Control on
    /// "Dumb Airhead", a trip to Bambi Sleep (one pinned emote set, never saved) and back used to
    /// land on set 1, "Basic Bimbo".
    /// </summary>
    public class ModAvatarLooksTests
    {
        private static readonly int[] InfectionSets = { 1, 2, 3 };

        [Fact]
        public void TripThroughASingleEmoteModPutsThePickedLookBack()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ModAvatarLooks.Store(map, "infection", 2);   // picked Dumb Airhead in Infection Control

            // Bambi Sleep pinned current = 1 and saved nothing; the global pick is still 2.
            var back = ModAvatarLooks.ForModSwitch(map, "infection", globalPick: 2, current: 1, InfectionSets);
            Assert.Equal(2, back);
        }

        [Fact]
        public void TheModsOwnPickBeatsTheLastPickAnywhere()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ModAvatarLooks.Store(map, "infection", 2);
            ModAvatarLooks.Store(map, "other", 3);        // later pick in another mod, global = 3

            Assert.Equal(2, ModAvatarLooks.ForModSwitch(map, "infection", globalPick: 3, current: 3, InfectionSets));
        }

        [Fact]
        public void NoStoredPickFallsBackToGlobalThenCurrentThenFirst()
        {
            var map = new Dictionary<string, int>();
            Assert.Equal(3, ModAvatarLooks.ForModSwitch(map, "m", globalPick: 3, current: 1, InfectionSets));
            Assert.Equal(2, ModAvatarLooks.ForModSwitch(map, "m", globalPick: 9, current: 2, InfectionSets));
            Assert.Equal(1, ModAvatarLooks.ForModSwitch(map, "m", globalPick: 9, current: 8, InfectionSets));
            Assert.Equal(1, ModAvatarLooks.ForModSwitch(null, null, globalPick: 9, current: 8, InfectionSets));
        }

        [Fact]
        public void AStoredLookTheModCannotShowIsIgnored()
        {
            var map = new Dictionary<string, int> { ["infection"] = 5 };   // e.g. a retired level
            Assert.Null(ModAvatarLooks.StoredFor(map, "infection", InfectionSets));
            Assert.Equal(2, ModAvatarLooks.ForModSwitch(map, "infection", globalPick: 2, current: 1, InfectionSets));
        }

        [Fact]
        public void NothingPickableKeepsTheCurrentLook()
        {
            var map = new Dictionary<string, int> { ["infection"] = 2 };
            Assert.Equal(4, ModAvatarLooks.ForModSwitch(map, "infection", 2, 4, Array.Empty<int>()));
            Assert.Equal(4, ModAvatarLooks.ForModSwitch(map, "infection", 2, 4, null));
        }

        [Fact]
        public void StoreIgnoresBlankModsAndBadSets()
        {
            var map = new Dictionary<string, int>();
            ModAvatarLooks.Store(map, "", 2);
            ModAvatarLooks.Store(map, null, 2);
            ModAvatarLooks.Store(map, "m", 0);
            Assert.Empty(map);
        }

        [Fact]
        public void TheSettingIsCaseInsensitiveAndRoundTripsThroughJson()
        {
            var s = new AppSettings();
            ModAvatarLooks.Store(s.ModAvatarSet, "Infection", 2);
            Assert.Equal(2, ModAvatarLooks.StoredFor(s.ModAvatarSet, "infection", InfectionSets));

            var back = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(s))!;
            Assert.Equal(2, ModAvatarLooks.StoredFor(back.ModAvatarSet, "INFECTION", InfectionSets));
        }
    }
}
