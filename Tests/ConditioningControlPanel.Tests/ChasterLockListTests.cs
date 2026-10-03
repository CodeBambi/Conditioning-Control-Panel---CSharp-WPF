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

    /// <summary>Ticket (2026-10-03): a keyholder lock running a scripted extension carries its
    /// Blockly program nested past 64 levels, and the whole list read as "offline".</summary>
    [Fact]
    public void A_lock_with_a_deeply_nested_extension_is_still_read()
    {
        const int depth = 300;
        var deep = new System.Text.StringBuilder();
        for (int i = 0; i < depth; i++) deep.Append("{\"block\":{\"next\":");
        deep.Append("{\"fields\":{\"AMOUNT\":5}}");
        for (int i = 0; i < depth; i++) deep.Append("}}");
        var body = "[{\"_id\":\"k1\",\"title\":\"Locktober\",\"status\":\"active\",\"role\":\"wearer\","
                   + "\"endDate\":\"2026-10-31T23:00:00.000Z\","
                   + "\"extensions\":[{\"slug\":\"script\",\"config\":{\"codeHistory\":[{\"blockly\":" + deep + "}]}}]}]";

        var only = Assert.Single(ChasterClient.ParseLocks(body));
        Assert.Equal("k1", only.Id);
        Assert.Equal("Locktober", only.Title);
        Assert.NotNull(only.EndDate);
    }
}
