using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1332: one lock the model cannot read must not turn the whole list into
/// "Chaster is unreachable".</summary>
public class ChasterLockListTests
{
    [Fact]
    public void A_lock_with_an_unreadable_field_is_skipped_and_the_others_survive()
    {
        var body = """
        [
          {"_id":"a1","title":"Mine","status":"active","role":"wearer","endDate":"2026-10-20T10:00:00.000Z"},
          {"_id":"b2","title":"Theirs","status":"active","role":"wearer","endDate":"not a date"},
          {"_id":"c3","title":"Third","status":"active","role":"wearer"}
        ]
        """;
        var locks = ChasterClient.ParseLocks(body);
        Assert.Equal(new[] { "a1", "c3" }, locks.ConvertAll(l => l.Id).ToArray());
    }

    [Fact]
    public void An_empty_body_is_no_locks() => Assert.Empty(ChasterClient.ParseLocks(""));
}
