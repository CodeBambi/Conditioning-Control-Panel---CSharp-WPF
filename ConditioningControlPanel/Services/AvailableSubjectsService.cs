using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// SP5 layer 3 — Available Subjects directory client. Polls
    /// /v2/directory/list every 15s while the tab is visible (StartPolling)
    /// and surfaces an ObservableCollection&lt;DirectoryEntry&gt; the WPF
    /// DataTemplate binds to. Claim flow returns the one-click pairing URL
    /// from the proxy; caller (MainWindow) opens it via Process.Start and
    /// never logs it.
    ///
    /// Auth: dual-header X-Auth-Token + X-Caller-Unified-Id (the proxy's
    /// SP5L3 retrofit accepts this in addition to Supabase JWT). The caller's
    /// own unified_id goes in X-Caller-Unified-Id; body.unified_id (claim
    /// only) is the SUBJECT being claimed — separate identity.
    /// </summary>
    public class AvailableSubjectsService : IDisposable
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        // 15s tab-visible cadence per spec; proxy serves from a 5s global
        // cache so a single user polling at 15s lands on the cached payload
        // 2/3 of the time. No backoff needed at this rate.
        private const double PollIntervalSeconds = 15.0;

        private readonly HttpClient _httpClient;
        private DispatcherTimer? _pollTimer;
        private CancellationTokenSource? _inFlightCts;
        private bool _disposed;

        public ObservableCollection<DirectoryEntry> Entries { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        private bool _isEmpty = true;
        /// <summary>True iff the last successful refresh returned 0 entries.</summary>
        public bool IsEmpty
        {
            get => _isEmpty;
            private set { _isEmpty = value; OnPropertyChanged(nameof(IsEmpty)); }
        }

        private bool _hasError;
        /// <summary>True iff the last refresh failed (network or non-2xx).</summary>
        public bool HasError
        {
            get => _hasError;
            private set { _hasError = value; OnPropertyChanged(nameof(HasError)); }
        }

        public bool IsPolling => _pollTimer?.IsEnabled == true;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AvailableSubjectsService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }

        /// <summary>
        /// Idempotently start the 15s poll loop. Calling twice is a no-op.
        /// MainWindow.ShowTab("availablesubjects") wires this on tab enter.
        /// </summary>
        public void StartPolling()
        {
            if (_pollTimer?.IsEnabled == true) return;
            // Fire an immediate refresh so the user sees fresh data without
            // waiting 15s on tab enter.
            _ = RefreshAsync();
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(PollIntervalSeconds)
            };
            _pollTimer.Tick += async (_, _) => await RefreshAsync();
            _pollTimer.Start();
            App.Logger?.Information("[AvailableSubjects] polling started");
        }

        /// <summary>
        /// Stop the poll loop and abort any in-flight fetch. Called when the
        /// user navigates away from the tab. Idempotent.
        /// </summary>
        public void StopPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
            try { _inFlightCts?.Cancel(); } catch { /* swallow */ }
        }

        /// <summary>
        /// Fetch the current list and update Entries. Marshals collection
        /// changes to the UI thread via the application dispatcher. Errors
        /// flip HasError; AbortError on cancellation is silent.
        /// </summary>
        public async Task RefreshAsync()
        {
            var unifiedId = App.UnifiedUserId;
            var token = App.Settings?.Current?.AuthToken;
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(token))
            {
                // No auth state — show empty + error rather than 401-spamming.
                HasError = true;
                IsEmpty = true;
                return;
            }

            // Cancel any in-flight refresh so we don't race two responses
            // back-to-back if the timer fires while StartPolling's immediate
            // refresh is still pending.
            try { _inFlightCts?.Cancel(); } catch { }
            _inFlightCts = new CancellationTokenSource();
            var ct = _inFlightCts.Token;

            IsLoading = true;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}/v2/directory/list");
                request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Caller-Unified-Id", unifiedId);

                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[AvailableSubjects] list failed: {Status}", response.StatusCode);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        HasError = true;
                    });
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                var parsed = JObject.Parse(body);
                var arr = parsed["entries"] as JArray ?? new JArray();
                var newEntries = arr.Select(ParseEntry).Where(e => e != null).Cast<DirectoryEntry>().ToList();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    ReconcileEntries(newEntries);
                    IsEmpty = Entries.Count == 0;
                    HasError = false;
                });
            }
            catch (TaskCanceledException) { /* expected on cancel */ }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AvailableSubjects] refresh error");
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    HasError = true;
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Try to claim a subject. On 200 returns the session_url for
        /// immediate Process.Start by the caller. On 409 returns null and
        /// triggers a refresh internally (the card flips to TAKEN). On any
        /// other error returns null and logs status only — never logs the
        /// response body, which on success contains the session_url+PIN.
        /// </summary>
        public async Task<string?> TryClaimAsync(string subjectUnifiedId)
        {
            var unifiedId = App.UnifiedUserId;
            var token = App.Settings?.Current?.AuthToken;
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(token))
            {
                App.Logger?.Warning("[AvailableSubjects] claim called without auth state");
                return null;
            }
            if (string.IsNullOrEmpty(subjectUnifiedId)) return null;

            try
            {
                var jsonBody = JsonConvert.SerializeObject(new { unified_id = subjectUnifiedId });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/directory/claim")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Caller-Unified-Id", unifiedId);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                if ((int)response.StatusCode == 409)
                {
                    // Someone won the race. Re-fetch silently — the new
                    // TAKEN state is the answer the user gets.
                    _ = RefreshAsync();
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("[AvailableSubjects] claim failed: {Status}", response.StatusCode);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JObject.Parse(body);
                var url = parsed["session_url"]?.ToString();
                if (string.IsNullOrEmpty(url))
                {
                    App.Logger?.Warning("[AvailableSubjects] claim 200 without session_url");
                    return null;
                }
                // Success — schedule a refresh so the card flips to claimed=true
                // for the desktop's own view too. Don't log the URL.
                _ = RefreshAsync();
                return url;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[AvailableSubjects] claim error");
                return null;
            }
        }

        private static DirectoryEntry? ParseEntry(JToken t)
        {
            try
            {
                var tagsArr = t["tags"] as JArray;
                var tags = tagsArr != null
                    ? tagsArr.Select(x => x.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    : new List<string>();
                return new DirectoryEntry
                {
                    UnifiedId = t["unified_id"]?.ToString() ?? "",
                    DisplayName = t["display_name"]?.ToString() ?? "Anonymous",
                    Level = t["level"]?.Value<int>() ?? 1,
                    Tags = tags,
                    StatusText = t["status_text"]?.ToString() ?? "",
                    Tier = t["tier"]?.ToString() ?? "light",
                    Claimed = t["claimed"]?.Value<bool>() ?? false
                };
            }
            catch { return null; }
        }

        /// <summary>
        /// Replace Entries contents with newEntries while preserving object
        /// identity for unchanged subjects (so the WPF DataTemplate doesn't
        /// flicker on poll). Match by UnifiedId.
        /// </summary>
        private void ReconcileEntries(List<DirectoryEntry> newEntries)
        {
            var existing = Entries.ToDictionary(e => e.UnifiedId, e => e);
            // Remove entries no longer in the list
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!newEntries.Any(n => n.UnifiedId == Entries[i].UnifiedId))
                    Entries.RemoveAt(i);
            }
            // Update or insert
            for (int idx = 0; idx < newEntries.Count; idx++)
            {
                var n = newEntries[idx];
                if (existing.TryGetValue(n.UnifiedId, out var e))
                {
                    // Update mutable fields in place. Tier + display_name are
                    // immutable for a given session, so only Claimed +
                    // StatusText + Tags realistically change.
                    e.Claimed = n.Claimed;
                    e.StatusText = n.StatusText;
                    e.Tags = n.Tags;
                    // Reorder if needed (rare; ZSET order = opted_in_at).
                    var currentIndex = Entries.IndexOf(e);
                    if (currentIndex != idx && currentIndex >= 0 && idx < Entries.Count)
                    {
                        Entries.Move(currentIndex, idx);
                    }
                }
                else
                {
                    if (idx >= Entries.Count) Entries.Add(n);
                    else Entries.Insert(idx, n);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopPolling();
            try { _inFlightCts?.Dispose(); } catch { }
            try { _httpClient?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// SP5L3: bindable view model for one row in the Available Subjects list.
    /// Mirrors the cclabs-web DirectoryEntry shape but with WPF-friendly
    /// computed properties (TierLabel, ConnectButtonText, IsConnectEnabled).
    /// </summary>
    public class DirectoryEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public string UnifiedId { get; set; } = "";
        public string DisplayName { get; set; } = "Anonymous";
        public int Level { get; set; }

        private List<string> _tags = new();
        public List<string> Tags
        {
            get => _tags;
            set
            {
                _tags = value ?? new List<string>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(TagsCsv));
                OnPropertyChanged(nameof(HasTags));
            }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatusText)); }
        }

        public string Tier { get; set; } = "light";

        private bool _claimed;
        public bool Claimed
        {
            get => _claimed;
            set
            {
                _claimed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnectEnabled));
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CardOpacity));
            }
        }

        // Computed bindings for the DataTemplate.
        public string TierLabel => Tier switch
        {
            "light" => "LIGHT",
            "standard" => "STANDARD",
            "full" => "FULL",
            _ => Tier.ToUpperInvariant()
        };

        public string TagsCsv => string.Join(", ", Tags);
        public bool HasTags => Tags != null && Tags.Count > 0;
        public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
        public bool IsConnectEnabled => !Claimed;
        public string ConnectButtonText => Claimed ? "Taken" : "Connect";
        public double CardOpacity => Claimed ? 0.6 : 1.0;
    }
}
