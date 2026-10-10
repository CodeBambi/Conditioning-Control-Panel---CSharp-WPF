using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Owner, 2026-10-10: nothing a remote participant sends may switch Strict Lock on or the panic
/// key off, for any controller, on any tier, leashed or not, under Lockdown or not. One table over every
/// verb name the relay knows, plus the start_session flag and the waiver line that promises it.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteStrictLockRuleTests
{
    private static readonly string[] Tiers = { "light", "standard", "full" };

    private sealed class FakeRelay : HttpMessageHandler
    {
        public string PollBody = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.RequestUri!.AbsolutePath switch
            {
                "/v2/remote/start" => "{\"code\":\"ABC123\"}",
                "/v2/remote/poll" => PollBody,
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    /// <summary>Every verb name the client knows: the execute switch, both label tables, the quiet list and
    /// the leash rule, read from the source so a verb added tomorrow is in the table without an edit here.</summary>
    internal static IReadOnlyList<string> KnownVerbs()
    {
        var root = ArcademyAnimatedWebpHintTests.RepoRoot();
        var verbs = new SortedSet<string>(StringComparer.Ordinal);
        var name = new Regex("\"([a-z]+(?:_[a-z0-9]+)+)\"");
        foreach (var file in new[] { "RemoteControl/RemoteCommands.cs", "RemoteControl/RemoteActionLabels.cs", "Leash/LeashGuard.cs" })
        {
            var src = File.ReadAllText(Path.Combine(root, "CCP.Core", "Services", file));
            foreach (Match m in Regex.Matches(src, "case \"([a-z0-9_]+)\"")) verbs.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(src, "\\[\"([a-z0-9_]+)\"\\]\\s*=")) verbs.Add(m.Groups[1].Value);
        }
        foreach (var v in RemoteCommands.LabelKeys.Keys) verbs.Add(v);
        foreach (var v in RemoteCommands.Quiet) verbs.Add(v);
        // The two that must never run, whether or not any table still names them.
        verbs.Add("enable_strict_lock");
        verbs.Add("disable_panic");
        verbs.RemoveWhere(v => !name.IsMatch("\"" + v + "\""));
        return verbs.ToList();
    }

    [Fact]
    public void The_table_really_holds_the_relays_verbs()
    {
        var verbs = KnownVerbs();
        Assert.True(verbs.Count >= 45, "only " + verbs.Count + " verbs found: the source scan broke");
        foreach (var v in new[] { "start_session", "trigger_panic", "disable_strict_lock", "enable_panic", "haptic_level", "trigger_flash", "start_lock_card" })
            Assert.Contains(v, verbs);
    }

    [Fact]
    public void The_gate_refuses_strict_lock_on_and_panic_off_whatever_Lockdown_says()
    {
        foreach (var lockdown in new[] { false, true })
        {
            Assert.Equal(RemoteCommandGate.StrictLockIsLocal, RemoteCommandGate.Screen("enable_strict_lock", lockdown));
            Assert.Equal(RemoteCommandGate.PanicStays, RemoteCommandGate.Screen("disable_panic", lockdown));
            // The other direction: what only reduces restraint, and the ordinary verbs, still pass.
            foreach (var v in KnownVerbs().Where(v => v != "enable_strict_lock" && v != "disable_panic"))
                Assert.Null(RemoteCommandGate.Screen(v, lockdown));
        }
    }

    [Fact]
    public void There_is_no_execute_case_for_either_verb()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("enable_strict_lock", null));
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("disable_panic", null));
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);

            // The other direction still runs: a controller may always hand restraint back.
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (true, false);
            Assert.Null(RemoteCommands.Execute("disable_strict_lock", null));
            Assert.Null(RemoteCommands.Execute("enable_panic", null));
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled) = saved; }
    }

    /// <summary>Every verb, every tier, leashed and not, with parameters that ask for the worst. The two
    /// safety verbs go to the real executor; the rest go to a spy (they would start real effects), which
    /// proves what reaches a head: never either refused verb, never a strict_lock key.</summary>
    [Fact]
    public async Task No_verb_on_any_tier_turns_strict_lock_on_or_the_panic_key_off()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect);
        var leashed = LeashGuard.IsLeashed;
        const string worst = "{\"strict_lock\":true,\"enabled\":true,\"value\":true,\"panic\":false,\"session_id\":\"x\"}";
        try
        {
            s.StopEffectsOnRemoteDisconnect = false;
            var verbs = KnownVerbs();
            foreach (var tier in Tiers)
            foreach (var onLeash in new[] { false, true })
            {
                LeashGuard.IsLeashed = () => onLeash;
                (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
                var reached = new List<(string Action, JObject? P)>();
                var f = new FakeRelay();
                using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9",
                    (a, p) =>
                    {
                        reached.Add((a, p));
                        return a.Contains("strict") || a.Contains("panic") && a != "trigger_panic" ? RemoteCommands.Execute(a, p) : null;
                    },
                    _ => { }, f) { AutoPoll = false };
                await r.StartAsync(tier);
                var id = 0;
                foreach (var v in verbs)
                {
                    f.PollBody = "{\"controller_connected\":true,\"commands\":[{\"id\":\"" + (++id) + "\",\"action\":\"" + v + "\",\"params\":" + worst + "}]}";
                    await r.PollOnceAsync();
                    Assert.False(s.StrictLockEnabled, $"{v} on {tier} (leashed {onLeash}) switched Strict Lock on");
                    Assert.True(s.PanicKeyEnabled, $"{v} on {tier} (leashed {onLeash}) switched the panic key off");
                }
                Assert.DoesNotContain(reached, x => x.Action is "enable_strict_lock" or "disable_panic");
                Assert.DoesNotContain(reached, x => x.P?.ContainsKey("strict_lock") == true);
                // Both directions: the session start itself, and the verbs that give restraint back, do land.
                Assert.Contains(reached, x => x.Action == "start_session" && (string?)x.P?["session_id"] == "x");
                Assert.Contains(reached, x => x.Action == "disable_strict_lock");
                Assert.Contains(reached, x => x.Action == "enable_panic");
            }
        }
        finally
        {
            LeashGuard.IsLeashed = leashed;
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = saved;
        }
    }

    [Theory]
    [InlineData("{\"strict_lock\":true}", true)]
    [InlineData("{\"strict_lock\":\"true\"}", true)]
    [InlineData("{\"strict_lock\":1}", true)]
    [InlineData("{\"strict_lock\":\"yes\"}", true)]     // any spelling: the key itself is dropped
    [InlineData("{\"strict_lock\":false}", true)]
    [InlineData("{\"session_id\":\"x\"}", false)]
    public void A_session_start_loses_its_strict_lock_key_for_everyone(string json, bool dropped)
    {
        var p = JObject.Parse(json);
        Assert.Equal(dropped, RemoteCommandGate.DropsStrictLockFlag("start_session", p));
        Assert.Equal(dropped, RemoteCommandGate.DropsStrictLockFlag("trigger_flash", p));   // no verb may carry one
        var clean = RemoteCommandGate.WithoutStrictLock(p)!;
        Assert.False(clean.ContainsKey("strict_lock"));
        Assert.False(LeashRemoteRule.AsksStrictLock(clean));
        Assert.Equal(json.Contains("strict_lock"), p.ContainsKey("strict_lock"));   // the sender's object is untouched
        Assert.Null(RemoteCommandGate.WithoutStrictLock(null));
    }

    /// <summary>The waiver the subject accepts says a controller cannot switch the panic key off or turn
    /// Strict Lock on. The sentence and the gate are pinned together: change one, this fails.</summary>
    [Fact]
    public void The_waiver_line_is_true_against_the_gate()
    {
        var en = JObject.Parse(File.ReadAllText(Path.Combine(ArcademyAnimatedWebpHintTests.RepoRoot(),
            "CCP.Core", "Localization", "Languages", "en.json")));
        var line = (string?)en["remote_waiver_panic"] ?? "";
        Assert.Contains("panic key always works", line);
        Assert.Contains("cannot switch it off or turn Strict Lock on", line);
        foreach (var lockdown in new[] { false, true })
        {
            Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdown));
            Assert.NotNull(RemoteCommandGate.Screen("enable_strict_lock", lockdown));
        }
    }
}
