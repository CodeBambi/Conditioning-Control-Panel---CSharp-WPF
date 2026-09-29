using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Presence as a stack: closing one surface shows what was open under it.</summary>
public class PresenceActivityStackTests
{
    [Fact]
    public void Empty_stack_is_the_panel()
    {
        var s = new PresenceActivityStack();
        Assert.Equal(PresenceActivity.Panel, s.Top);
        Assert.Equal(0, s.Count);
    }

    [Theory]
    [InlineData(PresenceActivity.Arcademy)]
    [InlineData(PresenceActivity.Remote)]
    [InlineData(PresenceActivity.Race)]
    public void Closing_a_surface_mid_session_shows_the_session_again(PresenceActivity surface)
    {
        var s = new PresenceActivityStack();
        s.Enter(PresenceActivity.Session);
        s.Enter(surface);
        Assert.Equal(surface, s.Top);
        Assert.True(s.Leave(surface));
        Assert.Equal(PresenceActivity.Session, s.Top);
        Assert.True(s.Leave(PresenceActivity.Session));
        Assert.Equal(PresenceActivity.Panel, s.Top);
    }

    [Fact]
    public void Leaving_out_of_order_never_strands_the_other_surface()
    {
        var s = new PresenceActivityStack();
        s.Enter(PresenceActivity.Session);
        s.Enter(PresenceActivity.Deeper);
        Assert.False(s.Leave(PresenceActivity.Session));   // the top did not move
        Assert.Equal(PresenceActivity.Deeper, s.Top);
        s.Leave(PresenceActivity.Deeper);
        Assert.Equal(PresenceActivity.Panel, s.Top);
    }

    [Fact]
    public void Entering_again_moves_to_the_top_once()
    {
        var s = new PresenceActivityStack();
        s.Enter(PresenceActivity.Goon);
        s.Enter(PresenceActivity.Session);
        Assert.True(s.Enter(PresenceActivity.Goon));
        Assert.Equal(2, s.Count);
        Assert.False(s.Enter(PresenceActivity.Goon));
        s.Leave(PresenceActivity.Goon);
        Assert.Equal(PresenceActivity.Session, s.Top);
    }

    [Fact]
    public void Goon_hosting_sits_on_goon()
    {
        var s = new PresenceActivityStack();
        s.Enter(PresenceActivity.Goon);
        s.Enter(PresenceActivity.GoonHosting);
        Assert.Equal(PresenceActivity.GoonHosting, s.Top);
        s.Leave(PresenceActivity.GoonHosting);   // someone sat down
        Assert.Equal(PresenceActivity.Goon, s.Top);
    }

    [Fact]
    public void Panel_and_offline_are_never_pushed_and_leaving_unknown_is_a_noop()
    {
        var s = new PresenceActivityStack();
        Assert.False(s.Enter(PresenceActivity.Panel));
        Assert.False(s.Enter(PresenceActivity.Offline));
        Assert.False(s.Leave(PresenceActivity.Chess));
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Replace_is_the_old_single_slot()
    {
        var s = new PresenceActivityStack();
        s.Enter(PresenceActivity.Session);
        s.Enter(PresenceActivity.Arcademy);
        Assert.True(s.Replace(PresenceActivity.BackRoom));
        Assert.Equal(1, s.Count);
        Assert.True(s.Replace(PresenceActivity.Panel));
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Chess_travels_on_the_wire_both_ways()
    {
        Assert.Equal("chess", FriendsApi.ActivityToWire(PresenceActivity.Chess));
        Assert.Equal(PresenceActivity.Chess, FriendsApi.ActivityFromWire("chess"));
        foreach (var a in Enum.GetValues<PresenceActivity>())
        {
            if (a == PresenceActivity.Offline) continue;
            Assert.Equal(a, FriendsApi.ActivityFromWire(FriendsApi.ActivityToWire(a)));
        }
    }
}

public partial class FriendsServiceTests
{
    [Fact]
    public async Task Poll_publishes_the_top_of_the_stack()
    {
        var (svc, api, _) = Make(shared: true);
        svc.EnterActivity(PresenceActivity.Session);
        svc.EnterActivity(PresenceActivity.Arcademy);
        await svc.TickAsync();
        Assert.Equal(PresenceActivity.Arcademy, api.PollCalls.Last().Activity);

        svc.LeaveActivity(PresenceActivity.Arcademy);
        await svc.TickAsync();
        Assert.Equal(PresenceActivity.Session, api.PollCalls.Last().Activity);

        svc.LeaveActivity(PresenceActivity.Session);
        await svc.TickAsync();
        Assert.Equal(PresenceActivity.Panel, api.PollCalls.Last().Activity);
    }

    [Fact]
    public async Task Hidden_presence_sends_no_activity_whatever_is_open()
    {
        var (svc, api, _) = Make(shared: false);
        svc.EnterActivity(PresenceActivity.Chess);
        await svc.TickAsync();
        Assert.Equal(((PresenceActivity?)null, (int?)null, false), api.PollCalls.Last());
        Assert.Equal(PresenceActivity.Chess, svc.Activity);
    }

    [Fact]
    public async Task Setting_row_goes_through_the_service_and_spends_the_ask()
    {
        var (svc, api, shared) = Make(shared: false);
        bool asked = false, fallback = false;
        FriendsPresenceSetting.Write(true, svc, _ => fallback = true, () => asked = true);
        Assert.True(shared());
        Assert.True(asked);
        Assert.False(fallback);
        Assert.True(FriendsPresenceSetting.Read(svc, () => false));

        svc.EnterActivity(PresenceActivity.Deeper);
        await svc.TickAsync();
        Assert.Equal(((PresenceActivity?)PresenceActivity.Deeper, (int?)null, true), api.PollCalls.Last());

        FriendsPresenceSetting.Write(false, svc, _ => fallback = true, () => { });
        Assert.False(shared());
        await svc.TickAsync();
        Assert.Equal(((PresenceActivity?)null, (int?)null, false), api.PollCalls.Last());
    }

    [Fact]
    public void Setting_row_without_a_service_writes_the_setting()
    {
        bool saved = false, asked = false;
        FriendsPresenceSetting.Write(true, null, v => saved = v, () => asked = true);
        Assert.True(saved);
        Assert.True(asked);
        Assert.True(FriendsPresenceSetting.Read(null, () => true));
        Assert.False(FriendsPresenceSetting.Read(null, () => throw new InvalidOperationException()));
    }
}

public partial class FriendsDrawerTests
{
    [Fact]
    public void Header_status_is_a_switch_and_answering_it_spends_the_ask()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            bool asked = false;
            PresenceAsk.Asked = () => asked;
            PresenceAsk.MarkAsked = () => asked = true;
            try
            {
                var svc = new FakeFriends(Sample()) { PresenceShared = false };
                var d = new FriendsDrawer(svc) { MeName = () => "cb", MeTier = () => 0 };
                d.Render();

                Assert.IsType<Button>(Find(d.Head, "friends-me-status"));
                Assert.Equal(Loc.Get("friends_me_hidden"), ((TextBlock)Find(d.Head, "friends-me-status-text")!).Text);
                Assert.NotNull(Find(d.Head, "friends-dot-off"));
                Assert.Null(Find(d.Head, "friends-dot-on"));
                Assert.Contains("friends-presence-yes", Tags(d));

                d.TogglePresence();
                Assert.True(svc.PresenceShared);
                Assert.True(asked);
                Assert.Equal(Loc.Get("friends_me_sharing"), ((TextBlock)Find(d.Head, "friends-me-status-text")!).Text);
                Assert.NotNull(Find(d.Head, "friends-dot-on"));
                Assert.DoesNotContain("friends-presence-yes", Tags(d));

                // And back: the switch keeps working after the ask is gone.
                d.TogglePresence();
                Assert.False(svc.PresenceShared);
                Assert.NotNull(Find(d.Head, "friends-dot-off"));
                Assert.DoesNotContain("friends-presence-yes", Tags(d));
            }
            finally
            {
                PresenceAsk.Asked = () => true;
                PresenceAsk.MarkAsked = () => { };
            }
        });
    }

    [Fact]
    public void Signed_out_header_has_no_switch_and_no_dot()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var svc = new FakeFriends(Sample()) { IsAvailable = false };
            var d = NewDrawer(svc);
            Assert.IsType<TextBlock>(Find(d.Head, "friends-me-status"));
            Assert.Null(Find(d.Head, "friends-dot-on"));
            Assert.Null(Find(d.Head, "friends-dot-off"));
            d.TogglePresence();
            Assert.False(svc.PresenceShared);
        });
    }

    [Fact]
    public void Presence_copy_is_in_all_nine_languages()
    {
        var keys = new[]
        {
            "friends_me_sharing", "friends_me_hidden", "friends_presence_ask", "friends_presence_ask_sub",
            "friends_presence_toggle_tip", "friends_presence_setting", "friends_presence_setting_hint",
            "friends_activity_chess",
        };
        var dir = SourceRoots.LanguagesDirectory;
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, lang + ".json")));
            foreach (var k in keys)
            {
                var v = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{lang}: {k}");
                Assert.DoesNotContain("—", v);
                Assert.DoesNotContain("!", v);
            }
        }
    }
}
