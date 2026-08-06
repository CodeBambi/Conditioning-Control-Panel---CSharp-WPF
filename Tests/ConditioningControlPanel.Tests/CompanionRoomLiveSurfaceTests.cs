using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The second review pass on "Her Room": the zones that rendered once and then quietly stopped.
///
/// <para><b>The shared root cause.</b> Three zones exposed a private <c>List&lt;T&gt;</c>, refilled
/// it in place, and raised <c>PropertyChanged</c> to push it. WPF suppresses an <c>ItemsSource</c>
/// change whose new value is reference-equal to the old one, so <c>OnItemsSourceChanged</c> never
/// ran and the containers were never regenerated. Every one of those surfaces drew its first frame
/// and then froze — the Live Actions feed, the profile strip on the app's own "what do you have on
/// me?" card, and the preset chips (which, being ungrouped ToggleButtons bound TwoWay, then lit
/// every preset the user had ever clicked).</para>
///
/// <para>The viewmodels need <c>App.Settings</c>, <c>App.Brain</c> and a dispatcher, so as with
/// <see cref="CompanionRoomWiringTests"/> nothing is constructed here. What can be pinned without a
/// window is pinned: the collection TYPES that make notification possible, the pure helpers, and the
/// brain-level events the page now listens to.</para>
/// </summary>
public class CompanionRoomLiveSurfaceTests
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static FieldInfo Field(Type owner, string name)
    {
        var field = owner.GetField(name, Instance);
        Assert.True(field != null, $"{owner.Name}.{name} is gone — if it was renamed, rename it here too.");
        return field!;
    }

    private static void AssertNotifies(Type owner, string fieldName)
    {
        var type = Field(owner, fieldName).FieldType;
        Assert.True(typeof(INotifyCollectionChanged).IsAssignableFrom(type),
            $"{owner.Name}.{fieldName} is a {type.Name}. It is mutated in place and bound to an " +
            "ItemsControl, so it has to raise CollectionChanged — a PropertyChanged raise on a " +
            "same-instance collection is a no-op in WPF and the surface will render once and freeze.");
    }

    // =====================================================================================
    //  the three in-place-mutated collections
    // =====================================================================================

    [Fact]
    public void Z4PresetChips_NotifyOnRebuild()
        => AssertNotifies(typeof(MakeHerYoursRuntimeVm), "_presets");

    [Fact]
    public void Z3ProfileStrip_NotifiesOnRebuild()
        => AssertNotifies(typeof(MemoryDiaryRuntimeVm), "_profile");

    [Fact]
    public void Z2Thread_NotifiesOnRebuild()
        => AssertNotifies(typeof(ChatThresholdRuntimeVm), "_turns");

    /// <summary>
    /// Z7's feed is <c>App.AiLiveActions</c> itself — the collection <c>AiCommandService</c> appends
    /// to — rather than a private copy kept in sync by a CollectionChanged hook. That copy was both
    /// the frozen-feed bug and a subscription the viewmodel never unhooked.
    /// </summary>
    [Fact]
    public void Z7LiveActions_KeepsNoPrivateCopyOfTheFeed()
    {
        var copies = typeof(EngineRoomRuntimeVm)
            .GetFields(Instance)
            .Where(f => f.FieldType != typeof(string) &&
                        typeof(IEnumerable<string>).IsAssignableFrom(f.FieldType))
            .Select(f => f.Name)
            .ToList();

        Assert.True(copies.Count == 0,
            "EngineRoomRuntimeVm holds its own string collection again (" + string.Join(", ", copies) +
            "). LiveActions must hand out App.AiLiveActions so the feed is live and nothing has to " +
            "be unsubscribed.");
    }

    // =====================================================================================
    //  Z7 — switching provider drops a stale feed
    // =====================================================================================

    /// <summary>
    /// The bug: the clear keyed off the <c>enabled</c> flag, and <c>SettingsFor</c> maps Off to
    /// <c>(false, Cloud)</c> — so Cloud, which the old <c>RadioAiCloud_Checked</c> also cleared for
    /// ("Cloud can't trigger effects, so prior local-session entries would be misleading"), silently
    /// stopped clearing. Run Local, switch to Cloud, come back, and last session's effects rendered
    /// as current.
    /// </summary>
    [Fact]
    public void ProviderSwitch_ClearsTheFeedForOffAndCloud_AndOnlyThose()
    {
        Assert.True(EngineRoomRuntimeVm.ClearsLiveActions(CompanionProviderMode.Off));
        Assert.True(EngineRoomRuntimeVm.ClearsLiveActions(CompanionProviderMode.Cloud));
        Assert.False(EngineRoomRuntimeVm.ClearsLiveActions(CompanionProviderMode.LocalOllama));
        Assert.False(EngineRoomRuntimeVm.ClearsLiveActions(CompanionProviderMode.Custom));

        // The reason the enabled flag cannot stand in for this.
        Assert.Equal(EngineRoomRuntimeVm.SettingsFor(CompanionProviderMode.Off).Provider,
                     EngineRoomRuntimeVm.SettingsFor(CompanionProviderMode.Cloud).Provider);
    }

    // =====================================================================================
    //  Z2 — the thread follows the log instead of waiting for a tab switch
    // =====================================================================================

    /// <summary>
    /// Both chips this page offers open the TUBE's input box, so a user could have a whole exchange
    /// from a button Z2 gave them and watch Z2 keep showing the turns it had on tab entry. Bark
    /// echoes — the "one mouth, made visible" promise — never appeared at all while the tab was up.
    /// The session now announces the log changing; the zone marshals and re-projects.
    /// </summary>
    [Fact]
    public void ChatSession_AnnouncesEveryChangeToTheLog()
    {
        var session = new ChatSession();
        int changes = 0;
        session.TurnsChanged += (_, _) => changes++;

        var user = session.Append(TurnKind.UserChat, "hi");
        Assert.Equal(1, changes);

        session.Append(TurnKind.AssistantChat, "hi back~");
        Assert.Equal(2, changes);

        // A bark echo is exactly the case tab-entry-only refresh swallowed.
        session.Append(TurnKind.BarkEcho, CompanionTurn.FormatBarkEcho("Bambi", "good girl~"));
        Assert.Equal(3, changes);

        // P2/H5 rollback: the user's turn coming back out has to un-draw too.
        Assert.True(session.Remove(user));
        Assert.Equal(4, changes);

        // …but a rollback that found nothing changed nothing.
        Assert.False(session.Remove(user));
        Assert.Equal(4, changes);

        session.Clear();
        Assert.Equal(5, changes);
    }

    [Fact]
    public void ChatSession_UnsubscribingReallyStops()
    {
        var session = new ChatSession();
        int changes = 0;
        void Handler(object? s, EventArgs e) => changes++;

        session.TurnsChanged += Handler;
        session.Append(TurnKind.UserChat, "one");
        session.TurnsChanged -= Handler;
        session.Append(TurnKind.UserChat, "two");

        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A handler that throws must not abort an append that already happened — the log is mutated
    /// before the event is raised, and a torn-down page is not a reason to lose a turn.
    /// </summary>
    [Fact]
    public void ChatSession_SurvivesAThrowingSubscriber()
    {
        var session = new ChatSession();
        session.TurnsChanged += (_, _) => throw new InvalidOperationException("torn down");

        session.Append(TurnKind.UserChat, "still lands");

        Assert.Single(session.Turns);
        Assert.Equal("still lands", session.Turns[0].Text);
    }

    // =====================================================================================
    //  …without repainting three identical bubbles every ambient turn
    // =====================================================================================

    /// <summary>
    /// Ambient turns land every ~10s and never render in Z2, so the refresh needs a cheap "did this
    /// actually change?" gate or the ItemsControl tears down and regenerates the same three
    /// containers on a timer. The projected timestamp is part of the signature, so "22m ago" →
    /// "1h ago" still repaints.
    /// </summary>
    [Fact]
    public void ThreadSignature_IsStableForAnIdenticalProjection()
    {
        var a = new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.You, "hi", timestamp: "22m ago"),
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi back~", isAi: true, timestamp: "22m ago")
        };
        var b = new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.You, "hi", timestamp: "22m ago"),
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi back~", isAi: true, timestamp: "22m ago")
        };

        Assert.Equal(ChatThresholdRuntimeVm.SignatureFor(a), ChatThresholdRuntimeVm.SignatureFor(b));
        Assert.Equal(string.Empty, ChatThresholdRuntimeVm.SignatureFor(Array.Empty<IChatBubbleVm>()));
    }

    [Fact]
    public void ThreadSignature_MovesWhenAnythingTheUserCanSeeMoves()
    {
        var baseline = new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi back~", isAi: true, timestamp: "22m ago")
        };
        var signature = ChatThresholdRuntimeVm.SignatureFor(baseline);

        // a new turn
        Assert.NotEqual(signature, ChatThresholdRuntimeVm.SignatureFor(new IChatBubbleVm[]
        {
            baseline[0], new CompanionChatBubble(CompanionBubbleKind.You, "still there?")
        }));

        // different text
        Assert.NotEqual(signature, ChatThresholdRuntimeVm.SignatureFor(new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Her, "hello~", isAi: true, timestamp: "22m ago")
        }));

        // the clock moved on
        Assert.NotEqual(signature, ChatThresholdRuntimeVm.SignatureFor(new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi back~", isAi: true, timestamp: "1h ago")
        }));

        // the AI badge is part of what is drawn, so it is part of the signature
        Assert.NotEqual(signature, ChatThresholdRuntimeVm.SignatureFor(new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Her, "hi back~", isAi: false, timestamp: "22m ago")
        }));

        // …and so is which side of the thread it sits on
        Assert.NotEqual(signature, ChatThresholdRuntimeVm.SignatureFor(new IChatBubbleVm[]
        {
            new CompanionChatBubble(CompanionBubbleKind.Echo, "hi back~", isAi: true, timestamp: "22m ago")
        }));
    }

    // =====================================================================================
    //  the privacy card may not promise what the code does not do
    // =====================================================================================

    /// <summary>
    /// The same two lines, one train later.
    ///
    /// <para><b>History.</b> Z5 first shipped "incognito is always invisible" and "page titles stay
    /// hidden unless you allow them" in nine languages while neither existed in code, so both were
    /// demoted to explicit Train 2 promises and this suite pinned the word "Train 2" into them.
    /// Train 2 has now landed — <c>AwarenessPrivacyRules</c> drops private windows before anything is
    /// counted and returns a title only for an app the user allow-listed — so deferring would be the
    /// new lie. The assertion inverts with the behaviour, and the promise is now checked against the
    /// code that keeps it rather than against a release name.</para>
    /// </summary>
    [Theory]
    [InlineData("companion_awareness_incognito")]
    [InlineData("companion_awareness_wire_caption")]
    public void AwarenessCopy_MatchesTheMergedPrivacyLayer(string key)
    {
        foreach (var language in CompanionLocMasters.Languages)
        {
            var value = CompanionLocMasters.For(language).TryGetValue(key, out var v) ? v : null;
            Assert.True(!string.IsNullOrWhiteSpace(value), $"{language}.json is missing {key}");

            // "Train 2" is untranslated in all nine files, which makes it the one language-agnostic
            // assertion available here: a shipped capability may not still be described as coming.
            Assert.DoesNotContain("Train 2", value!, StringComparison.Ordinal);
        }

        var english = CompanionLocMasters.Get(key);
        Assert.DoesNotContain("always invisible", english, StringComparison.OrdinalIgnoreCase);

        // …and the two claims those lines make, asserted against the merged code path.
        Assert.True(AwarenessPrivacyRules.LooksIncognito("Bank — InPrivate — Microsoft Edge"));
        Assert.True(AwarenessPrivacyRules.LooksIncognito("Something (Incognito) - Google Chrome"));
        Assert.False(AwarenessPrivacyRules.IsTitleAllowed("chrome", "Chrome", null, new AppSettings()));
    }

    /// <summary>
    /// <c>Loc.GetF</c> is a plain <c>string.Format</c> with no plural rules, so a Russian counted
    /// noun has to be reworded into a shape that is grammatical for every value of {0}: "чат" for
    /// 1/21/31…, "чата" for 2-4, "чатов" for 5-20. Both of these hardcoded the genitive plural.
    /// </summary>
    [Theory]
    [InlineData("companion_attention_detail_fmt")]
    [InlineData("companion_engine_status_ready_fmt")]
    public void RussianCountedNouns_DoNotAgreeWithASubstitutedNumber(string key)
    {
        var value = CompanionLocMasters.For("ru")[key];
        Assert.Contains("{0}", value, StringComparison.Ordinal);

        // The failure shape: a number immediately followed by a form that only fits some numbers.
        Assert.DoesNotContain("{0} чатов", value, StringComparison.Ordinal);
        Assert.DoesNotContain("{0} чата", value, StringComparison.Ordinal);
        Assert.DoesNotContain("{0} чат", value, StringComparison.Ordinal);
    }

    /// <summary>
    /// es.json is tú/region-neutral across ~3,800 keys; two new strings switched to peninsular
    /// vosotros, which reads as foreign to the larger Latin American share of that audience — and
    /// one of them is a card heading.
    /// </summary>
    [Fact]
    public void SpanishStaysRegionNeutral_NoVosotros()
    {
        var markers = new[] { "habéis", "tenéis", "podéis", "queréis", "vosotros", "vuestro", "vuestra" };
        foreach (var key in new[] { "companion_chat_history_title", "companion_constellation_dormant" })
        {
            var value = CompanionLocMasters.For("es")[key];
            foreach (var marker in markers)
                Assert.DoesNotContain(marker, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
