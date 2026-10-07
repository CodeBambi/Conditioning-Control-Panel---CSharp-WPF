using System.Linq;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #966: the Programs tab lists the active mod's programs first.</summary>
public class ProgramBrowseOrderTests
{
    private static ProgramDefinition P(string id, string mod) => new() { Id = id, ModId = mod };

    [Fact]
    public void Active_mod_first_then_no_mod_then_other_mods_keeping_library_order()
    {
        var library = new[]
        {
            P("a", "mod-sissy"), P("b", ""), P("c", "mod-bambi"), P("d", "mod-sissy"),
            P("e", ""), P("f", "MOD-BAMBI")
        };

        var ids = ProgramBrowseOrder.Sort(library, "mod-bambi").Select(p => p.Id);

        Assert.Equal(new[] { "c", "f", "b", "e", "a", "d" }, ids);
    }

    [Fact]
    public void No_active_mod_puts_unmodded_programs_first()
    {
        var library = new[] { P("a", "mod-sissy"), P("b", ""), P("c", "mod-bambi") };

        Assert.Equal(new[] { "b", "a", "c" }, ProgramBrowseOrder.Sort(library, null).Select(p => p.Id));
        Assert.Equal(new[] { "b", "a", "c" }, ProgramBrowseOrder.Sort(library, " ").Select(p => p.Id));
    }

    [Fact]
    public void Null_library_is_empty()
    {
        Assert.Empty(ProgramBrowseOrder.Sort(null, "mod-bambi"));
    }
}
