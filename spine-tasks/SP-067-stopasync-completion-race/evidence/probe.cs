// SP-067 Step 1 bounded-loop probe: drives HeartbeatParticipant start -> immediate stop
// N times against the product code as-built, records the tick loop's terminal outcome
// per iteration. Bounded: fixed iteration count, per-iteration await bounded by a
// hard cap so a wedged loop cannot spin forever. Run: dotnet run probe.cs [N]
#:project ../../../client/src/CcpClient.Desktop/CcpClient.Desktop.csproj

using CcpClient.Desktop.Lifecycle;

var iterations = args.Length > 0 ? int.Parse(args[0]) : 500;
var interval = TimeSpan.FromMilliseconds(1);

var completedTicks = new List<int>();
var cancelledTicks = new List<int>();
var other = 0;
var timeouts = 0;

for (var i = 0; i < iterations; i++)
{
    var registry = new OperationRegistry();
    var boundary = new UiDispatchBoundary();
    var heartbeat = new HeartbeatParticipant(registry.OwnerFor("Heartbeat"), boundary, interval);

    await heartbeat.StartAsync(CancellationToken.None);
    await heartbeat.StopAsync(); // immediate: exercises the zero-tick exit (framing e)

    var completion = heartbeat.Completion!;
    var winner = await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(10)));
    if (winner != completion)
    {
        timeouts++;
        continue;
    }

    var outcome = await completion;
    switch (outcome)
    {
        case OperationOutcome.Completed:
            completedTicks.Add(heartbeat.TickCount);
            break;
        case OperationOutcome.Cancelled:
            cancelledTicks.Add(heartbeat.TickCount);
            break;
        default:
            other++;
            break;
    }
}

static string Tally(List<int> ticks)
{
    if (ticks.Count == 0)
    {
        return "none";
    }

    var zero = ticks.Count(t => t == 0);
    return $"count={ticks.Count} zeroTick={zero} ticked={ticks.Count - zero} minTicks={ticks.Min()} maxTicks={ticks.Max()}";
}

Console.WriteLine($"iterations={iterations}");
Console.WriteLine($"Completed: {Tally(completedTicks)}");
Console.WriteLine($"Cancelled: {Tally(cancelledTicks)}");
Console.WriteLine($"other={other} timeouts={timeouts}");
