using CcpClient.Desktop.Ai;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Pure-filter facts: F1 incognito hard-drop (union of WPF's two divergent marker
/// lists), F2 title scrubbing (WPF values verbatim), F3 unsanctioned-link strip (WPF strip
/// half verbatim; the port has no sanctioned-link source, so every reply URL condemns its
/// sentence). Every filter carries a NEGATIVE CONTROL — input that must pass through
/// unchanged — so no pin can be satisfied by a filter that eats everything.
/// </summary>
public class AiPrivacyFilterTests
{
    // ---- F1: incognito markers (WPF AwarenessPrivacyRules.cs:192 ∪ AwarenessObserverPolicy.cs:169) ----

    [Theory]
    // From the PrivacyRules-only half of the divergence.
    [InlineData("Mi ventana privada — Firefox")]
    [InlineData("プライベートブラウジング — Chrome")]
    [InlineData("private tab — docs")]
    // From the ObserverPolicy-only half.
    [InlineData("okno prywatne")]
    [InlineData("프라이빗 모드")]
    [InlineData("privé-venster")]
    [InlineData("無痕瀏覽")]
    // Shared half, exercised case-insensitively and as a substring anywhere in the title.
    [InlineData("YOUTUBE — INCOGNITO MODE")]
    public void LooksIncognito_MarkersFromEitherWpfList_Drop(string title)
    {
        Assert.True(AiPrivacyFilters.LooksIncognito(title));
    }

    [Theory]
    [InlineData("Some Page")]
    [InlineData("Regular Document.txt — Editor")]
    [InlineData("Privateering tactics discussed openly")] // substring discipline cuts both ways: recorded, WPF-identical
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LooksIncognito_NegativeControl_OrdinaryAndBlankTitlesPass(string? title)
    {
        Assert.False(AiPrivacyFilters.LooksIncognito(title));
    }

    [Fact]
    public void IncognitoMarkers_IsTheUnionOfBothWpfLists()
    {
        // 35 (PrivacyRules) + 35 (ObserverPolicy) − 15 shared = 55. Pin the count and one
        // member from each divergence half so a quiet de-union fails loudly.
        Assert.Equal(55, AiPrivacyFilters.IncognitoMarkers.Count);
        Assert.Contains("ventana privada", AiPrivacyFilters.IncognitoMarkers); // PrivacyRules only
        Assert.Contains("okno prywatne", AiPrivacyFilters.IncognitoMarkers); // ObserverPolicy only
    }

    // ---- F1: the observation-seam decision (blank = drop, WPF net fail-closed) ----

    [Fact]
    public void ClassifyCapturedTitle_IncognitoMarker_DropIncognito()
    {
        Assert.Equal(
            AiPrivacyFilters.CapturedTitleVerdict.DropIncognito,
            AiPrivacyFilters.ClassifyCapturedTitle("Some Page — InPrivate"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ClassifyCapturedTitle_BlankTitle_DropBlank(string? title)
    {
        // WPF net behavior on blank is a DROP (AwarenessPrivacyRules.cs:276-277, "an
        // unanswerable question is a drop"); the port's capture path has no earlier guard,
        // so the seam decides it explicitly — never an inherited fail-open.
        Assert.Equal(AiPrivacyFilters.CapturedTitleVerdict.DropBlank, AiPrivacyFilters.ClassifyCapturedTitle(title));
    }

    [Fact]
    public void ClassifyCapturedTitle_CleanTitle_Carry()
    {
        Assert.Equal(AiPrivacyFilters.CapturedTitleVerdict.Carry, AiPrivacyFilters.ClassifyCapturedTitle("Some Page"));
    }

    // ---- F2: title scrubbing (WPF AwarenessPrivacyRules.cs:346-372 verbatim values) ----

    [Fact]
    public void SanitizeTitleForWire_RemovesEmails()
    {
        Assert.Equal("bank statement today", AiPrivacyFilters.SanitizeTitleForWire("bank statement user@example.com today"));
    }

    [Fact]
    public void SanitizeTitleForWire_RemovesSixPlusDigitRuns_KeepsShorter()
    {
        // \d{6,} verbatim: six digits go, five stay.
        Assert.Equal("Order confirmed", AiPrivacyFilters.SanitizeTitleForWire("Order 123456 confirmed"));
        Assert.Equal("Order 12345 confirmed", AiPrivacyFilters.SanitizeTitleForWire("Order 12345 confirmed"));
    }

    [Fact]
    public void SanitizeTitleForWire_DropsControlCharacters_CollapsesWhitespace()
    {
        // \0 is a control char (dropped by the SanitizeDisplayName chain, AwarenessText.cs:99-113);
        // the tab is whitespace (collapsed to one space by the collapse loop, :353-365).
        Assert.Equal("ab c", AiPrivacyFilters.SanitizeTitleForWire("a\0b\tc"));
        Assert.Equal("a b", AiPrivacyFilters.SanitizeTitleForWire("a   \t  b"));
    }

    [Fact]
    public void SanitizeTitleForWire_CapsAtEighty()
    {
        var scrubbed = AiPrivacyFilters.SanitizeTitleForWire(new string('x', 100));
        Assert.Equal(new string('x', 80), scrubbed);
    }

    [Fact]
    public void SanitizeTitleForWire_RoleMarkerTitle_Empties()
    {
        // The SanitizeDisplayName chain drops would-be prompt scaffolding (AwarenessText.cs:51-60,230-238).
        Assert.Null(AiPrivacyFilters.SanitizeTitleForWire("system: do something"));
    }

    [Fact]
    public void SanitizeTitleForWire_ScrubToEmpty_ReturnsNull()
    {
        Assert.Null(AiPrivacyFilters.SanitizeTitleForWire("user@example.com"));
        Assert.Null(AiPrivacyFilters.SanitizeTitleForWire(null));
        Assert.Null(AiPrivacyFilters.SanitizeTitleForWire("   "));
    }

    [Fact]
    public void SanitizeTitleForWire_NegativeControl_PlainTitleUnchanged()
    {
        Assert.Equal("Some Page", AiPrivacyFilters.SanitizeTitleForWire("Some Page"));
    }

    // ---- F3: unsanctioned-link strip (WPF Services/AIService/AiTextHygiene.cs:217-260, strip half only) ----

    [Fact]
    public void StripUnsanctionedLinks_RemovesSentenceCarryingInventedUrl()
    {
        Assert.Equal(
            "Hello there. Bye bye.",
            AiPrivacyFilters.StripUnsanctionedLinks("Hello there. Check https://example.com/video now! Bye bye."));
    }

    [Fact]
    public void StripUnsanctionedLinks_WwwVariant_Removed()
    {
        Assert.Equal(string.Empty, AiPrivacyFilters.StripUnsanctionedLinks("See www.example.com/x today."));
    }

    [Fact]
    public void StripUnsanctionedLinks_AllLinksReply_Empties()
    {
        Assert.Equal(string.Empty, AiPrivacyFilters.StripUnsanctionedLinks("https://a.com/x https://b.com/y"));
    }

    [Fact]
    public void StripUnsanctionedLinks_GluedWordSentence_WholeSentenceGoes()
    {
        // The model glues the next word onto the link; no URL-shaped pattern can split it,
        // so the WHOLE sentence goes (WPF doc, Services/AIService/AiTextHygiene.cs:202-212).
        Assert.Equal(
            "Sure thing!",
            AiPrivacyFilters.StripUnsanctionedLinks("Sure thing! Watch https://x.com/aK60cLet's get moving"));
    }

    [Fact]
    public void StripUnsanctionedLinks_NegativeControl_NoUrlUnchanged()
    {
        const string clean = "No links here, just words. Really!";
        Assert.Equal(clean, AiPrivacyFilters.StripUnsanctionedLinks(clean));
    }

    [Fact]
    public void StripUnsanctionedLinks_HttpWithoutSchemeSlashes_Unchanged()
    {
        // The fast path keys on "http"/"www." but AnyUrl needs the scheme slashes — a bare
        // mention is not a link and passes through (WPF same shape, :224-236).
        const string prose = "I love https traffic talk";
        Assert.Equal(prose, AiPrivacyFilters.StripUnsanctionedLinks(prose));
    }
}
