using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// ccp-bugs #1303: a modal session recap opened under a topmost lock card or pop quiz disabled
    /// the card and hid Continue behind it. Anything topmost on screen makes the recap passive.
    /// </summary>
    public class SessionSummaryPresentationTests
    {
        [Fact]
        public void NothingOnScreen_OpensModal()
        {
            Assert.Equal(SessionSummaryPresentation.Mode.Modal,
                SessionSummaryPresentation.Decide(videoUp: false, lockCardUp: false, popQuizUp: false));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void AnyTopmostCover_OpensPassive(bool videoUp, bool lockCardUp, bool popQuizUp)
        {
            Assert.Equal(SessionSummaryPresentation.Mode.Passive,
                SessionSummaryPresentation.Decide(videoUp, lockCardUp, popQuizUp));
        }
    }
}
