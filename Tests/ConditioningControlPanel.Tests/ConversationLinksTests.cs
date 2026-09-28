using System;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion.V2;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class ConversationLinksTests
{
    private const string Title = "Deep Acceptance";
    private const string Url = "https://hypnotube.com/video/deep-acceptance-113157.html";

    private static CompanionLinkIndex.Entry? Finder(string? text) =>
        text != null && text.Contains(Title, StringComparison.OrdinalIgnoreCase) ? new CompanionLinkIndex.Entry(Title, Url) : null;

    private static CompanionTurn Turn(TurnKind kind, string text, bool app = false) =>
        new("t1", DateTime.UtcNow, kind, text, null, false, 1) { IsApplicationReply = app };

    [Theory]
    [InlineData("Click here: [Deep Acceptance]", "Click here: Deep Acceptance")]
    [InlineData("Try this one [PLAY THE VIDEO: Deep Acceptance] now~", "Try this one Deep Acceptance now~")]
    [InlineData("Watch Deep Acceptance for me. [PLAY THE VIDEO]", "Watch Deep Acceptance for me.")]
    [InlineData("Go on: [click here]", "Go on")]
    [InlineData("Deep Acceptance, and [whispers] relax", "Deep Acceptance, and [whispers] relax")]
    [InlineData("No brackets here, Deep Acceptance.", "No brackets here, Deep Acceptance.")]
    public void TidyKeepsTheTitleAndDropsDeadBrackets(string input, string expected)
        => Assert.Equal(expected, ConversationLinks.Tidy(input, Title));

    [Fact]
    public void TidyWithoutATitleOnlyDropsPlaceholders()
    {
        Assert.Equal("Here you go.", ConversationLinks.Tidy("Here you go. [video link]", null));
        Assert.Equal("[giggles] hi", ConversationLinks.Tidy("[giggles] hi", null));
    }

    [Fact]
    public void AssistantReplyNamingATitleGetsALink()
    {
        var line = ConversationPageVm.Line(Turn(TurnKind.AssistantChat, "Click here: [Deep Acceptance]"), "Companion", Finder);
        Assert.True(line.HasLink);
        Assert.Equal(Title, line.LinkTitle);
        Assert.Equal(Url, line.LinkUrl);
        Assert.Equal("Click here: Deep Acceptance", line.Text);
        Assert.False(line.IsUser);
    }

    [Fact]
    public void UserTurnsAndAppRepliesGetNoLink()
    {
        var user = ConversationPageVm.Line(Turn(TurnKind.UserChat, "play [Deep Acceptance]"), "Companion", Finder);
        Assert.False(user.HasLink);
        Assert.Equal("play [Deep Acceptance]", user.Text);

        var app = ConversationPageVm.Line(Turn(TurnKind.AssistantChat, "Opening [Deep Acceptance]", app: true), "Companion", Finder);
        Assert.False(app.HasLink);
        Assert.Equal("Opening [Deep Acceptance]", app.Text);
    }

    [Fact]
    public void ReplyWithoutATitleHasNoLink()
    {
        var line = ConversationPageVm.Line(Turn(TurnKind.AssistantChat, "Just breathe."), "Companion", Finder);
        Assert.False(line.HasLink);
        Assert.Equal("Just breathe.", line.Text);
    }

    [Theory]
    [InlineData("hello", true, true)]
    [InlineData("hello", false, false)]
    [InlineData("  ", true, false)]
    [InlineData(null, true, false)]
    public void SpeaksOnlyWithAnAvatarAndText(string? text, bool enabled, bool expected)
        => Assert.Equal(expected, ConversationPageVm.ShouldSpeak(text, enabled));
}
