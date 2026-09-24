using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class CompanionMaintenanceTests
{
    private sealed class Rig : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "ccp-maintenance-" + Guid.NewGuid());
        public readonly MemoryStore Memory;
        public readonly CompanionMemoryMaintenance Worker;
        public bool Enabled = true;
        public string Context = "account:avatar";
        public int Calls;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Wait;
        public bool Invalid;
        public Rig()
        {
            Directory.CreateDirectory(DirectoryPath);
            Memory = new MemoryStore(Path.Combine(DirectoryPath, "memory.json"), chatMemoryEnabled: () => Enabled);
            Worker = new CompanionMemoryMaintenance(Memory, Send, () => Enabled, () => Context, TimeSpan.FromMilliseconds(2));
        }
        private async Task<AiReplyResult> Send(IReadOnlyList<ChatMessage> messages, AiCallOptions options, CancellationToken token)
        {
            Calls++;
            Assert.Equal(AiPurpose.Summary, options.Purpose);
            Assert.Equal(300, options.MaxTokens);
            Assert.False(options.Interactive);
            Assert.True(options.IsStructuredUtility);
            var sources = JsonSerializer.Deserialize<Dictionary<string, string>>(messages.Last().Content)!;
            Entered.TrySetResult();
            if (Wait) await Release.Task; // Simulate a transport which ignores cancellation.
            var source = sources.Last();
            return new AiReplyResult(Invalid ? "{broken" : JsonSerializer.Serialize(new { context = new[] {
                new { id = source.Key, quote = source.Value } } }), true, null);
        }
        public CompanionTurn Accept(string text = "I am building a small garden")
        {
            var turn = CompanionTurn.Create(TurnKind.UserChat, text);
            Worker.Accept(turn);
            return turn;
        }
        public async Task CompleteBatch()
        {
            for (int i = 0; i < 8; i++) Accept();
            await Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        public async Task Drain()
        {
            Release.TrySetResult();
            await Worker.PendingJob.WaitAsync(TimeSpan.FromSeconds(3));
        }
        public void Dispose()
        {
            Worker.Dispose(); Memory.Dispose();
            Directory.Delete(DirectoryPath, true);
        }
    }

    [Theory]
    [InlineData("call me River", "preferred-name")]
    [InlineData("remember my favorite tea is jasmine", "favorite:tea")]
    [InlineData("actually, remember my favorite tea is mint", "favorite:tea")]
    [InlineData("remember my goal is finishing the garden", "current-goal")]
    public void ExplicitFacts_AreNarrowAndCorrectionKeysAreStable(string text, string key) =>
        Assert.Equal(key, ExplicitMemoryRules.Parse(text)?.Key);

    [Theory]
    [InlineData("I like tea")]
    [InlineData("pretend my name is River")]
    [InlineData("remember my password is maple")]
    [InlineData("remember my diagnosis is example")]
    [InlineData("call me tomorrow please")]
    public void AmbiguousOrSensitiveStatements_DoNotBecomeFacts(string text) => Assert.Null(ExplicitMemoryRules.Parse(text));

    [Fact]
    public void ExplicitCorrectionReplacesAndRetractionRemoves_WithoutRescanningDeletedSources()
    {
        using var rig = new Rig();
        rig.Accept("remember my favorite tea is jasmine");
        var newer = rig.Accept("actually, remember my favorite tea is mint");
        Assert.Contains("mint", Assert.Single(rig.Memory.GetFacts()).Text);
        Assert.Equal(newer.Id, Assert.Single(rig.Memory.GetFacts()).SourceTurnId);
        rig.Accept("forget my favorite tea");
        Assert.Empty(rig.Memory.GetFacts());
        rig.Worker.Accept(newer);
        Assert.Empty(rig.Memory.GetFacts());
    }

    [Fact]
    public void PreferredName_ClearStaysClearAndNoDuplicateFactSurvives()
    {
        using var rig = new Rig();
        var source = rig.Accept("call me River");
        Assert.Equal("River", rig.Memory.Profile[MemoryStore.KeyPreferredName]);
        rig.Memory.UpdateProfileSignal(MemoryStore.KeyPreferredName, null);
        rig.Worker.Accept(source);
        Assert.False(rig.Memory.Profile.ContainsKey(MemoryStore.KeyPreferredName));
        Assert.Empty(rig.Memory.GetFacts());
    }

    [Fact]
    public async Task EightAcceptedExchanges_CreateOneBoundedGroundedSummary()
    {
        using var rig = new Rig();
        for (int i = 0; i < 7; i++) rig.Accept();
        Assert.Equal(0, rig.Calls);
        rig.Accept();
        await rig.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (int i = 0; i < 100 && rig.Worker.GetContext() == null; i++) await Task.Delay(5);
        Assert.Contains("small garden", rig.Worker.GetContext());
        Assert.True(rig.Worker.GetContext()!.Length <= 960);
        Assert.Equal(1, rig.Calls);
        await rig.Drain();
    }

    [Theory]
    [InlineData("forget")]
    [InlineData("edit")]
    [InlineData("persona")]
    [InlineData("account")]
    [InlineData("off")]
    public async Task InFlightSummaryCannotRestoreInvalidatedContext(string invalidation)
    {
        using var rig = new Rig { Wait = true };
        var fact = rig.Memory.AddFact("A remembered preference", MemoryFactKind.Preference);
        await rig.CompleteBatch();
        switch (invalidation)
        {
            case "forget": rig.Worker.Forget(); break;
            case "edit": rig.Memory.UpdateFact(fact.Id, "A corrected preference"); break;
            case "persona": rig.Context = "account:new-avatar"; break;
            case "account": rig.Context = "other-account:avatar"; break;
            case "off": rig.Enabled = false; break;
        }
        await rig.Drain();
        Assert.Null(rig.Worker.GetContext());
    }

    [Fact]
    public async Task NewChatInterruptsUtilityWithoutPublishingLateResult()
    {
        using var rig = new Rig { Wait = true };
        await rig.CompleteBatch();
        var interrupted = rig.Worker.InterruptAsync();
        rig.Release.TrySetResult();
        await interrupted.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(rig.Worker.GetContext());
    }

    [Fact]
    public async Task DeletedFactSourceIsNotRelearnedAfterRestart()
    {
        using var rig = new Rig();
        var source = rig.Accept("remember my favorite tea is jasmine");
        rig.Memory.ForgetFact(Assert.Single(rig.Memory.GetFacts()).Id);
        for (int i = 0; i < 8; i++) rig.Accept();
        await rig.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await rig.Drain();
        using var next = new CompanionMemoryMaintenance(rig.Memory, (_, _, _) => throw new Exception("no call"),
            () => true, () => rig.Context);
        next.Accept(source);
        Assert.Empty(rig.Memory.GetFacts());
        Assert.DoesNotContain("jasmine", next.GetContext() ?? "");
    }

    [Fact]
    public async Task MalformedSummaryIsDiscardedWithoutRetry()
    {
        using var rig = new Rig { Invalid = true };
        await rig.CompleteBatch();
        await rig.Drain();
        Assert.Null(rig.Worker.GetContext());
        Assert.Equal(1, rig.Calls);
    }

    [Fact]
    public void MemoryOffAndAmbientDoNoMaintenance()
    {
        using var rig = new Rig { Enabled = false };
        for (int i = 0; i < 10; i++) rig.Accept("remember my favorite tea is mint");
        rig.Enabled = true;
        for (int i = 0; i < 10; i++) rig.Worker.Accept(CompanionTurn.Create(TurnKind.AmbientEvent, "hello"));
        Assert.Equal(0, rig.Calls);
        Assert.Empty(rig.Memory.GetFacts());
    }

    [Theory]
    [InlineData("{\"context\":[{\"id\":\"s1\",\"quote\":\"invented preference\"}]}")]
    [InlineData("{\"context\":[{\"id\":\"unknown\",\"quote\":\"a garden\"}]}")]
    [InlineData("```json {} ```")]
    public void StructuredSummaryRequiresExactKnownSource(string json) => Assert.Null(
        ExplicitMemoryRules.ParseSummary(json, new Dictionary<string, string> { ["s1"] = "I am building a garden" }));
}
