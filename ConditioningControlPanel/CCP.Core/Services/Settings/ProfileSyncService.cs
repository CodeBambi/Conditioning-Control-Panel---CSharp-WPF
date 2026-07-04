using System;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ConditioningControlPanel.Core.Services.Settings;

/// <summary>
/// Cross-platform implementation of <see cref="IProfileSyncService"/>. Owns its own
/// <see cref="HttpClient"/> and reaches the server at <see cref="ProxyBaseUrl"/> using the
/// per-user <c>X-Auth-Token</c> (plus HMAC anti-cheat signing on the sync push).
///
/// Ported slice-by-slice from the WPF <c>ProfileSyncService</c>; see
/// <c>docs/profilesync-port-plan.md</c>. This slice (1) lands only the seam, DTOs, and the
/// pure plumbing helpers (auth header, request signing, disposal). The async members remain
/// inherited default-interface no-ops until the slices noted on <see cref="IProfileSyncService"/>.
///
/// SECURITY: the auth token is never logged. It is read transparently (decrypted) from
/// <c>ISettingsService.Current.AuthToken</c> and attached only as a request header.
/// </summary>
public sealed class ProfileSyncService : IProfileSyncService, IDisposable
{
    /// <summary>Server base URL (WPF const, ProfileSyncService.cs:25).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    /// <summary>Minimum interval between sync round-trips (WPF <c>SyncCooldown</c>, ~line 415).</summary>
    private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProfileSyncService> _logger;

    /// <summary>Gates concurrent sync attempts (WPF <c>_syncGate</c>). Used from slice 4.</summary>
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private bool _syncEnabled = true;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsSyncEnabled => _syncEnabled && !string.IsNullOrEmpty(_settings.Current?.AuthToken);

    /// <inheritdoc />
    public DateTime? LastSyncTime { get; private set; }

    /// <inheritdoc />
    public string? LastSyncError { get; private set; }

    /// <inheritdoc />
    public int ConsecutiveSyncFailures { get; private set; }

    // These events are declared to satisfy IProfileSyncService (events cannot be default
    // interface members). They are raised in later slices (ProfileLoaded: slice 3,
    // SyncHealthChanged: slice 4). Suppress the "never used" warning until then.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<int>? SyncHealthChanged;

    /// <inheritdoc />
    public event EventHandler? ProfileLoaded;
#pragma warning restore CS0067

    public ProfileSyncService(ISettingsService settings, ILogger<ProfileSyncService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var version = GetCurrentVersion();
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{version}");

        // Never logs any token / auth value.
        _logger.LogDebug("ProfileSyncService initialized (client {Version})", version);
    }

    /// <summary>
    /// Adds the <c>X-Auth-Token</c> header to a V2 API request if an auth token is available.
    /// The token value is never logged. (WPF <c>AddAuthHeader</c>, ProfileSyncService.cs:2256.)
    /// </summary>
    private void AddAuthHeader(HttpRequestMessage request)
    {
        var token = _settings.Current?.AuthToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Add("X-Auth-Token", token);
    }

    /// <summary>
    /// Signs an HTTP request with HMAC-SHA256 for anti-cheat verification, adding the
    /// <c>X-CCP-Timestamp</c> and <c>X-CCP-Signature</c> headers. Applied only to the sync push.
    ///
    /// Scheme (ported verbatim from WPF <c>SignRequest</c>, ProfileSyncService.cs:2355):
    /// key = UTF8(<c>{unifiedId}:ccp-anticheat-2026</c>), payload = <c>{unixSeconds}:{body}</c>,
    /// signature = lowercase-hex HMACSHA256(key, payload). The app key is an embedded
    /// obfuscation constant, not a real secret. No-op when <paramref name="unifiedId"/> is empty.
    /// </summary>
    internal static void SignRequest(HttpRequestMessage request, string body, string unifiedId)
    {
        if (string.IsNullOrEmpty(unifiedId)) return;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = $"{timestamp}:{body}";

        // Key derived from unified_id + embedded app key.
        const string appKey = "ccp-anticheat-2026";
        var keyBytes = Encoding.UTF8.GetBytes($"{unifiedId}:{appKey}");

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        request.Headers.Add("X-CCP-Timestamp", timestamp);
        request.Headers.Add("X-CCP-Signature", signature);
    }

    /// <summary>
    /// Resolves the current client version string for the <c>X-Client-Version</c> / user-agent
    /// headers (mirrors <c>RemoteControlService.GetCurrentVersion</c>).
    /// </summary>
    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
            return $"{version.Major}.{version.Minor}.{version.Build}";

        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var clean = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
            if (Version.TryParse(clean, out var parsed))
                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        }

        return "1.0.0";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // ProfileSync slice 2: also StopHeartbeat() here once the heartbeat timer lands.
        _httpClient.Dispose();
        _syncGate.Dispose();
    }
}
