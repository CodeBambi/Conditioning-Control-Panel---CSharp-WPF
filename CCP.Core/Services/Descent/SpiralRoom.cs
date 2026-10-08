using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>Which of the Spiral Room's three standing states a set of gates resolves to.</summary>
    /// <remarks>
    /// FIRST LIGHT is deliberately NOT in this enum. It is an EVENT, not a gate: it plays once,
    /// seconds after a ceremony commits, and while it runs it overrides whatever this arithmetic
    /// says. Modelling it here would mean inventing a "has the reveal played" input that only the
    /// view knows and that nothing persists (see DescentShowDirector's note on why the one-shot is
    /// not written to settings).
    /// </remarks>
    public enum SpiralRoomState
    {
        /// <summary>The fog era: a ceremony is owed or scheduled, so the spiral is withheld and the
        /// tab shows weather and a countdown instead.</summary>
        Fog = 0,

        /// <summary>The gate is open but there is nothing to draw yet — no descent block, or the
        /// embed could not start. A held promise, in the fuse's own voice.</summary>
        Waiting = 1,

        /// <summary>The spiral itself: the `/embed/spiral` canvas at ?mode=map.</summary>
        Spiral = 2,
    }

    /// <summary>How hard the fuse rail chip is shaking. One tier per phase band.</summary>
    public enum FuseWobbleTier
    {
        /// <summary>Nothing. The chip is not on screen.</summary>
        None = 0,

        /// <summary>Whisper: one damped burst every twenty seconds. Easy to miss on purpose.</summary>
        Rare = 1,

        /// <summary>Clock through Candle: the same burst, every nine seconds.</summary>
        Frequent = 2,

        /// <summary>Vigil: a continuous fine tremble and a breathing ring.</summary>
        Tremble = 3,

        /// <summary>Terminal: the tremble four times as fast, and the ring flaring with it.</summary>
        Violent = 4,
    }

    /// <summary>
    /// THE SPIRAL ROOM's arithmetic — every "what should be on screen" decision the tab, the rail
    /// entry and the fuse chip take, with no WPF in any of it.
    ///
    /// <para><b>Why this is a separate file with every input passed in.</b> Same discipline as
    /// <see cref="DescentMigrationService.SpiralWithheldFor"/> and
    /// <see cref="DescentFuseCopy"/>: the three surfaces below have to AGREE — a rail entry that is
    /// visible while the tab it points at has nothing to show, or a chip that outlives the ceremony
    /// that gave it a reason to exist, are both bugs you only ever find on the one night the
    /// feature runs. Collecting the predicates here means they are one truth table, pinned by
    /// tests, on a suite that has no dispatcher and no settings singleton.</para>
    ///
    /// <para><b>Nothing here reads <see cref="App"/>.</b> The callers do the reading; this file
    /// does the deciding. That is what lets a test stand every persona up — fuse-dark fresh
    /// account, veteran mid-ceremony, migrated veteran with the block still in flight — from four
    /// booleans and an enum.</para>
    /// </summary>
    public static class SpiralRoom
    {
        /// <summary>The tab key. One string, spelled once, because it is asserted in five files.</summary>
        public const string TabKey = "spiral";

        // ==================== the fog era ====================

        /// <summary>
        /// TRUE while a migration ceremony is owed or still scheduled — the whole of the fog era.
        ///
        /// <para>Three ways in, and they are three different moments of the same night:</para>
        /// <list type="bullet">
        /// <item>THE FUSE IS BURNING (<c>Whisper &lt;= phase &lt; Zero</c>). Something is coming and
        /// has not arrived. Nobody's spiral is open yet, veteran or not.</item>
        /// <item>THE SPIRAL IS WITHHELD (<paramref name="spiralWithheld"/>, from the frozen
        /// <see cref="DescentMigrationService.SpiralWithheldFor"/>). The question is in the room and
        /// has not been answered.</item>
        /// <item>THE INSTANT HAS PASSED AND NOTHING WAS ANSWERED. The fuse was armed, zero went by,
        /// and this account has neither an acked migration nor a valid pending choice — which is
        /// the state of a veteran whose ceremony has not reached them yet (a paused rollout dial, a
        /// sync that has not landed). The withhold cannot see that case on its own: it needs the
        /// offer to have arrived, and the whole point of this clause is that it has not.</item>
        /// </list>
        ///
        /// <para><b>A fresh post-zero account is NOT in the fog era</b> — the fuse is dark for them
        /// (the owner clears the timestamp after launch night), they are never offered a migration,
        /// and they have nothing owed. They fall straight through to Waiting or Spiral, which is the
        /// whole point of the block dial promoting at zero.</para>
        /// </summary>
        public static bool IsFogEra(
            DescentFusePhase phase,
            bool fuseArmed,
            bool spiralWithheld,
            bool migrationCompleted,
            bool hasValidPendingChoice)
        {
            if (phase >= DescentFusePhase.Whisper && phase < DescentFusePhase.Zero) return true;
            if (spiralWithheld) return true;
            if (fuseArmed && phase >= DescentFusePhase.Zero && !migrationCompleted && !hasValidPendingChoice)
                return true;
            return false;
        }

        // ==================== the tab ====================

        /// <summary>
        /// THE STATE SELECTION, whole. Fog outranks everything (a spiral shown beside an unanswered
        /// question is the one thing the withhold exists to prevent), then block presence decides
        /// between the canvas and the held promise.
        ///
        /// <para><b>Waiting is not an error state.</b> The embed's <c>?mode=map</c> route is not
        /// deployed yet, and the seconds after a commit are legitimately blockless, so Waiting is
        /// what most of today's traffic actually lands on. It has to look like a room the app meant
        /// to build.</para>
        /// </summary>
        public static SpiralRoomState StateFor(
            DescentFusePhase phase,
            bool fuseArmed,
            bool spiralWithheld,
            bool migrationCompleted,
            bool hasValidPendingChoice,
            bool hasBlock)
        {
            if (IsFogEra(phase, fuseArmed, spiralWithheld, migrationCompleted, hasValidPendingChoice))
                return SpiralRoomState.Fog;

            return hasBlock ? SpiralRoomState.Spiral : SpiralRoomState.Waiting;
        }

        /// <summary>
        /// Convenience over <see cref="StateFor"/> for the callers that hold a settings object: the
        /// two migration flags are read off it with the same validity test everything else uses, so
        /// a hand-edited settings file cannot open the gate by writing nonsense into the pending
        /// choice field. Null settings read as a fresh account.
        /// </summary>
        public static SpiralRoomState StateFor(
            AppSettings? settings,
            DescentFusePhase phase,
            bool fuseArmed,
            bool spiralWithheld,
            bool hasBlock)
            => StateFor(phase, fuseArmed, spiralWithheld,
                        settings?.DescentMigrationCompleted == true,
                        DescentMigrationChoices.IsValid(settings?.PendingDescentMigrationChoice),
                        hasBlock);

        // ==================== the rail entry ====================

        /// <summary>
        /// Is there a row for the Spiral in the You door at all?
        ///
        /// <para><b>The default is NO, and that is load-bearing.</b> A fuse-dark account with no
        /// descent block — every install on today's server — never sees this row, so the rail
        /// measures byte-identically to the build before the Spiral Room existed. The row appears
        /// only when there is genuinely somewhere to go: the fog era (where it reads as an ellipsis,
        /// because naming it would spoil it) or the spiral era (where it is "The Spiral").</para>
        ///
        /// <para>Waiting does NOT light the row. Waiting means the account has answered but has no
        /// block yet — a row pointing at a held promise is a row that answers "nothing happened"
        /// when clicked, and the tab re-evaluates on the next BlockChanged anyway.</para>
        /// </summary>
        public static bool RailEntryVisible(SpiralRoomState state)
            => state is SpiralRoomState.Fog or SpiralRoomState.Spiral;

        /// <summary>TRUE while the row must wear the fog era's ellipsis rather than its name.</summary>
        public static bool RailEntryIsAnonymous(SpiralRoomState state) => state == SpiralRoomState.Fog;

        // ==================== the rail chip ====================

        /// <summary>
        /// THE FUSE RAIL CHIP's gate. The countdown is on screen and this account has not already
        /// taken their ceremony.
        ///
        /// <para>Both halves matter and neither is redundant. The phase band is what the Dark kill
        /// switch travels through — a cleared server timestamp announces
        /// <see cref="DescentFusePhase.Dark"/> and the chip leaves with it, mid-session, no restart.
        /// The migration flag is what makes the chip PERMANENT-off for somebody who took the
        /// ceremony on the first night: their fuse is spent, and a clock still counting down to a
        /// door they already walked through would be the app forgetting what they did.</para>
        /// </summary>
        public static bool FuseChipVisible(DescentFusePhase phase, bool migrationCompleted)
            => !migrationCompleted
               && phase >= DescentFusePhase.Whisper
               && phase < DescentFusePhase.Zero;

        /// <summary>
        /// How hard the chip shakes at a phase. Returns <see cref="FuseWobbleTier.None"/> for every
        /// phase the chip is not shown at, so a caller cannot start a clock on a hidden element by
        /// forgetting to check the gate first.
        /// </summary>
        public static FuseWobbleTier WobbleFor(DescentFusePhase phase) => phase switch
        {
            DescentFusePhase.Whisper => FuseWobbleTier.Rare,
            DescentFusePhase.Clock or DescentFusePhase.Dimming or DescentFusePhase.Candle
                => FuseWobbleTier.Frequent,
            DescentFusePhase.Vigil => FuseWobbleTier.Tremble,
            DescentFusePhase.Terminal => FuseWobbleTier.Violent,
            _ => FuseWobbleTier.None,
        };

        /// <summary>
        /// THE FLASH-OUT GATE (owner ruling 2026-08-16: "Timer goes to 0, starts flashing, and we
        /// run the cerimony"). TRUE only for a LIVE transition into <see cref="DescentFusePhase.Zero"/>
        /// from a phase the chip was actually on screen for.
        ///
        /// <para><b>The "from a live phase" half is the whole point.</b> An app launched the morning
        /// after the ceremony seeds its chip from
        /// <see cref="DescentCountdownService.LastAnnouncedPhase"/>, which is
        /// <see cref="DescentFusePhase.Dark"/> or already Zero — nothing was ever on screen to flash
        /// out, and a rail that flickered violently on every cold start for a moment the subject
        /// slept through would be the app performing an event rather than marking one. The flash is
        /// a goodbye, and a goodbye needs somebody to have been in the room.</para>
        ///
        /// <para><b>Zero to Zero is not a transition</b> either — a repaint (the migration flag
        /// landing, a settings reload) must not re-fire two and a half seconds of flicker on a chip
        /// that already left.</para>
        /// </summary>
        public static bool ShouldFlashOutAtZero(DescentFusePhase previous, DescentFusePhase current)
            => current >= DescentFusePhase.Zero
               && previous >= DescentFusePhase.Whisper
               && previous < DescentFusePhase.Zero;

        // ==================== the fog era's heartbeat ====================

        /// <summary>
        /// How long ONE BREATH of the fog countdown's pulse takes, in seconds — the half-cycle of
        /// the scale animation under the hero digits (owner verdict 2026-08-16: "could use some FX,
        /// a bolder text, maybe some little animation and flair").
        ///
        /// <para><b>It is a tempo ladder, not a size ladder.</b> The amplitude never changes; only
        /// the rate does, which is the same instrument the rail chip's wobble plays — the last ten
        /// minutes raise their voice with speed, not with size. A countdown whose digits grew as
        /// the instant approached would be shouting; one that just breathes faster is somebody
        /// getting nervous, which is the register the whole fog era is written in.</para>
        ///
        /// <para><b>Zero is as tight as Terminal</b> on purpose. The fog era outlives the instant
        /// for anybody whose own ceremony has not reached them yet, and the readout they are looking
        /// at says "any moment now" — the least calm sentence in the feature. Slowing the pulse down
        /// at exactly that moment would read as the room losing interest.</para>
        ///
        /// <para>Never zero or negative, at any phase: the caller divides nothing by this, but it
        /// does hand it to a <c>TimeSpan.FromSeconds</c> that a zero would turn into a stuck
        /// animation rather than a fast one.</para>
        /// </summary>
        public static double FogPulseSecondsFor(DescentFusePhase phase) => phase switch
        {
            DescentFusePhase.Whisper => 2.8,
            DescentFusePhase.Clock or DescentFusePhase.Dimming or DescentFusePhase.Candle => 2.0,
            DescentFusePhase.Vigil => 1.15,
            DescentFusePhase.Terminal or DescentFusePhase.Zero => 0.52,
            _ => 3.0,
        };
    }
}
