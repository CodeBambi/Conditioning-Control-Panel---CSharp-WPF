using System;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Tests;

public sealed class AvaloniaTestDispatcherTests
{
    [Fact]
    public async Task KeepsAsyncContinuationsOnDedicatedDispatcher()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var firstThread = 0;
        var continuationThread = 0;
        var apartment = ApartmentState.Unknown;

        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            firstThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
            await Task.Yield();
            continuationThread = Environment.CurrentManagedThreadId;
            Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        });

        Assert.NotEqual(callerThread, firstThread);
        Assert.Equal(firstThread, continuationThread);
        if (OperatingSystem.IsWindows())
            Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public async Task PropagatesActionExceptions()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AvaloniaTestDispatcher.RunAsync(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("dispatcher probe");
            }));

        Assert.Equal("dispatcher probe", failure.Message);
    }
}
