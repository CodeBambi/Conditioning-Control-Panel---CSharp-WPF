using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class DailyFreeSandboxTests
{
    /// <summary>A no-fetch service returns before the attempt gate is stamped, so it never builds a request.</summary>
    [Fact]
    public async Task NoFetchServiceNeverAttemptsTheOverride()
    {
        var svc = new DailyFreeService(fetchOverride: false);
        await svc.RefreshAsync();
        var attempted = (string?)typeof(DailyFreeService)
            .GetField("_lastAttemptForDate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(svc);
        Assert.Null(attempted);
    }
}
