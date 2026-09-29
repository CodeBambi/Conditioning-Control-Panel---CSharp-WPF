using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class SyncPushCancellationTests
{
    /// <summary>WPF's rule: shutdown cancellations never back off; an HttpClient timeout (cancellation wrapping
    /// TimeoutException) and real failures do.</summary>
    [Fact]
    public void OnlyShutdownCancellationsSkipTheBackoff()
    {
        Assert.True(SyncPush.IsExpectedCancellation(new TaskCanceledException()));
        Assert.True(SyncPush.IsExpectedCancellation(new ObjectDisposedException("handler")));
        Assert.True(SyncPush.IsExpectedCancellation(new Exception("wrapped", new OperationCanceledException())));
        Assert.False(SyncPush.IsExpectedCancellation(new System.Net.Http.HttpRequestException("refused")));
        var timeout = new TaskCanceledException("timeout", new TimeoutException());
        Assert.True(SyncPush.IsExpectedCancellation(timeout) && timeout.InnerException is TimeoutException);
    }
}
