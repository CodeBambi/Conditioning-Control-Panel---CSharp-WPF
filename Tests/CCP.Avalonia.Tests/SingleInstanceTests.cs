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
            using var primary = SingleInstance.Claim(suffix, _ => { Interlocked.Increment(ref shown); return Task.CompletedTask; });
            Assert.NotNull(primary);

            // A mutex is thread-owned, so the second launch claims from another thread, as another process would.
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));

            Assert.Null(second);
            Assert.Equal(1, Volatile.Read(ref shown));
        }

        [Fact]
        public void AFailedShowStillAcksSoTheSecondLaunchExits()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            using var primary = SingleInstance.Claim(suffix, _ => throw new InvalidOperationException("show failed"));
            SingleInstance? second = null;
            var t = new Thread(() => second = SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Null(second);
        }
    
        [Fact]
        public void SecondLaunchHandsItsSurfaceToThePrimary()
        {
            var suffix = "_test_" + Guid.NewGuid().ToString("N")[..8];
            string? got = "unset";
            using var primary = SingleInstance.Claim(suffix, p => { got = p; return Task.CompletedTask; });
            var t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask, "game:race"));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Equal("game:race", got);

            t = new Thread(() => SingleInstance.Claim(suffix, _ => Task.CompletedTask));
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(20)));
            Assert.Null(got);                                   // a bare relaunch
        }
    }
}
