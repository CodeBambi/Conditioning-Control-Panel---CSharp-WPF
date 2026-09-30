using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// CCP Default is the plain, gender-neutral mod (owner pivot 2026-09-25): every trigger it ships
/// or defaults to is a classic trance word (drop, sink, relax, deeper, blank, obey...). Niche
/// vocabulary lives in its own mod. These guards fail if a niche word creeps back into any of
/// CCP Default's default pools or into the fresh-install AppSettings defaults.
/// </summary>
public class CcpDefaultClassicTriggersTests
{
    /// <summary>Substrings (case-insensitive) that mark a phrase as niche, not classic neutral.</summary>
    private static readonly string[] Banned =
    {
        "BAMBI", "BIMBO", "SISSY", "DOLL", "GOOD GIRL", "GOOD BOY", "GIRL", "COCK", "CUM",
        "DRONE", "UNIT", "PUPPY", "PUP", "CHASTITY", "LOCKED", "CAGE", "PINK", "PRETTY",
        "GIGGLE", "BLONDE", "PRIMPED", "SNAP AND FORGET", "HER ", "SHE ", "SLUT", "WHORE",
        "PANTIES", "UNIFORM",
    };

    private static IEnumerable<string> Offenders(IEnumerable<string>? phrases) =>
        (phrases ?? Enumerable.Empty<string>())
            .Where(p => Banned.Any(b => (" " + p.ToUpperInvariant() + " ").Contains(
                b.EndsWith(' ') ? " " + b : b, StringComparison.Ordinal)));

    [Fact]
    public void CcpDefault_ManifestPools_AreClassicNeutral()
    {
        var m = BuiltInMods.CCPDefault;
        Assert.Empty(Offenders(m.SubliminalPool?.Keys));
        Assert.Empty(Offenders(m.LockCardPhrases?.Keys));
        Assert.Empty(Offenders(m.CustomTriggers));
        Assert.Empty(Offenders(m.BouncingTextPool?.Keys));
        Assert.Empty(Offenders(new[]
        {
            m.Triggers?.Freeze ?? "", m.Triggers?.Reset ?? "", m.Triggers?.CumAndCollapse ?? "",
        }));
    }

    [Fact]
    public void FreshInstall_Defaults_AreClassicNeutral()
    {
        var s = new AppSettings();
        Assert.Empty(Offenders(s.SubliminalPool.Keys));
        Assert.Empty(Offenders(s.LockCardPhrases.Keys));
        Assert.Empty(Offenders(s.CustomTriggers));
        Assert.Empty(Offenders(s.BouncingTextPool.Keys));
        Assert.Empty(Offenders(s.AttentionPool.Keys));
        Assert.Empty(Offenders(s.MantraPool));
        Assert.Empty(s.KeywordTriggers);
    }

    [Fact]
    public void FreshInstall_CustomTriggers_NameNoBambiVoicedClip()
    {
        // Trigger Mode and subliminals play Resources\sub_audio\<PHRASE>.mp3 by exact name, and
        // every clip in that folder is Bambi-voiced (the 21 names below, audit 2026-09-25). A fresh
        // default phrase that matches one makes CCP Default speak in Bambi's voice.
        var bambiClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BAMBI CUM AND COLLAPSE", "BAMBI DOES AS SHE'S TOLD", "BAMBI FREEZE", "BAMBI RESET",
            "BAMBI SLEEP", "BAMBI UNIFORM LOCK", "BIMBO DOLL", "COCK TURNS MY BRAIN OFF",
            "COCK ZOMBIE NOW", "DONT THINK SILLY", "DROP FOR COCK", "GIGGLETIME", "GOOD GIRL",
            "GOOD GIRLS DONT THINK", "I CANT RESIST MY TRIGGERS", "JUST OBEY", "PRIMPED AND PAMPERED",
            "SNAP AND FORGET", "THERES NO NEED TO THINK", "TURN YOUR BRAIN OFF", "ZAP COCK DRAIN OBEY",
        };
        var s = new AppSettings();
        Assert.DoesNotContain(s.CustomTriggers, t => bambiClips.Contains(t));
        Assert.DoesNotContain(s.SubliminalPool.Keys, t => bambiClips.Contains(t));
        Assert.DoesNotContain(s.LockCardPhrases.Keys, t => bambiClips.Contains(t));
        Assert.DoesNotContain(BuiltInMods.CCPDefault.Triggers!.Freeze!, bambiClips);
        Assert.DoesNotContain(BuiltInMods.CCPDefault.Triggers!.Reset!, bambiClips);
    }
}
