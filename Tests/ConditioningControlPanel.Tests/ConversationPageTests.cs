using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion.V2;
using Xunit;
namespace ConditioningControlPanel.Tests;
public sealed class ConversationPageTests
{
    [Theory]
    [InlineData(TurnKind.UserChat, true)]
    [InlineData(TurnKind.AssistantChat, true)]
    [InlineData(TurnKind.AmbientEvent, false)]
    [InlineData(TurnKind.AmbientReply, false)]
    [InlineData(TurnKind.BarkEcho, false)]
    [InlineData(TurnKind.SystemNote, false)]
    public void TranscriptIncludesOnlyActualConversation(TurnKind kind, bool expected)
        => Assert.Equal(expected, ConversationPageVm.Shows(kind));
}
