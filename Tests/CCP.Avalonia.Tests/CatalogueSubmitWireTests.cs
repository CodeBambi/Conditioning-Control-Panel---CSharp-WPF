using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Deeper submit path on Core CatalogueClient (App.Catalogue) against a fake handler: no network.</summary>
public sealed class CatalogueSubmitWireTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Paths.Add(r.RequestUri!.AbsolutePath);
            return Task.FromResult(r.RequestUri.AbsolutePath == "/api/auth/token-exchange"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $"{{\"access_token\":\"sb\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}") }
                : new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"e1\",\"status\":\"pending\"}") });
        }
    }

    private static async Task<(SubmissionResult Result, Fake Fake, string Key)> SubmitAs(string? token)
    {
        var fake = new Fake();
        var previous = App.Catalogue;
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ccpenh.json");
        File.WriteAllText(file, "{\"title\":\"t\"}");
        try
        {
            App.Catalogue = new CatalogueClient(() => token, () => token == null ? null : "uid-1", "9.9.9", fake);
            return (await MainShellWindow.SubmitDeeperEntryAsync(file), fake, MainShellWindow.CanonicalCataloguePathKey(file));
        }
        finally { App.Catalogue = previous; File.Delete(file); }
    }

    [Fact]
    public async Task SignedInSubmitPostsRecordsAndToastsSuccess()
    {
        var (result, fake, key) = await SubmitAs("ccp-tok");

        Assert.IsType<SubmissionResult.Success>(result);
        Assert.Equal(new[] { "/api/auth/token-exchange", "/api/enhancements" }, fake.Paths);
        Assert.Equal("e1", CoreSettings.Current.DeeperSubmissions[key].CatalogueId);
        Assert.Equal((Loc.Get("catalogue_toast_success"), NotificationType.Success, 6),
            MainShellWindow.DescribeSubmissionResult(result));
    }

    [Fact]
    public async Task SignedOutSubmitSendsNothingAndToastsAuthFailed()
    {
        var (result, fake, key) = await SubmitAs(null);

        Assert.IsType<SubmissionResult.AuthFailed>(result);
        Assert.Empty(fake.Paths);
        Assert.False(CoreSettings.Current.DeeperSubmissions.ContainsKey(key));
        Assert.Equal((Loc.Get("catalogue_toast_auth_failed"), NotificationType.Warning, 10),
            MainShellWindow.DescribeSubmissionResult(result));
    }
}
