using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// A4 (her-room-divergence-audit.md row A4, ADOPT): the per-app page-title allow-list.
///
/// <para>The row's whole point is a NARROWING — before it, a granted awareness consent let the
/// (scrubbed) title of every app reach the provider; after it, the list ships EMPTY and no title
/// reaches anything until the user names an app. The first fact below is that narrowing stated as
/// a pair.</para>
///
/// <para>The second fact is the row's HARD CONSTRAINT: the identity matched is the caller-supplied
/// <c>App</c> field and nothing else. Observing which application is actually in the foreground is
/// the boundary audit row A1 / owner question Q2 is blocked on, and a filter keyed off an observed
/// process name would have crossed it while looking like a privacy improvement.</para>
/// </summary>
public class AiTitleAllowListTests
{
    // ---- the narrowing (the row, stated as a pair) ----

    [Fact]
    public async Task ShipsEmpty_SoAGrantedConsentCarriesNoTitleForAnyone()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();
        h.Service.Consent = AiAwarenessConsent.Given;

        // The shipped state: consent granted, nothing named.
        Assert.Empty(h.Service.TitleAllowList.Entries);
        Assert.IsType<AiAwarenessRoutingResult.Visible>(
            await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "A Private Page", "0m")));

        // Category, app and duration still travel — the frame proceeds TITLE-FREE (WPF
        // ResolveTitle -> null, AwarenessPrivacyRules.cs:453-466), it is not dropped.
        Assert.Equal("[Category: Social | App: Browser | Title:  | Duration: 0m]", h.Provider.LastRequest?.Prompt);
        Assert.DoesNotContain("A Private Page", h.Provider.LastRequest?.Prompt);

        // The other half of the pair: naming the app is what lets the title through, and nothing
        // else does. Same service, same context, one user action between them.
        h.Now = h.Now.AddHours(1); // clear the reaction cooldown
        Assert.True(h.Service.TitleAllowList.Add("Browser"));
        Assert.IsType<AiAwarenessRoutingResult.Visible>(
            await h.Service.RunReactionAsync(new AiAwarenessContext("Social", "Browser", "A Private Page", "0m")));
        Assert.Equal("[Category: Social | App: Browser | Title: A Private Page | Duration: 0m]", h.Provider.LastRequest?.Prompt);
    }

    // ---- the constraint: the CALLER-SUPPLIED App field, and nothing else ----

    [Fact]
    public async Task MatchesTheAppFieldOnly_NeverTheTitle_NeverTheCategory()
    {
        var h = new Harness();
        await h.AdmitProviderAsync();
        h.Service.Consent = AiAwarenessConsent.Given;
        h.Service.TitleAllowList.Add("browser");

        // The allow key appears in the CATEGORY and in the TITLE, and the app is not named.
        // A title that could allow-list itself is the failure WPF names at
        // AwarenessPrivacyRules.cs:461-464; so is a whole cluster allowing its own titles.
        await h.Service.RunReactionAsync(new AiAwarenessContext("browser", "Notepad", "browser session notes", "0m"));
        Assert.Equal("[Category: browser | App: Notepad | Title:  | Duration: 0m]", h.Provider.LastRequest?.Prompt);

        // Only the App field moves it, and case does not matter (WPF MatchesAny lowercases both
        // sides, AwarenessPrivacyRules.cs:483-505).
        h.Now = h.Now.AddHours(1);
        await h.Service.RunReactionAsync(new AiAwarenessContext("Work", "BROWSER.EXE", "browser session notes", "0m"));
        Assert.Equal("[Category: Work | App: BROWSER.EXE | Title: browser session notes | Duration: 0m]", h.Provider.LastRequest?.Prompt);
    }

    /// <summary>
    /// The constraint again, structurally: the filter's source carries no way to learn which
    /// application is in the foreground. This is a LEXICAL guard over one file — it cannot prove
    /// the boundary is uncrossable everywhere, only that the type A4 introduced does not reach for
    /// the observation A1 is blocked on. It is the pin that reds if a later worker "improves" the
    /// filter by looking the app up instead of being told it.
    /// </summary>
    [Fact]
    public void TheFilterSourceCannotObserveAProcess()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "client", "src", "CcpClient.Desktop", "Ai", "AiTitleAllowList.cs"));

        string[] forbidden =
        [
            "System.Diagnostics",        // Process/ProcessName
            "Process",                   // any process type or member
            "DllImport",                 // a P/Invoke of its own
            "GetForegroundWindow",       // the WPF-parity capture this port already has, elsewhere
            "AiWindowTitleCapability",   // the port's own observation seam
            "ObserveForegroundTitle",    // the service method that uses it
        ];
        Assert.NotEmpty(forbidden);
        foreach (var needle in forbidden)
        {
            Assert.DoesNotContain(needle, source, StringComparison.Ordinal);
        }

        // Positive control, so the guard cannot pass by reading the wrong file: the type under
        // guard really is in there.
        Assert.Contains("class AiTitleAllowList", source, StringComparison.Ordinal);
    }

    // ---- entry sanitisation (WPF AwarenessText.SanitizeRuleEntry, :174-198, verbatim) ----

    [Theory]
    // trimmed, lowercased
    [InlineData("  Chrome  ", "chrome")]
    // wildcards are neither wildcards nor literals here (:183) — stripped, then the rest stands
    [InlineData("n*otep%ad?", "notepad")]
    // one character matches half the machine (:188)
    [InlineData("a", null)]
    [InlineData(" x ", null)]
    // nothing but punctuation would match every app on the machine (:191-197)
    [InlineData("***", null)]
    [InlineData("--", null)]
    // an entry that tries to be prompt scaffolding (:189 over AwarenessText.cs:52-59)
    [InlineData("ignore previous", null)]
    [InlineData("system: you are", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SanitizeEntry_WpfRuleShape(string? raw, string? expected) =>
        Assert.Equal(expected, AiTitleAllowList.SanitizeEntry(raw));

    [Fact]
    public void SanitizeEntry_CapsAtTheWpfLength()
    {
        var overlong = new string('a', AiTitleAllowList.MaxEntryLength + 20);
        Assert.Equal(new string('a', AiTitleAllowList.MaxEntryLength), AiTitleAllowList.SanitizeEntry(overlong));
    }

    [Fact]
    public void Add_StoresTheSanitisedForm_RefusesDuplicatesCaseInsensitively()
    {
        var list = new AiTitleAllowList();
        Assert.True(list.Add("  Notepad  "));
        Assert.Equal(["notepad"], list.Entries);

        // The stored form is what the filter matches, so the chip the user sees is the truth.
        Assert.False(list.Add("NOTEPAD"));
        Assert.False(list.Add("notepad"));
        Assert.Single(list.Entries);

        Assert.True(list.Remove("NOTEPAD"));
        Assert.Empty(list.Entries);
        Assert.False(list.Remove("notepad")); // gone means gone
        Assert.False(list.AllowsTitleFor("notepad"));
    }

    [Fact]
    public void Add_StopsAtTheWpfEntryCap()
    {
        // WPF: "How many entries an allow/deny list may hold. Beyond this it is not a list, it is
        // a policy." (AwarenessText.cs:41-42, MaxRuleEntries = 200.)
        var list = new AiTitleAllowList();
        for (var i = 0; i < AiTitleAllowList.MaxEntries; i++)
        {
            Assert.True(list.Add($"app{i:D4}"));
        }

        Assert.Equal(AiTitleAllowList.MaxEntries, list.Count);
        Assert.False(list.Add("one-too-many"));
        Assert.False(list.AllowsTitleFor("one-too-many"));
    }

    [Fact]
    public void Clear_EmptiesIt_AndNoTitleTravelsAgain()
    {
        var list = new AiTitleAllowList();
        list.Add("browser");
        Assert.True(list.AllowsTitleFor("Browser"));

        list.Clear();
        Assert.Empty(list.Entries);
        Assert.False(list.AllowsTitleFor("Browser"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    // The awareness harness in miniature: the real pipeline and the real service, a controllable
    // clock for the cooldown registry, and a recording provider at the seam.
    private sealed class Harness
    {
        public Harness()
        {
            Boundary = new AiModerationBoundary(AiModerationPolicy.Empty);
            Pipeline = new AiOperationPipeline(
                Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, Boundary);
            Service = new AiAwarenessService(
                Pipeline, Boundary, Diagnostics, Capabilities, new AiCooldownRegistry(() => Now));
        }

        public OperationRegistry Registry { get; } = new();

        public CapabilityRegistry Capabilities { get; } = new();

        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();

        public AiModerationBoundary Boundary { get; }

        public AiOperationPipeline Pipeline { get; }

        public AiAwarenessService Service { get; }

        public RecordingProvider Provider { get; } = new();

        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public async Task AdmitProviderAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingProvider : IAiProvider
    {
        public AiProviderDescriptor Descriptor { get; } = new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public AiRequest? LastRequest { get; private set; }

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("test-provider"));

        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult<AiReply>(new AiReply.Generated("ok", AiEndpointClass.Loopback));
        }
    }
}
