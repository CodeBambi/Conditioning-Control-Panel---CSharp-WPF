using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Sessions;
using Newtonsoft.Json;

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

    /// <summary>Heartbeat cadence (WPF <c>HeartbeatIntervalSeconds</c>, ProfileSyncService.cs:26).</summary>
    private const int HeartbeatIntervalSeconds = 120;

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProfileSyncService> _logger;
    private readonly ISessionService? _sessionService;

    /// <summary>Client version string; reused for the request headers and the heartbeat body.</summary>
    private readonly string _version;

    /// <summary>
    /// Portable heartbeat timer — replaces the WPF <c>DispatcherTimer</c> (Core has no Dispatcher).
    /// Null while the heartbeat is stopped. (WPF <c>_heartbeatTimer</c>, ProfileSyncService.cs:29.)
    /// </summary>
    private Timer? _heartbeatTimer;

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

    /// <summary>
    /// Production constructor. Creates the default HTTP handler and (optionally) takes the
    /// session seam used for the heartbeat <c>in_session</c> flag.
    /// </summary>
    public ProfileSyncService(
        ISettingsService settings,
        ILogger<ProfileSyncService> logger,
        ISessionService? sessionService = null)
        : this(settings, logger, handler: null, sessionService)
    {
    }

    /// <summary>
    /// Test-seam constructor: injects an <see cref="HttpMessageHandler"/> so unit tests can stub
    /// server responses without touching the network. The public constructor passes
    /// <paramref name="handler"/> = <c>null</c>, which falls back to the default handler, so the
    /// public API is unchanged.
    /// </summary>
    internal ProfileSyncService(
        ISettingsService settings,
        ILogger<ProfileSyncService> logger,
        HttpMessageHandler? handler,
        ISessionService? sessionService = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionService = sessionService;

        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _version = GetCurrentVersion();
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", _version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{_version}");

        // Never logs any token / auth value.
        _logger.LogDebug("ProfileSyncService initialized (client {Version})", _version);
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

    #region Heartbeat

    /// <summary>Test seam: whether the heartbeat timer is currently running.</summary>
    internal bool IsHeartbeatActive => _heartbeatTimer is not null;

    /// <summary>
    /// Whether a preset session is currently running (drives the heartbeat <c>in_session</c> flag).
    /// WPF sends <c>App.IsSessionRunning</c>; in Core <c>ISessionService.State != Idle</c> matches
    /// that contract (session state stays non-Idle while paused). The seam is optional, so this is
    /// <c>false</c> until the head wires <see cref="ISessionService"/> (slice 7).
    /// </summary>
    private bool InSession =>
        (_sessionService?.State ?? SessionState.Idle) != SessionState.Idle;

    /// <inheritdoc />
    public void StartHeartbeat()
    {
        if (_disposed) return;
        if (_heartbeatTimer != null) return;

        // System.Threading.Timer replaces the WPF DispatcherTimer (Core has no Dispatcher).
        // dueTime 0 => immediate first tick (WPF fires one right after Start, line 116); then
        // every HeartbeatIntervalSeconds. The callback runs on a thread-pool thread;
        // SendHeartbeatAsync swallows its own exceptions so nothing goes unobserved.
        _heartbeatTimer = new Timer(
            _ => _ = SendHeartbeatAsync(),
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromSeconds(HeartbeatIntervalSeconds));

        _logger.LogInformation("Heartbeat started (every {Seconds}s)", HeartbeatIntervalSeconds);
    }

    /// <inheritdoc />
    public void StopHeartbeat()
    {
        // Idempotent + null-safe: fine to call when never started or already stopped.
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _logger.LogDebug("Heartbeat stopped");
    }

    /// <summary>
    /// Sends a lightweight V2 heartbeat (<c>POST /v2/user/heartbeat</c>) so the user shows as
    /// online. Skips when disposed, offline, not logged in, or missing a unified id — mirroring the
    /// WPF guards (ProfileSyncService.cs:132). SECURITY: never logs the auth token.
    /// </summary>
    internal async Task SendHeartbeatAsync()
    {
        if (_disposed) return;

        // Skip if offline mode is enabled (WPF guard).
        if (_settings.Current?.OfflineMode == true) return;

        // Requires opt-in + a valid auth token (Core's "logged in" proxy, slice 1).
        if (!IsSyncEnabled) return;

        var unifiedId = _settings.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/user/heartbeat");
            AddAuthHeader(request);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    // ProfileSync slice 7: wire real activity/idle state. WPF uses
                    // App.ActivityTracker?.IsIdle != true; no idle seam exists in Core yet, so
                    // default to active. The heartbeat presence flags are not security-critical.
                    is_active = true,
                    in_session = InSession,
                    app_version = _version
                }),
                Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // ProfileSync slice 4: full HandleUnauthorizedAsync / restore-session recovery.
                // Minimal path for now: count the failure. The token is never cleared or logged.
                ConsecutiveSyncFailures++;
                SyncHealthChanged?.Invoke(this, ConsecutiveSyncFailures);
                _logger.LogWarning("[Auth] Heartbeat unauthorized (401)");
                return;
            }

            _logger.LogDebug("V2 Heartbeat: {Status}", response.StatusCode);
        }
        catch (Exception ex)
        {
            // Heartbeat is best-effort; never surface. Message only, never the token.
            _logger.LogDebug("Heartbeat error: {Error}", ex.Message);
        }
    }

    #endregion

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

        StopHeartbeat();
        _httpClient.Dispose();
        _syncGate.Dispose();
    }
}
