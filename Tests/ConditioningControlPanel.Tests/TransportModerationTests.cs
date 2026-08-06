using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — the Layer-1 moderation spine, exercised through the REAL provider-configured objects
/// (<c>LocalAiService.CreateModeration()</c> / <c>OpenAiCompatibleService.CreateModeration()</c>), not
/// a hand-built stand-in. Giving each provider a second entry point (SendAsync) alongside its legacy
/// one-shot path is precisely the situation that produced a whole release of
/// <see cref="OpenAiCompatibleService"/> with no moderation at all, so these assertions are about a
/// compliance guarantee, not a code style.
///
/// <para>What must hold, per provider:</para>
/// <list type="number">
/// <item>hard categories BLOCK on input and on output;</item>
/// <item>every hit — hard or soft — is written to the CCBill log with that provider's model hint;</item>
/// <item>only user-typed input escalates <c>ModerationCounter</c>; background reactions and model
/// output never do.</item>
/// </list>
/// </summary>
public class TransportModerationTests
{
    // ---------- fakes ----------

    private sealed class FakeGuard : IModerationGuard
    {
        public ModerationResult InputResult { get; set; } = ModerationResult.Pass();
        public ModerationResult OutputResult { get; set; } = ModerationResult.Pass();
        public List<string> InputsSeen { get; } = new();
        public List<string> OutputsSeen { get; } = new();

        public ModerationResult CheckInput(string text) { InputsSeen.Add(text); return InputResult; }
        public ModerationResult CheckOutput(string text) { OutputsSeen.Add(text); return OutputResult; }
    }

    private sealed class Recorder
    {
        public List<(ProhibitedCategory Category, string Source, string ModelHint)> LogWrites { get; } = new();
        public List<(ProhibitedCategory Category, string Source)> CounterHits { get; } = new();
    }

    /// <summary>Wires a provider's real spine to fakes and hands back both.</summary>
    private static (TransportModeration Moderation, FakeGuard Guard, Recorder Sink) Wire(TransportModeration moderation)
    {
        var guard = new FakeGuard();
        var sink = new Recorder();
        moderation.GuardOverride = () => guard;
        moderation.RecordOverride = (cat, source, hint) => sink.LogWrites.Add((cat, source, hint));
        moderation.CounterOverride = (cat, source) => sink.CounterHits.Add((cat, source));
        return (moderation, guard, sink);
    }

    public static TheoryData<string> Providers => new() { "local", "openai_compat" };

    private static TransportModeration ForProvider(string provider) => provider switch
    {
        "local" => LocalAiService.CreateModeration(),
        "openai_compat" => OpenAiCompatibleService.CreateModeration(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    // ---------- input ----------

    [Theory]
    [MemberData(nameof(Providers))]
    public void BlockedInput_IsRefused_Logged_AndEscalatedOnlyWhenTheUserTypedIt(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));
        guard.InputResult = ModerationResult.Block(ProhibitedCategory.Minor, "test");

        // Interactive = the chat box. This is the only case that may spend the user's
        // Content Policy Notice budget.
        var blocked = moderation.CheckInput("prohibited text", escalate: true);

        Assert.Equal(ProhibitedCategory.Minor, blocked);
        Assert.Single(sink.LogWrites);
        Assert.Equal("input", sink.LogWrites[0].Source);
        Assert.StartsWith(provider + ":", sink.LogWrites[0].ModelHint);
        Assert.Single(sink.CounterHits);
        Assert.Equal("input:" + provider, sink.CounterHits[0].Source);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void BlockedInput_OnABackgroundReaction_IsLoggedButNeverEscalated(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));
        guard.InputResult = ModerationResult.Block(ProhibitedCategory.Minor, "test");

        var blocked = moderation.CheckInput("awareness frame", escalate: false);

        Assert.NotNull(blocked);
        Assert.Single(sink.LogWrites);      // compliance record still written
        Assert.Empty(sink.CounterHits);     // but the user is not pushed toward the cooldown
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ProfessionalAdvice_IsSoft_LoggedButAllowed(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));
        guard.InputResult = ModerationResult.SoftHit(ProhibitedCategory.ProfessionalAdvice, "test");

        var blocked = moderation.CheckInput("is this rash normal", escalate: true);

        Assert.Null(blocked);                                   // allowed through
        Assert.Single(sink.LogWrites);                          // still on the record
        Assert.Equal(ProhibitedCategory.ProfessionalAdvice, sink.LogWrites[0].Category);
        Assert.Empty(sink.CounterHits);                         // soft hits never escalate
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CleanInput_WritesNothing(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));

        Assert.Null(moderation.CheckInput("hi bambi", escalate: true));
        Assert.Equal("hi bambi", Assert.Single(guard.InputsSeen));
        Assert.Empty(sink.LogWrites);
        Assert.Empty(sink.CounterHits);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void NullInput_IsCheckedAsEmpty_NotSkipped(string provider)
    {
        var (moderation, guard, _) = Wire(ForProvider(provider));

        moderation.CheckInput(null, escalate: false);

        Assert.Equal(string.Empty, Assert.Single(guard.InputsSeen));
    }

    // ---------- output ----------

    [Theory]
    [MemberData(nameof(Providers))]
    public void BlockedOutput_IsRefused_Logged_AndNeverEscalates(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));
        guard.OutputResult = ModerationResult.Block(ProhibitedCategory.Minor, "test");

        var blocked = moderation.CheckOutput("prohibited model reply");

        Assert.Equal(ProhibitedCategory.Minor, blocked);
        Assert.Equal("output", Assert.Single(sink.LogWrites).Source);
        // Model output tripping the filter is not the user's doing.
        Assert.Empty(sink.CounterHits);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void SoftOutputHit_IsLoggedAndShown(string provider)
    {
        var (moderation, guard, sink) = Wire(ForProvider(provider));
        guard.OutputResult = ModerationResult.SoftHit(ProhibitedCategory.ProfessionalAdvice, "test");

        Assert.Null(moderation.CheckOutput("take two aspirin~"));
        Assert.Equal(ProhibitedCategory.ProfessionalAdvice, Assert.Single(sink.LogWrites).Category);
        Assert.Empty(sink.CounterHits);
    }

    // ---------- degenerate host ----------

    [Theory]
    [MemberData(nameof(Providers))]
    public void NoGuardConfigured_AllowsEverythingWithoutThrowing(string provider)
    {
        // Headless hosts (and a startup ordering bug) leave App.ModerationGuard null. The spine must
        // degrade to "allow", never to a NullReferenceException inside a chat turn.
        var moderation = ForProvider(provider);
        moderation.GuardOverride = () => null;

        Assert.Null(moderation.CheckInput("anything", escalate: true));
        Assert.Null(moderation.CheckOutput("anything"));
    }

    [Fact]
    public void ProvidersReportDistinctCounterSources()
    {
        // moderation.log and the counter must be able to say WHICH transport produced a hit.
        Assert.Equal("input:local", LocalAiService.CreateModeration().InputCounterSource);
        Assert.Equal("input:openai_compat", OpenAiCompatibleService.CreateModeration().InputCounterSource);
    }
}
