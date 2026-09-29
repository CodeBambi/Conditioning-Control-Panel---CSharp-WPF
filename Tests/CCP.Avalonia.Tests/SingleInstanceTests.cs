using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests
{
    /// <summary>WPF App.xaml.cs:1819-1899: a second launch asks the primary to show, waits for the ack, exits.</summary>
    public class SingleInstanceTests
    {
        [Fact]
        public void SecondLaunchRaisesThePrimaryAndExits()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            int shown = 0;
            using var primary = SingleInstance.Claim(suffix, () => { Interlocked.Increment(ref shown); return Task.CompletedTask; });
            Assert.NotNull(primary);

            // A mutex is thread-owned, so the second launch claims from another thread, as another process would.
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, () => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));

            Assert.Null(second);
            Assert.Equal(1, Volatile.Read(ref shown));
        }
    }
}
