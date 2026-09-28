using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PhrasePoolCustody.FoldUserPoolEdit, copied verbatim from WPF SessionEngine.cs:1224-1270 (#906).
/// No WPF tests existed to port; these are written from the oracle's list.</summary>
public sealed class PhrasePoolFoldTests
{
    private static Dictionary<string, bool> D(params (string K, bool V)[] kv) => kv.ToDictionary(p => p.K, p => p.V);

    private static (Dictionary<string, bool>? Saved, Dictionary<string, bool>? Prescribed) Fold(
        Dictionary<string, bool>? live, Dictionary<string, bool>? prescribed, Dictionary<string, bool>? saved)
    {
        PhrasePoolCustody.FoldUserPoolEdit(live, ref prescribed, ref saved);
        return (saved, prescribed);
    }

    [Fact]
    public void Fold_UnprescribedPool_AdoptsLive()
    {
        var live = D(("b", false));
        var (saved, prescribed) = Fold(live, null, D(("a", true)));
        Assert.Equal(live, saved);
        Assert.NotSame(live, saved);
        Assert.Null(prescribed);
    }

    [Fact]
    public void Fold_NullLiveOrSaved_NoOp()
    {
        var p = D(("s", true));
        var s = D(("u", true));
        var (saved, prescribed) = Fold(null, p, s);
        Assert.Same(s, saved); Assert.Same(p, prescribed);
        (saved, prescribed) = Fold(D(("x", true)), p, null);
        Assert.Null(saved); Assert.Same(p, prescribed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Fold_AddedPhrase_Adopted(bool value)
    {
        var (saved, _) = Fold(D(("u", false), ("s", true), ("new", value)), D(("u", false), ("s", true)), D(("u", true)));
        Assert.Equal(D(("u", true), ("new", value)), saved);
    }

    [Fact]
    public void Fold_DeletedPhrase_RemovedFromSaved()
    {
        var (saved, _) = Fold(D(("u", false), ("s", true)), D(("u", false), ("v", false), ("s", true)), D(("u", true), ("v", true)));
        Assert.Equal(D(("u", true)), saved);
    }

    [Fact]
    public void Fold_RetoggleOwnPhrase_Adopted()
    {
        // u is the user's and also a session phrase (prescribed true); unticking it differs from the prescription.
        var (saved, _) = Fold(D(("u", false)), D(("u", true)), D(("u", true)));
        Assert.Equal(D(("u", false)), saved);
    }

    [Fact]
    public void Fold_EqualFalse_NotAdopted()
    {
        var (saved, _) = Fold(D(("u", false)), D(("u", false)), D(("u", true)));
        Assert.Equal(D(("u", true)), saved);
    }

    [Fact]
    public void Fold_EqualTrue_Adopted()
    {
        var (saved, _) = Fold(D(("u", true)), D(("u", true)), D(("u", false)));
        Assert.Equal(D(("u", true)), saved);
    }

    [Fact]
    public void Fold_SessionPhrase_NeverLeaks()
    {
        var (saved, _) = Fold(D(("u", false), ("s", true)), D(("u", false), ("s", true)), D(("u", true)));
        Assert.False(saved!.ContainsKey("s"));
        (saved, _) = Fold(D(("u", false), ("s", false)), D(("u", false), ("s", true)), D(("u", true)));
        Assert.False(saved!.ContainsKey("s"));
    }

    [Fact]
    public void Fold_RebasesPrescribed()
    {
        Dictionary<string, bool>? prescribed = D(("u", false), ("s", true));
        Dictionary<string, bool>? saved = D(("u", true));
        PhrasePoolCustody.FoldUserPoolEdit(D(("u", false), ("s", true), ("x", true)), ref prescribed, ref saved);
        Assert.True(saved!["x"]);
        Assert.True(prescribed!.ContainsKey("x"));
        PhrasePoolCustody.FoldUserPoolEdit(D(("u", false), ("s", true)), ref prescribed, ref saved);
        Assert.Equal(D(("u", true)), saved);
    }
}
