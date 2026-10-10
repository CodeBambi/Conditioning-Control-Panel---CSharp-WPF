using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Sign-in recovery (WPF ProfileSyncService.HandleUnauthorizedAsync + Services/Account/MergedAccountRecovery):
/// a 401 asks restore-session, then a provider, each on its own cooldown, and never clears the token; a
/// 409 "merged" adopts the canonical id once and re-signs in. The auth token lives in CoreSecrets (process-wide),
/// so the class runs in that collection with an in-memory store.
/// </summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class AuthRecoveryTests : IDisposable
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Answers = new();
        public readonly List<(string Path, string? Token)> Calls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.AbsolutePath, request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null));
            return Task.FromResult(Answers.Dequeue()());
        }
    }

    private static Func<HttpResponseMessage> Json(HttpStatusCode code, string json) =>
        () => new HttpResponseMessage(code) { Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json") };

    private sealed class Holder { public AppSettings Current = new(); }
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly string? _accountId = CoreAccount.UnifiedUserId;
    private readonly Holder _service = new();
    private readonly Fake _http = new();
    private DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public AuthRecoveryTests()
    {
        CoreSecrets.RetrieveProvider = name => _secrets.TryGetValue(name, out var v) ? v : null;
        CoreSecrets.StoreProvider = (name, value) => _secrets[name] = value;
        _service.Current.UnifiedId = "u_tombstone1";
        _service.Current.AuthToken = "tok";
        AuthRecovery.ResetForTest();
        AuthRecovery.SettingsForTest = () => _service.Current;
        AuthRecovery.HandlerForTest = _http;
        AuthRecovery.Now = () => _now;
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
        MergedAccountRecovery.ResetForTest();
        MergedAccountRecovery.SettingsForTest = () => _service.Current;
        MergedAccountRecovery.StopSync = null;
        MergedAccountRecovery.StampQuests = null;
        MergedAccountRecovery.Reauthenticate = null;
        MergedAccountRecovery.ReloadProfile = null;
        MergedAccountRecovery.Finished = null;
    }

    public void Dispose()
    {
        AuthRecovery.ResetForTest();
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
        MergedAccountRecovery.ResetForTest();
        MergedAccountRecovery.StopSync = null;
        MergedAccountRecovery.StampQuests = null;
        MergedAccountRecovery.Reauthenticate = null;
        MergedAccountRecovery.ReloadProfile = null;
        MergedAccountRecovery.Finished = null;
        CoreAccount.UnifiedUserId = _accountId;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{}") };

    [Fact]
    public async Task AnythingButA401IsNotRecovered()
    {
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Forbidden)));
        Assert.Empty(_http.Calls);
    }

    [Fact]
    public async Task RestoreSessionConfirmsTheTokenAndAdoptsARotatedOne()
    {
        int recovered = 0;
        AuthRecovery.Recovered = () => recovered++;
        _http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'auth_token':'fresh'}"));

        Assert.True(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal(("/v2/auth/restore-session", "tok"), _http.Calls.Single());
        Assert.Equal("fresh", _service.Current.AuthToken);
        Assert.Equal(1, recovered);
    }

    [Fact]
    public async Task AFailedRecoveryKeepsTheTokenAndWaitsOutItsCooldowns()
    {
        int providerAsks = 0;
        AuthRecovery.ProviderRevalidate = () => { providerAsks++; return Task.FromResult(true); };   // validates, mints nothing
        _http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));

        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal("tok", _service.Current.AuthToken);          // never cleared
        Assert.Equal(1, providerAsks);

        // A burst of 401s inside the cooldowns asks nobody.
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Single(_http.Calls);
        Assert.Equal(1, providerAsks);

        // 31 s later restore-session may be asked again; the provider waits two minutes.
        _now = _now.AddSeconds(31);
        _http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal(2, _http.Calls.Count);
        Assert.Equal(1, providerAsks);
    }

    [Fact]
    public async Task AProviderThatMintsANewTokenRecovers()
    {
        AuthRecovery.ProviderRevalidate = () => { _service.Current.AuthToken = "minted"; return Task.FromResult(true); };
        _http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));
        Assert.True(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal("minted", _service.Current.AuthToken);
    }

    [Fact]
    public void OnlyA409MergedWithAValidCanonicalIsATombstone()
    {
        Assert.True(MergedAccountResponse.TryParse(409, "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}", out var id));
        Assert.Equal("u_canon123", id);
        Assert.False(MergedAccountResponse.TryParse(401, "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}", out _));
        Assert.False(MergedAccountResponse.TryParse(409, "{\"error\":\"conflict\",\"canonical_unified_id\":\"u_canon123\"}", out _));
        Assert.False(MergedAccountResponse.TryParse(409, "{\"error\":\"merged\",\"canonical_unified_id\":\"../etc\"}", out _));
        Assert.False(MergedAccountResponse.TryParse(409, "not json", out _));
        Assert.False(MergedAccountRecovery.TryHandle(500, "{}"));
    }

    [Fact]
    public async Task AMergedReplyAdoptsTheCanonicalOnceAndReSignsIn()
    {
        var steps = new List<string>();
        MergedAccountRecovery.StopSync = () => steps.Add("stop");
        MergedAccountRecovery.StampQuests = id => steps.Add("stamp " + id);
        MergedAccountRecovery.Reauthenticate = () =>
        {
            steps.Add("reauth token=" + (_service.Current.AuthToken ?? "none"));
            _service.Current.AuthToken = "canon-token";
            return Task.FromResult(true);
        };
        MergedAccountRecovery.ReloadProfile = () => { steps.Add("reload"); return Task.CompletedTask; };
        MergedAccountRecovery.Finished = (id, ok) => steps.Add($"finished {id} {ok}");
        const string body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}";

        Assert.True(MergedAccountRecovery.TryHandle(409, body));
        await MergedAccountRecovery.LastSwap;

        Assert.Equal("u_canon123", _service.Current.UnifiedId);
        Assert.Equal("u_canon123", CoreAccount.UnifiedUserId);
        Assert.Equal("canon-token", _service.Current.AuthToken);
        Assert.Equal(new[] { "stop", "stamp u_canon123", "reauth token=none", "reload", "finished u_canon123 True" }, steps);

        // Every other door answers the same 409 while the swap runs: handled, never swapped twice.
        Assert.True(MergedAccountRecovery.TryHandle(409, body));
        await MergedAccountRecovery.LastSwap;
        Assert.Equal(5, steps.Count);
    }

    [Fact]
    public async Task WithNoProviderTheSwapEndsSignedOutAndOffersTheSignIn()
    {
        (string Id, bool Reauthed)? finished = null;
        bool reloaded = false;
        MergedAccountRecovery.ReloadProfile = () => { reloaded = true; return Task.CompletedTask; };
        MergedAccountRecovery.Finished = (id, ok) => finished = (id, ok);

        Assert.True(MergedAccountRecovery.TryHandle(409, "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon456\"}"));
        await MergedAccountRecovery.LastSwap;

        Assert.Equal("u_canon456", _service.Current.UnifiedId);
        Assert.Null(_service.Current.AuthToken);
        Assert.False(reloaded);
        Assert.Equal(("u_canon456", false), finished);
    }
}

/// <summary>The purchase and the streak fix retry ONCE after a recovered 401, with the recovered token.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class UnauthorizedRetryTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();

    public UnauthorizedRetryTests()
    {
        CoreSecrets.RetrieveProvider = name => _secrets.TryGetValue(name, out var v) ? v : null;
        CoreSecrets.StoreProvider = (name, value) => _secrets[name] = value;
    }

    public void Dispose()
    {
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<Func<HttpResponseMessage>> Answers = new();
        public readonly List<string?> Tokens = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Tokens.Add(request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null);
            return Task.FromResult(Answers.Dequeue()());
        }
    }

    private static Func<HttpResponseMessage> Json(HttpStatusCode code, string json) =>
        () => new HttpResponseMessage(code) { Content = new StringContent(json.Replace('\'', '"'), Encoding.UTF8, "application/json") };

    private static readonly SkillDefinition Skill = SkillDefinition.All.First(s => !s.IsSecret && string.IsNullOrEmpty(s.PrerequisiteId));

    [Fact]
    public async Task ASkillPurchaseRetriesOnceWithTheRecoveredToken()
    {
        var s = new AppSettings { SkillPoints = Skill.Cost + 5, UnifiedId = "u_1", AuthToken = "dead", UnlockedSkills = new List<string>() };
        var http = new Fake();
        int asked = 0;
        var service = new SkillPurchase(() => s, http)
        {
            Save = () => { },
            Unauthorized = r => { asked++; s.AuthToken = "fresh"; return Task.FromResult(r.StatusCode == HttpStatusCode.Unauthorized); },
        };
        http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':true,'debited':true,'skill_points':5,'unlocked_skills':['" + Skill.Id + "']}"));

        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);

        Assert.True(ok, error);
        Assert.Equal(new[] { "dead", "fresh" }, http.Tokens);
        Assert.Equal(1, asked);
        Assert.Contains(Skill.Id, s.UnlockedSkills);
    }

    [Fact]
    public async Task AnUnrecovered401IsNotRetriedAndSaysTheSessionExpired()
    {
        var s = new AppSettings { SkillPoints = Skill.Cost + 5, UnifiedId = "u_1", AuthToken = "dead", UnlockedSkills = new List<string>() };
        var http = new Fake();
        var service = new SkillPurchase(() => s, http) { Save = () => { }, Unauthorized = _ => Task.FromResult(false) };
        http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{}"));

        var (ok, error) = await service.PurchaseSkillAsync(Skill.Id);

        Assert.False(ok);
        Assert.StartsWith("Your session has expired.", error);
        Assert.Single(http.Tokens);
        Assert.Equal(Skill.Cost + 5, s.SkillPoints);
    }

    [Fact]
    public async Task AStreakFixRetriesOnceWithTheRecoveredToken()
    {
        var s = new AppSettings { UnifiedId = "u_1", AuthToken = "dead" };
        var http = new Fake();
        var fix = new StreakFix(() => s, http)
        {
            Unauthorized = r => { s.AuthToken = "fresh"; return Task.FromResult(r.StatusCode == HttpStatusCode.Unauthorized); },
        };
        http.Answers.Enqueue(Json(HttpStatusCode.Unauthorized, "{'error':'Invalid or missing auth token'}"));
        http.Answers.Enqueue(Json(HttpStatusCode.OK, "{'success':true,'oopsie_credits':2}"));

        var (ok, error, credits) = await fix.UseAsync(new DateTime(2026, 10, 3));

        Assert.True(ok, error);
        Assert.Equal(2, credits);
        Assert.Equal(new[] { "dead", "fresh" }, http.Tokens);
    }
}
