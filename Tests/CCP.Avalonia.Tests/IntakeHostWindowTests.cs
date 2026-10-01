using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Avalonia intake host's page protocol: the weekly pass is spent by a completed
/// intake (a parsed quiz-result) and by nothing else (WPF IntakeHostService.OnQuizResult).</summary>
public sealed class IntakeHostWindowTests
{
    [Fact]
    public async Task OnlyACompletedIntakeSpendsThePass()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var prevSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var (week, utc) = (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc);
            var (loggedIn, lab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.HasLabAccessProvider = () => false;
            (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = ("", null);
            var dir = Directory.CreateTempSubdirectory("intake-host-").FullName;
            var host = new IntakeHostWindow { SessionsFolder = dir };
            try
            {
                Assert.True(AvApp.IntakePass.IsPassAvailable);

                // Walk-outs, aborts, crashes and garbage cost nothing.
                foreach (var msg in new[]
                {
                    "{\"type\":\"heartbeat\"}", "not json", "{\"type\":\"boot-error\",\"msg\":\"x\"}",
                    "{\"type\":\"quiz-result\",\"result\":\"garbled\"}", "{\"type\":\"exit\"}",
                    "{\"type\":\"intake-close\"}",
                })
                {
                    host.HandleMessage(msg);
                }
                Assert.True(AvApp.IntakePass.IsPassAvailable);
                Assert.Empty(Directory.GetFiles(dir));

                // The page's own carrier reaches the same handler.
                host.Web.OnWebMessage("{\"type\":\"quiz-result\",\"result\":{\"niche\":\"bambi\",\"totalScore\":95,\"maxScore\":100,\"peakDepth\":0.8}}");
                Assert.Equal(IntakePassService.CurrentWeekKey(), service.Current.IntakePassSpentWeek);
                Assert.False(AvApp.IntakePass.IsPassAvailable);
                Assert.Single(Directory.GetFiles(dir, "*.session.json"));
            }
            finally
            {
                host.Close();
                Directory.Delete(dir, true);
                CoreAccount.IsLoggedInProvider = loggedIn;
                CoreAccount.HasLabAccessProvider = lab;
                (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = (week, utc);
                service.SaveImmediate();
                CoreSettings.ServiceProvider = prevSettings;
            }
            return Task.CompletedTask;
        });
    }
}
