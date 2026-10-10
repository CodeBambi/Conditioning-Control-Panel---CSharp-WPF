using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The owner rules of EMI's moment bus that the port has to keep (LINES-SCHEMA 5): the 45 s floor,
/// a wordless hold face never stamping it, the panic hold's silence tail, and the bark mirror that
/// feeds her the app-wide funnel. Every engine here is an in-memory <c>FromJson</c> instance, so
/// nothing reads or writes the user's emi-desk.json.
/// </summary>
public class EmiMomentBusTests
{
    private const string File =
        "{\"version\":1,\"moments\":{" +
        "\"talk\":{\"pools\":[\"p\"],\"odds\":1.0,\"cooldownMs\":0,\"priority\":2,\"spiceCeiling\":2}," +
        "\"face\":{\"pools\":[\"h\"],\"odds\":0.0,\"priority\":3,\"hold\":true,\"holdMs\":1}," +
        "\"panicPressed\":{\"pools\":[\"h\"],\"odds\":0.0,\"priority\":3,\"hold\":true,\"tailMs\":300000}," +
        "\"check\":{\"pools\":[\"h\"],\"odds\":0.0,\"priority\":3,\"hold\":true,\"holdUntilReleased\":true}," +
        "\"ceremony\":{\"pools\":[\"p\"],\"odds\":1.0,\"cooldownMs\":0,\"priority\":3,\"spiceCeiling\":2}" +
        "},\"pools\":{" +
        "\"p\":[{\"id\":\"p1\",\"t\":\"one\",\"face\":\"^_^\",\"spice\":0},{\"id\":\"p2\",\"t\":\"two\",\"face\":\"^_^\",\"spice\":0}," +
        "{\"id\":\"p3\",\"t\":\"three\",\"face\":\"^_^\",\"spice\":0}]," +
        "\"h\":[{\"id\":\"h1\",\"t\":\"\",\"face\":\"-_-\",\"spice\":0}]" +
        "},\"asks\":[]}";

    [Fact]
    public void TheFloorIsFortyFiveSeconds()
    {
        Assert.Equal(45_000, EmiLineEngine.GlobalFloorMs);
        var engine = EmiLineEngine.FromJson(File);

        var first = engine.Draw("talk");
        Assert.NotNull(first);
        Assert.False(first!.Hold);
        engine.Ack(first.Id);

        Assert.Null(engine.Draw("talk"));          // inside the floor: silent
        Assert.NotNull(engine.Draw("ceremony"));   // priority 3 bypasses the floor and nothing else
    }

    [Fact]
    public void AWordlessHoldFaceNeverStampsTheFloor()
    {
        var engine = EmiLineEngine.FromJson(File);

        var hold = engine.Draw("face");
        Assert.NotNull(hold);
        Assert.True(hold!.Hold);
        Assert.Equal(string.Empty, hold.Text);
        engine.Ack(hold.Id, spoke: false);

        System.Threading.Thread.Sleep(30);         // the 1 ms hold itself has run out
        Assert.NotNull(engine.Draw("talk"));       // she may speak at once: the face cost her nothing
    }

    [Fact]
    public void PanicSilencesHerForFiveMinutes_CeremoniesIncluded()
    {
        var engine = EmiLineEngine.FromJson(File);
        Assert.True(engine.IsHoldMoment("panicPressed"));

        engine.Draw("panicPressed");
        Assert.True(engine.HoldActive);
        Assert.Null(engine.Draw("talk"));
        Assert.Null(engine.Draw("ceremony"));
    }

    [Fact]
    public void AHeldMomentSilencesHerUntilItIsReleased()
    {
        var engine = EmiLineEngine.FromJson(File);

        engine.Draw("check");
        Assert.Equal("check", engine.HeldBy);
        Assert.Null(engine.Draw("talk"));

        engine.ReleaseHold("check");
        Assert.False(engine.HoldActive);
        Assert.NotNull(engine.Draw("talk"));
    }

    [Fact]
    public void TheBusIsSilentWithNoDesk_AndTheBarkMirrorFeedsIt()
    {
        var (prevSink, prevRelease) = (EmiDeskBus.Sink, EmiDeskBus.ReleaseSink);
        try
        {
            EmiDeskBus.Sink = null;
            EmiDeskBus.Fire("levelUp");                            // no desk: a no-op, never a throw
            EmiBarkBridge.Mirror("LevelUp", c => c.Set("level", 7d));

            var heard = new List<(string Id, object? Ctx)>();
            EmiDeskBus.Sink = (id, ctx) => heard.Add((id, ctx));

            EmiBarkBridge.Mirror("LevelUp", c => c.Set("level", 7d));
            EmiBarkBridge.Mirror("BubblePopped", null);            // high frequency: deliberately unmapped
            EmiBarkBridge.Mirror("IdleStateChanged", c => c.Set("idle", false));   // only the going-idle edge
            EmiBarkBridge.Mirror("Panic", null);

            Assert.Equal(new[] { "levelUp", "panicPressed" }, heard.ConvertAll(h => h.Id));
            Assert.Equal(7, EmiLineEngine.ToCtx(heard[0].Ctx)["level"]);

            EmiDeskBus.Sink = (_, _) => throw new InvalidOperationException("a throwing desk");
            EmiDeskBus.Fire("levelUp");                            // must never break the caller
        }
        finally { (EmiDeskBus.Sink, EmiDeskBus.ReleaseSink) = (prevSink, prevRelease); }
    }
}
