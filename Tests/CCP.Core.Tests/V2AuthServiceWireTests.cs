using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Core V2 client on the wire, through a fake handler. Expected requests are transcribed
/// VERBATIM from the pre-move WPF source (ConditioningControlPanel/Services/Account/V2AuthService.cs
/// at b30f8f53a): the same paths, JObject bodies (Newtonsoft indented, Environment.NewLine), the
/// static-ctor headers (X-Client-Version, User-Agent) and X-Auth-Token only where WPF's
/// AddAuthHeader ran. Nothing was run on Windows to record them.
/// </summary>
public sealed class V2AuthServiceWireTests : IDisposable
{
    private const string Url = "https://codebambi-proxy.vercel.app";
    private const string Ver = "6.10.3";
    private const string Tok = "SECRET-auth-token-123";
    private readonly Func<string?>? _oldVersion = CoreReleaseContent.AppVersionProvider;
    private readonly Fake _fake = new();
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly AppSettings _settings;

    public V2AuthServiceWireTests()
    {
        CoreReleaseContent.AppVersionProvider = () => Ver;
        // AuthToken is backed by CoreSecrets; an in-memory store stands in for the head's.
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings = new() { AuthToken = Tok, UnifiedId = "u_abc123" };
    }

    public void Dispose()
    {
        CoreReleaseContent.AppVersionProvider = _oldVersion;
        V2AuthService.MergedRecovery = null;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private V2AuthService Client() => new(() => _settings, _fake);

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"success\":true}";
        public Exception? Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var headers = r.Headers.Concat(r.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Where(h => h.Key != "Content-Length") // computed by HttpContent, not set by the client
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}").OrderBy(h => h, StringComparer.Ordinal);
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Seen.Add($"{r.Method} {r.RequestUri!.AbsoluteUri}\n{string.Join("\n", headers)}\n\n{body.Replace(Environment.NewLine, "\n")}");
            if (Throw != null) throw Throw;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    [Fact]
    public async Task EveryRequestMatchesTheWpfFixture()
    {
        var c = Client();
        await c.AuthenticateWithDiscordAsync("dtok", "Bambi");
        await c.AuthenticateWithPatreonAsync("ptok");
        await c.AuthenticateWithSubstarAsync("stok", "  ");
        await c.RegisterAsync("INV", "Bambi", "pw");
        await c.LoginAsync("Bambi", "pw");
        await c.LinkProviderAsync("u_abc123", "discord", "dtok");
        _fake.Body = "{\"user\":{\"unified_id\":\"u_abc123\",\"level\":7}}";
        var user = await c.GetUserProfileAsync("u a&b");
        _fake.Body = "{}";
        await c.UpdateUserProfileAsync("u_abc123", xp: 10, level: 2, achievements: new[] { "a1" });
        await c.SendHeartbeatAsync("u_abc123");
        await c.DeleteAccountAsync("u_abc123");
        await c.AuthorizeMobileLinkAsync();

        Assert.Equal(7, user!.Level);
        string[] expected =
        {
            Post("/v2/auth/discord", "{\n  \"access_token\": \"dtok\",\n  \"display_name\": \"Bambi\"\n}", false),
            Post("/v2/auth/patreon", "{\n  \"access_token\": \"ptok\"\n}", false),
            Post("/v2/auth/substar", "{\n  \"access_token\": \"stok\"\n}", false),
            Post("/v2/auth/register", "{\n  \"invite_code\": \"INV\",\n  \"display_name\": \"Bambi\",\n  \"password\": \"pw\"\n}", false),
            Post("/v2/auth/login", "{\n  \"display_name\": \"Bambi\",\n  \"password\": \"pw\"\n}", false),
            Post("/v2/auth/link", "{\n  \"unified_id\": \"u_abc123\",\n  \"provider\": \"discord\",\n  \"access_token\": \"dtok\"\n}", true),
            $"GET {Url}/v2/user/profile?unified_id=u%20a%26b\nUser-Agent: ConditioningControlPanel/{Ver}\nX-Auth-Token: {Tok}\nX-Client-Version: {Ver}\n\n",
            Post("/v2/user/update", "{\n  \"unified_id\": \"u_abc123\",\n  \"xp\": 10,\n  \"level\": 2,\n  \"achievements\": [\n    \"a1\"\n  ]\n}", true),
            Post("/v2/user/heartbeat", "{\n  \"unified_id\": \"u_abc123\"\n}", true),
            Post("/v2/user/delete-account", "{\n  \"unified_id\": \"u_abc123\",\n  \"confirmation\": \"DELETE\"\n}", true),
            Post("/v2/auth/device/authorize", "{\n  \"unified_id\": \"u_abc123\",\n  \"client\": \"ccp-desktop\"\n}", true),
        };
        Assert.Equal(expected, _fake.Seen);
    }

    [Fact]
    public async Task NoTokenMeansNoAuthHeader()
    {
        _settings.AuthToken = null;
        await Client().SendHeartbeatAsync("u_abc123");
        Assert.DoesNotContain("X-Auth-Token", Assert.Single(_fake.Seen));
    }

    [Fact]
    public async Task ErrorsMapAsWpfDid()
    {
        var c = Client();
        _fake.Status = HttpStatusCode.BadRequest; _fake.Body = "{\"error\":\"name_taken\"}";
        Assert.Equal("name_taken", (await c.AuthenticateWithDiscordAsync("t")).Error);
        _fake.Status = HttpStatusCode.InternalServerError; _fake.Body = "<html>";
        Assert.Equal("HTTP 500", (await c.LoginAsync("a", "b")).Error);
        Assert.Null(await c.GetUserProfileNodeAsync("u_abc123"));
        Assert.False(await c.SendHeartbeatAsync("u_abc123"));
        _fake.Throw = new HttpRequestException("down");
        Assert.Equal("Login failed. Please try again.", (await c.LoginAsync("a", "b")).Error);
        Assert.Equal("Registration failed. Please try again.", (await c.RegisterAsync("i", "a", "b")).Error);
        Assert.Equal("down", (await c.AuthenticateWithPatreonAsync("t")).Error);
        _fake.Throw = null;

        // Contract D goes through the head's seam; unseeded it is an ordinary error.
        _fake.Status = HttpStatusCode.Conflict; _fake.Body = "{\"error\":\"merged\"}";
        Assert.Equal("merged", (await c.LinkProviderAsync("u", "discord", "t")).Error);
        V2AuthService.MergedRecovery = (_, _) => Task.FromResult(true);
        Assert.Equal("account_merged", (await c.LinkProviderAsync("u", "discord", "t")).Error);
        Assert.False(await c.UpdateUserProfileAsync("u", xp: 1));
    }

    [Fact]
    public async Task TokensAndPasswordsAreNeverLogged()
    {
        var sink = new Sink();
        var old = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var c = Client();
            _fake.Body = $"{{\"success\":true,\"auth_token\":\"{Tok}\",\"link_code\":\"LINKCODE-9\"}}";
            await c.AuthenticateWithDiscordAsync("ACCESS-777", "Bambi");
            await c.LoginAsync("Bambi", "PASSWORD-555");
            await c.LinkProviderAsync("u_abc123", "patreon", "ACCESS-777");
            await c.AuthorizeMobileLinkAsync();
            _fake.Status = HttpStatusCode.InternalServerError;
            await c.GetUserProfileNodeAsync("u_abc123");
            await c.AuthorizeMobileLinkAsync();
            _fake.Throw = new HttpRequestException("down");
            await c.LoginAsync("Bambi", "PASSWORD-555");
            await c.AuthenticateWithPatreonAsync("ACCESS-777");
        }
        finally { Log.Logger = old; }

        Assert.NotEmpty(sink.Lines);
        foreach (var secret in new[] { Tok, "ACCESS-777", "PASSWORD-555", "LINKCODE-9" })
            Assert.DoesNotContain(sink.Lines, l => l.Contains(secret));
    }

    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public readonly List<string> Lines = new();
        public void Emit(LogEvent e) => Lines.Add(e.RenderMessage() + " " + e.Exception);
    }

    private static string Post(string path, string body, bool auth) =>
        $"POST {Url}{path}\nContent-Type: application/json; charset=utf-8\n" +
        (auth ? $"User-Agent: ConditioningControlPanel/{Ver}\nX-Auth-Token: {Tok}\n" : $"User-Agent: ConditioningControlPanel/{Ver}\n") +
        $"X-Client-Version: {Ver}\n\n{body}";
}
