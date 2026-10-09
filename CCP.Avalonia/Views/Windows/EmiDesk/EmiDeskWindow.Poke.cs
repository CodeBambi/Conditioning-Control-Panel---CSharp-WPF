using System;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The click pat and THE POKE LADDER (row E9): WPF EmiDeskWindow.React.cs:98-185 and
    /// Alive.cs:72-131 / :738-783. Pats inside 4 s climb: 3rd = glee (chime + petStreak), 4th =
    /// annoyed flick, 5th = rage (wordless, then a 60 s truce). The 6 s pet cooldown still decides
    /// whether a pat draws a line; the ladder only re-dresses the flick.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private readonly EmiAlive.PokeLadder _pokes = new();
        private DateTime _lastPokeAt = DateTime.MinValue;

        /// <summary>The pat inside the cooldown: a wink and a bounce.</summary>
        private static readonly EmiChain PetFlickChain = new(
            "petFlick", "PAT (cooling down)",
            new[] { new EmiFrame("^_~", 320), new EmiFrame(EmiChains.RestFace, 180) },
            Move: "bounce", BodyFrame: "pet");

        /// <summary>The fourth: the same flick, wearing the annoyed face.</summary>
        private static readonly EmiChain PokeAnnoyChain = new(
            "pokeAnnoy", "POKE 4 (annoyed)",
            new[] { new EmiFrame(EmiAlive.PokeAnnoyFace, 700), new EmiFrame(EmiChains.RestFace, 180) },
            BodyFrame: "idle");

        /// <summary>The fifth: the canon rage frames and then the glare, held, wordless.</summary>
        private static readonly EmiChain PokeRageChain = new(
            "pokeRage", "POKE 5 (rage)",
            new[]
            {
                new EmiFrame(">.<", 200), new EmiFrame(">_<", 200), new EmiFrame(">.<", 200),
                new EmiFrame(EmiAlive.PokeRageFace, EmiAlive.PokeRageHoldMs)
            },
            Fx: "storm", Move: "shiver", BodyFrame: "shock");

        /// <summary>The id of the chain on screen (test seam).</summary>
        internal string? ChainId => _player.Current?.Id;

        /// <summary>
        /// The click pat (WPF React.cs:119). A pat during her own entrance CUTS the entrance; a line
        /// in flight is never cut (LAW 3).
        /// </summary>
        internal void PetFromClick()
        {
            try
            {
                if (_transiting || InputLocked) return;
                if (_player.IsLive)
                {
                    if (!_summonChainLive) return;
                    FinishSummon();
                    CancelChain();
                }

                DisarmPet();
                _petArmed = true;
                RaiseActivity();
                PlayPatSfx();

                var poke = NotePoke();
                if (poke == EmiPokeStep.Glee)
                {
                    _petCooldownUntil = DateTime.UtcNow.AddMilliseconds(PetCooldownMs);
                    PlayPokeFlick(poke);
                    return;
                }

                if (DateTime.UtcNow < _petCooldownUntil)
                {
                    PlayPokeFlick(poke);
                    return;
                }

                _petCooldownUntil = DateTime.UtcNow.AddMilliseconds(PetCooldownMs);
                PlayChain("pet");
                CountPat();
                FireDeskEvent("petted");
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pat failed"); }
        }

        private EmiPokeStep NotePoke()
        {
            var now = DateTime.UtcNow;
            _lastPokeAt = now;
            try { return _pokes.Note(now); }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] poke ladder failed");
                return EmiPokeStep.Pat;
            }
        }

        /// <summary>The flick a poke earns: the plain wink, the annoyed look, the glare, or glee.</summary>
        private void PlayPokeFlick(EmiPokeStep step)
        {
            switch (step)
            {
                case EmiPokeStep.Rage: PlayChain(PokeRageChain); break;
                case EmiPokeStep.Annoyed: PlayChain(PokeAnnoyChain); break;
                case EmiPokeStep.Glee:
                    EmiSfx.Chime();
                    PlayChain("petStreak");
                    break;
                default: PlayChain(PetFlickChain); break;
            }
        }
    }
}
