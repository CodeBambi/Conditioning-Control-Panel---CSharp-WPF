using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// A running session's hold on the three flat phrase pools (#906), ported from WPF SessionEngine
    /// (SessionEngine.cs:1117-1317, 1379-1395, 1443-1457, 1582-1595, 1678-1761). One instance per
    /// session, in memory only. Session writes to the pools are IN PLACE (no INPC), so every pool
    /// assignment ModService's mirror sees is a genuine user edit; ModService's mod-driven
    /// assignments run with the mirror suppressed.
    ///
    /// Deliberate difference from WPF (D1, docs/avalonia-decisions.md): a mid-session mod switch
    /// re-bases the user snapshot onto the incoming mod's pools in <see cref="Reapply"/>, and
    /// <see cref="Restore"/> reconciles after all three pools, not between them.
    /// </summary>
    public sealed class PhrasePoolCustody
    {
        private readonly AppSettings _settings;
        private Dictionary<string, bool>? _savedSubliminalPool, _savedLockCardPool, _savedBouncingTextPool;
        private Dictionary<string, bool>? _prescribedSubliminalPool, _prescribedLockCardPool, _prescribedBouncingTextPool;

        /// <summary>The active mod when the session started; Restore reconciles if it changed.</summary>
        public string? ModIdAtStart { get; }

        /// <summary>The running session's custody, or null. Every CoreSession delegate reads it afresh.</summary>
        public static PhrasePoolCustody? Active { get; private set; }

        private PhrasePoolCustody(AppSettings settings, string? modId)
        {
            _settings = settings;
            ModIdAtStart = modId;
        }

        /// <summary>Seeds the three CoreSession pool delegates (WPF App.xaml.cs:324-329). The head calls
        /// this once at startup; Begin never does, so it cannot overwrite WPF's own seeding.</summary>
        public static void Seed()
        {
            CoreSession.NoteUserPhrasePoolEdit = name => Active?.NoteUserEdit(name);
            CoreSession.ReapplyPhrasePoolOverrides = () => Active?.Reapply();
            CoreSession.UserPhrasePoolsWhileOverriding = () => Active?.UserPoolsWhileOverriding();
        }

        /// <summary>Publish, snapshot, override in place, prescribe - WPF StartSessionAsync order.
        /// With the running <paramref name="session"/>, built-ins override with PresetNaming's mod-aware
        /// words (neutral under CCP Default; WPF SessionEngine.cs:1381-1383, 1447-1449).</summary>
        public static PhrasePoolCustody Begin(AppSettings current, SessionSettings settings, string? modId, Session? session = null)
        {
            var custody = new PhrasePoolCustody(current, modId);
            // Published first: not overriding until the prescribed copies exist below.
            Active = custody;

            custody._savedSubliminalPool = new Dictionary<string, bool>(current.SubliminalPool);
            custody._savedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
            custody._savedLockCardPool = new Dictionary<string, bool>(current.LockCardPhrases);

            var own = session != null && ReferenceEquals(settings, session.Settings);
            var subliminalWords = own ? PresetNaming.SubliminalWords(session!, modId) : settings.SubliminalPhrases;
            var bouncingWords = own ? PresetNaming.BouncingWords(session!, modId) : settings.BouncingTextPhrases;
            if (settings.SubliminalEnabled && subliminalWords.Count > 0)
                Override(current.SubliminalPool, subliminalWords, CoreMods.MakeModAware);
            if (settings.BouncingTextEnabled && bouncingWords.Count > 0)
                Override(current.BouncingTextPool, bouncingWords, p => p);
            if (settings.LockCardEnabled && settings.LockCardPhrases.Count > 0)
                Override(current.LockCardPhrases, settings.LockCardPhrases, CoreMods.MakeModAware);

            // D2: phrase count only, Enabled ignored - as WPF RememberPrescribedPools.
            if (settings.SubliminalPhrases.Count > 0)
                custody._prescribedSubliminalPool = new Dictionary<string, bool>(current.SubliminalPool);
            if (settings.BouncingTextPhrases.Count > 0)
                custody._prescribedBouncingTextPool = new Dictionary<string, bool>(current.BouncingTextPool);
            if (settings.LockCardPhrases.Count > 0)
                custody._prescribedLockCardPool = new Dictionary<string, bool>(current.LockCardPhrases);
            return custody;
        }

        /// <summary>Disable every existing phrase, then enable the session's own.</summary>
        private static void Override(Dictionary<string, bool> pool, List<string> phrases, Func<string, string> key)
        {
            foreach (var k in new List<string>(pool.Keys)) pool[k] = false;
            foreach (var phrase in phrases) pool[key(phrase)] = true;
        }

        private bool IsOverriding =>
            ReferenceEquals(Active, this) && (_prescribedSubliminalPool != null
                                              || _prescribedBouncingTextPool != null
                                              || _prescribedLockCardPool != null);

        private static Dictionary<string, bool>? Copy(Dictionary<string, bool>? d) =>
            d == null ? null : new Dictionary<string, bool>(d);

        /// <summary>The user's own pools while this session overrides any; null otherwise.</summary>
        public (Dictionary<string, bool>? Subliminal, Dictionary<string, bool>? LockCard, Dictionary<string, bool>? BouncingText)? UserPoolsWhileOverriding() =>
            IsOverriding ? (Copy(_savedSubliminalPool), Copy(_savedLockCardPool), Copy(_savedBouncingTextPool)) : null;

        /// <summary>Folds a genuine user pool edit into the restore snapshot (SessionEngine.cs:1185-1215).</summary>
        public void NoteUserEdit(string? propertyName)
        {
            if (!ReferenceEquals(Active, this)) return;
            try
            {
                switch (propertyName)
                {
                    case nameof(AppSettings.SubliminalPool):
                        FoldUserPoolEdit(_settings.SubliminalPool, ref _prescribedSubliminalPool, ref _savedSubliminalPool);
                        break;
                    case nameof(AppSettings.BouncingTextPool):
                        FoldUserPoolEdit(_settings.BouncingTextPool, ref _prescribedBouncingTextPool, ref _savedBouncingTextPool);
                        break;
                    case nameof(AppSettings.LockCardPhrases):
                        FoldUserPoolEdit(_settings.LockCardPhrases, ref _prescribedLockCardPool, ref _savedLockCardPool);
                        break;
                }
                Log.Debug("[Session] Folded mid-session {Pool} edit into restore snapshot", propertyName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Session] Failed to fold a mid-session phrase edit into the restore snapshot");
            }
        }

        /// <summary>
        /// Applies the user's delta (live pool vs what the session prescribed) to the pre-session
        /// snapshot, then re-bases the prescribed copy onto the live pool so a later edit diffs
        /// against what is actually on screen — and so a mod switch re-asserts the edited pool.
        /// Phrases the session owns are never folded into the user's pool: only keys the user
        /// added, deleted, or re-toggled on their OWN phrases move across.
        /// </summary>
        internal static void FoldUserPoolEdit(
            Dictionary<string, bool>? live,
            ref Dictionary<string, bool>? prescribed,
            ref Dictionary<string, bool>? saved)
        {
            if (live == null || saved == null) return;

            if (prescribed == null)
            {
                // The session left this pool alone, so the live pool IS the user's own.
                saved = new Dictionary<string, bool>(live);
                return;
            }

            foreach (var kvp in live)
            {
                if (!prescribed.TryGetValue(kvp.Key, out var wasEnabled))
                {
                    saved[kvp.Key] = kvp.Value;                     // added while the session ran
                }
                else if (saved.ContainsKey(kvp.Key))
                {
                    // Differs from what the session prescribed: an unambiguous re-toggle of one of
                    // their own phrases.
                    //
                    // EQUAL to what the session prescribed is ambiguous, and stays ambiguous: the
                    // phrase editor is seeded from the LIVE pool, so the user may have ticked the
                    // box onto the session's value on purpose, or may simply have saved an
                    // unrelated edit with the session's own value standing. Adopt only the ENABLED
                    // side of that ambiguity - ApplySessionSettings disables every pre-existing
                    // phrase and enables only its own, so adopting a `false` here would silence
                    // the user's whole pool the moment they save any mid-session edit, while
                    // adopting a `true` at worst leaves one phrase they already own switched on,
                    // and it is the half the fold actually loses today (ticking a phrase the
                    // session happens to run too, which then reverted at session end).
                    if (wasEnabled != kvp.Value || kvp.Value) saved[kvp.Key] = kvp.Value;
                }
            }

            foreach (var key in prescribed.Keys)
            {
                if (!live.ContainsKey(key)) saved.Remove(key);       // deleted while the session ran
            }

            prescribed = new Dictionary<string, bool>(live);
        }


        /// <summary>
        /// After a mod switch restored the incoming mod's pools: re-base each user snapshot onto them
        /// (D1 fix; WPF kept the outgoing mod's snapshot), then re-assert only the prescribed pools. Always
        /// safe: with no per-mod backup, ModService falls back to the user's pools, not the session's (D1a).
        /// Runs inside ModService.ActivateMod's mirror suppression, so these assignments are not user edits.
        /// </summary>
        public void Reapply()
        {
            if (!ReferenceEquals(Active, this)) return;
            try
            {
                if (_savedSubliminalPool != null) _savedSubliminalPool = new Dictionary<string, bool>(_settings.SubliminalPool);
                if (_savedBouncingTextPool != null) _savedBouncingTextPool = new Dictionary<string, bool>(_settings.BouncingTextPool);
                if (_savedLockCardPool != null) _savedLockCardPool = new Dictionary<string, bool>(_settings.LockCardPhrases);

                if (_prescribedSubliminalPool != null)
                    _settings.SubliminalPool = new Dictionary<string, bool>(_prescribedSubliminalPool);
                if (_prescribedBouncingTextPool != null)
                    _settings.BouncingTextPool = new Dictionary<string, bool>(_prescribedBouncingTextPool);
                if (_prescribedLockCardPool != null)
                    _settings.LockCardPhrases = new Dictionary<string, bool>(_prescribedLockCardPool);
                Log.Information("[Session] Re-applied prescribed phrase pools after a mod switch");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Session] Failed to re-apply prescribed phrase pools");
            }
        }

        /// <summary>Hands custody back; the runner calls it where it sets IsRunning=false (SessionEngine.cs:343-349).</summary>
        public void Release()
        {
            _prescribedSubliminalPool = null;
            _prescribedBouncingTextPool = null;
            _prescribedLockCardPool = null;
            if (ReferenceEquals(Active, this)) Active = null;
        }

        /// <summary>Puts the user's pools back in place, then (D1 fix: after all three, not between)
        /// re-reads the active mod's pools if the mod changed mid-session.</summary>
        public void Restore(AppSettings current)
        {
            RestoreInPlace(current.SubliminalPool, ref _savedSubliminalPool);
            RestoreInPlace(current.BouncingTextPool, ref _savedBouncingTextPool);
            RestoreInPlace(current.LockCardPhrases, ref _savedLockCardPool);
            try
            {
                var modNow = CoreMods.ActiveModId;
                if (ModIdAtStart != null && modNow != ModIdAtStart)
                {
                    Log.Information("[Session] Mod changed mid-session ({From} -> {To}); re-applying the active mod's pools "
                        + "instead of the start-time snapshot", ModIdAtStart, modNow);
                    CoreMods.ReapplyActiveModPoolsProvider?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Session] Post-session mod pool reconciliation failed");
            }
        }

        private static void RestoreInPlace(Dictionary<string, bool> live, ref Dictionary<string, bool>? saved)
        {
            if (saved == null) return;
            live.Clear();
            foreach (var kvp in saved) live[kvp.Key] = kvp.Value;
            saved = null;
        }
    }
}
