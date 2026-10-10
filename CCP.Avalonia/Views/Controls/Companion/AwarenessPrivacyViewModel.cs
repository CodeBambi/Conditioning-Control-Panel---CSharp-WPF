using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Awareness;
using Serilog;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// Z5 — What she can see, over the real settings. Port of WPF
    /// <c>Runtime/AwarenessPrivacyRuntimeVm.cs</c>: the dial maps Off / Broad strokes / Everything to
    /// awareness off / on with an empty title allow list / on with at least one listed app, and every
    /// ON edge goes through <see cref="MainShellWindow.SetAwarenessEnabled"/> (entitlement, then the
    /// consent dialog). Nothing widens because a segment was pressed.
    ///
    /// <para><b>The v2 observer and ledger are on this head</b> (Platform/AwarenessHead.cs), so WPF's
    /// own rule applies as written: <c>IsLegacyPipeline = on &amp;&amp; !AwarenessObserver.IsEnabled</c>.
    /// The wire and its JSON read <see cref="AwarenessLive.LastFrame"/> through the cloud projection,
    /// the known-apps row reads the ledger, and wipe / forget go through <see cref="AwarenessLive"/>.
    /// The seen-apps row is still the window in front right now.</para>
    /// </summary>
    public sealed class AwarenessPrivacyViewModel : INotifyPropertyChanged
    {
        private const int MaxAppChips = 8;   // WPF MaxAppChips

        private readonly Func<MainShellWindow?> _window;
        private readonly Action? _revealFineTuning;
        private readonly ObservableCollection<AwarenessChip> _deny = new();
        private readonly ObservableCollection<AwarenessChip> _allow = new();
        private readonly ObservableCollection<AwarenessChip> _seen = new();
        private readonly List<string> _denyKeys = new(), _allowKeys = new(), _seenKeys = new();

        private AwarenessIntensity _intensity = AwarenessIntensity.Off;
        private bool _isJsonExpanded, _isPaused, _isLegacyPipeline;
        private string _wireLine = string.Empty;
        private int _retentionDays = 30;

        public AwarenessPrivacyViewModel(Func<MainShellWindow?> window, Action? revealFineTuning = null)
        {
            _window = window;
            _revealFineTuning = revealFineTuning;
            ReviewConsentCommand = new RelayCommand(() => _ = ReviewV2ConsentAsync());
            AddDenyCommand = new RelayCommand(() => _ = EditDenyListAsync());
            AllowPerAppCommand = new RelayCommand(() => _ = EditTitleAllowListAsync());
            ToggleJsonCommand = new RelayCommand(() => IsJsonExpanded = !IsJsonExpanded);
            PauseCommand = new RelayCommand(TogglePause);
            WipeCommand = new RelayCommand(Wipe);
            FineTuningCommand = new RelayCommand(() => _revealFineTuning?.Invoke());
            Sync();
        }

        /// <summary>The last dial/picker/consent flow started from this card, for tests to await.</summary>
        internal Task Pending { get; private set; } = Task.CompletedTask;

        // ------------------------------- the dial -------------------------------

        public AwarenessIntensity Intensity
        {
            get => _intensity;
            set
            {
                if (_intensity == value) return;
                Pending = ApplyIntensityAsync(value);
            }
        }

        private async Task ApplyIntensityAsync(AwarenessIntensity value)
        {
            try
            {
                var w = _window();
                if (w != null)
                {
                    if (value == AwarenessIntensity.Off) await w.SetAwarenessEnabled(false);
                    // Broad strokes promises no page title travels, so it empties the allow list.
                    else if (await w.SetAwarenessEnabled(true))
                    {
                        if (value == AwarenessIntensity.BroadStrokes) ClearTitleAllowList();
                        else await EditTitleAllowListAsync();   // Everything: enable, then ASK
                    }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Awareness dial failed"); }
            Sync();   // snaps the dial to what is actually true
        }

        public bool IsEverythingAvailable => true;
        public string? EverythingLockedTip => null;
        public string DialHint => _intensity switch
        {
            AwarenessIntensity.Off => Loc.Get("companion_awareness_dial_hint_off"),
            AwarenessIntensity.BroadStrokes => Loc.Get("companion_awareness_dial_hint_broad"),
            _ => Loc.Get("companion_awareness_dial_hint_everything")
        };
        public bool IsDormant => false;
        public string DormantCopy => Loc.Get("companion_awareness_dormant_copy");

        public bool IsLegacyPipeline => _isLegacyPipeline;
        public string IncognitoCopy => Loc.Get(_isLegacyPipeline
            ? "companion_awareness_incognito_legacy" : "companion_awareness_incognito");
        public string LegacyHead => Loc.Get("companion_awareness_legacy_head");
        public string LegacyBody => Loc.Get("companion_awareness_legacy_body");
        public string LegacyAction => Loc.Get("companion_awareness_legacy_action");
        /// <summary>Hidden on this head: the dial's own consent already set AwarenessConsentShownV2, so
        /// re-asking shows nothing and the band would stay - a button that cannot switch the v2
        /// protections on (there is no v2 observer here) must not offer to. Show it when v2 lands.</summary>
        public bool CanReviewConsent => AwarenessConsentDialog.IsRequired(CoreSettings.Current);

        // ------------------------------- the wire -------------------------------

        public string WireLine { get => _wireLine; private set => Set(ref _wireLine, value); }
        /// <summary>WPF Sync: live only on the v2 observer, on and not paused. The legacy pipeline
        /// writes no frame, so it is never styled as a live readout.</summary>
        public bool IsWireLive => _isWireLive;
        private bool _isWireLive;
        private string _wireJson = string.Empty;
        public string WireCaption => Loc.Get(_isLegacyPipeline
            ? "companion_awareness_wire_caption_legacy" : "companion_awareness_wire_caption");
        /// <summary>The cloud projection of the last frame that went out (WPF BuildWireJson). Empty on
        /// the legacy pipeline: a reconstruction is not the wire.</summary>
        public string WireJson => _wireJson;
        public bool HasWireJson => !string.IsNullOrWhiteSpace(_wireJson);
        public string WireJsonEmptyCopy => Loc.Get("companion_awareness_wire_json_empty");

        public bool IsJsonExpanded
        {
            get => _isJsonExpanded;
            set { if (Set(ref _isJsonExpanded, value)) Raise(nameof(JsonToggleLabel)); }
        }
        public string JsonToggleLabel => Loc.Get(IsJsonExpanded
            ? "companion_awareness_wire_json_hide" : "companion_awareness_wire_json_show");

        // ------------------------------- lists -------------------------------

        public IReadOnlyList<AwarenessChip> DenyList => _deny;
        public string AddDenyLabel => Loc.Get("companion_awareness_add_deny");
        public IReadOnlyList<AwarenessChip> TitleAllowList => _allow;
        public bool HasTitleAllowList => _allow.Count > 0;
        public string TitleAllowLabel => Loc.Get("companion_awareness_allow_label");
        public IReadOnlyList<AwarenessChip> SeenApps => _seen;
        public bool HasSeenApps => _seen.Count > 0;
        public string SeenAppsLabel => Loc.Get("companion_awareness_seen_label");
        /// <summary>WPF RebuildKnown: the apps the ledger keeps counters on, each with its own forget.</summary>
        public IReadOnlyList<AwarenessChip> KnownApps => _known;
        public bool HasKnownApps => _known.Count > 0;
        private readonly ObservableCollection<AwarenessChip> _known = new();
        private readonly List<string> _knownKeys = new();
        public string KnownAppsLabel => Loc.Get("companion_awareness_known_label");

        /// <summary>True when an app is title-allow-listed; on asks which app, off empties the list.</summary>
        public bool AllowPageTitles
        {
            get => (CoreSettings.Current.AwarenessTitleAllowList?.Count ?? 0) > 0;
            set
            {
                if (value == AllowPageTitles) return;
                if (value) { Pending = EditTitleAllowListAsync(); return; }
                ClearTitleAllowList();
                Sync();
            }
        }

        public string PageTitlesLabel
        {
            get
            {
                int count = CoreSettings.Current.AwarenessTitleAllowList?.Count ?? 0;
                return count == 0
                    ? Loc.Get("companion_awareness_page_titles_hidden")
                    : Loc.GetF("companion_awareness_page_titles_allowed_fmt", count);
            }
        }

        // ------------------------------- ledger controls -------------------------------

        public int RetentionDays
        {
            get => _retentionDays;
            set
            {
                if (_retentionDays == value) return;
                CoreSettings.Current.AwarenessRetentionDays = value;   // setter clamps to 7..90
                CoreSettings.Save();
                // Shortening the window bites now, not at the next start-up (WPF :266).
                try { AwarenessLive.Ledger?.PruneRetention(DateTime.Now); }
                catch (Exception ex) { Log.Debug("Awareness: retention prune failed: {E}", ex.Message); }
                Log.Information("Awareness: retention set to {Days}d", CoreSettings.Current.AwarenessRetentionDays);
                Sync();
            }
        }
        public string RetentionLabel => Loc.GetF("companion_awareness_retention_fmt", RetentionDays);

        public bool IsPaused => _isPaused;
        public string PauseLabel => !_isPaused
            ? Loc.Get("companion_awareness_pause")
            : Loc.GetF("companion_awareness_paused_fmt",
                Math.Max((int)Math.Ceiling(AwarenessPause.Remaining().TotalMinutes), 1));
        public string WipeLabel => Loc.Get("companion_awareness_wipe");

        public ICommand AddDenyCommand { get; }
        public ICommand AllowPerAppCommand { get; }
        public ICommand ReviewConsentCommand { get; }
        public ICommand FineTuningCommand { get; }
        public ICommand ToggleJsonCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand WipeCommand { get; }

        // ------------------------------- sync -------------------------------

        /// <summary>Re-reads everything the card shows (room show + the view's 1.5 s visible-only tick).</summary>
        public void Sync()
        {
            try
            {
                var s = CoreSettings.Current;
                bool on = s.AwarenessModeEnabled && s.AwarenessConsentGiven;
                _isPaused = AwarenessPause.IsPaused();
                // Watching, but not through the v2 privacy layer (WPF :323): the legacy poll needs only
                // two of IsEnabled's four flags.
                _isLegacyPipeline = on && !AwarenessObserver.IsEnabled;
                _retentionDays = s.AwarenessRetentionDays;
                _intensity = !on ? AwarenessIntensity.Off
                    : (s.AwarenessTitleAllowList?.Count ?? 0) > 0 ? AwarenessIntensity.Everything
                    : AwarenessIntensity.BroadStrokes;
                _isWireLive = on && !_isPaused && !_isLegacyPipeline;
                WireLine = BuildWireLine(on, _isPaused, _isLegacyPipeline);
                _wireJson = _isLegacyPipeline ? string.Empty : BuildWireJson(on);

                RebuildChips(s);
                RebuildKnown();
                foreach (var n in new[] { nameof(IsWireLive), nameof(WireJson), nameof(HasWireJson),
                             nameof(HasKnownApps), nameof(CanReviewConsent) })
                    Raise(n);
                foreach (var n in new[] { nameof(IsPaused), nameof(PauseLabel), nameof(IsLegacyPipeline),
                             nameof(IncognitoCopy), nameof(WireCaption), nameof(RetentionDays), nameof(RetentionLabel),
                             nameof(Intensity), nameof(DialHint), nameof(AllowPageTitles), nameof(PageTitlesLabel),
                             nameof(HasTitleAllowList), nameof(HasSeenApps) })
                    Raise(n);
            }
            catch (Exception ex) { Log.Debug("Awareness panel sync failed: {E}", ex.Message); }
        }

        private void RebuildChips(Models.AppSettings s)
        {
            var deny = AwarenessPrivacyRules.EffectiveDenyList(s);
            var next = new List<(string, Func<AwarenessChip>)>();
            foreach (var raw in deny)
            {
                var key = AwarenessPrivacyRules.ChipLabelKey(raw);
                var label = key.Length > 0 ? Loc.Get(key) : raw;
                next.Add((raw + "\u0001" + label, () => new AwarenessChip(label, new RelayCommand(() => RemoveFromDeny(raw)))));
            }
            RefillIfChanged(_deny, _denyKeys, next);

            next = new();
            foreach (var raw in s.AwarenessTitleAllowList ?? new List<string>())
                next.Add((raw, () => new AwarenessChip(raw, new RelayCommand(() => RemoveFromAllow(raw)))));
            RefillIfChanged(_allow, _allowKeys, next);

            // WPF folds in the keyword-trigger ring too; this head has only the window in front.
            next = new();
            var current = AvApp.WindowAwareness.CurrentServiceName;
            if (!string.IsNullOrWhiteSpace(current) && AwarenessText.SanitizeRuleEntry(current) is { } clean
                && !deny.Any(d => string.Equals(d, clean, StringComparison.OrdinalIgnoreCase)) && next.Count < MaxAppChips)
            {
                var tip = Loc.Get("companion_awareness_seen_tip");
                next.Add((current + "\u0001" + clean + "\u0001" + tip, () => new AwarenessChip(current, new RelayCommand(() => AddToDeny(clean)), tip)));
            }
            RefillIfChanged(_seen, _seenKeys, next);
        }

        /// <summary>WPF BuildWireLine: a one-line summary of the last frame that went out, under the same
        /// projection rules as the JSON below it (the adult cluster shows its cluster id, nothing else).</summary>
        internal static string BuildWireLine(bool on, bool paused, bool legacy)
        {
            if (!on) return Loc.Get("companion_awareness_wire_closed");
            if (paused) return Loc.Get("companion_awareness_wire_paused");
            if (legacy) return Loc.Get("companion_awareness_wire_legacy");

            var frame = AwarenessLive.LastFrame;
            if (frame == null) return Loc.Get("companion_awareness_wire_idle");

            var category = frame.Category.ToString().ToLowerInvariant();
            var dwell = TimeSpan.FromSeconds(Math.Max(0, frame.DwellSeconds));
            if (frame.IsAdultCluster)
                return FormatWire(category, AwarenessText.SanitizeId(frame.AppCluster), null, dwell);

            var app = string.IsNullOrWhiteSpace(frame.ServiceName)
                ? AwarenessText.SanitizeId(frame.AppId)
                : AwarenessText.SanitizeDisplayName(frame.ServiceName);
            var title = string.IsNullOrWhiteSpace(frame.PageTitleSanitized)
                ? null
                : AwarenessProjection.ScrubTitle(frame.PageTitleSanitized);
            return FormatWire(category, app, string.IsNullOrWhiteSpace(title) ? null : title, dwell);
        }

        /// <summary>WPF FormatWire: "[ category · app · title · 4m ]".</summary>
        internal static string FormatWire(string? category, string? app, string? title, TimeSpan duration)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(category)) parts.Add(category!.Trim());
            if (!string.IsNullOrWhiteSpace(app)) parts.Add(app!.Trim());
            if (!string.IsNullOrWhiteSpace(title) &&
                !string.Equals(title, app, StringComparison.OrdinalIgnoreCase)) parts.Add(title!.Trim());
            parts.Add(ConditioningControlPanel.Services.AIService.FrameFormatter.Duration(duration));
            return "[ " + string.Join(" \u00B7 ", parts) + " ]";
        }

        /// <summary>WPF BuildWireJson: the cloud projection itself, indented. Raw bytes when it will not parse.</summary>
        internal static string BuildWireJson(bool on)
        {
            if (!on) return string.Empty;
            var frame = AwarenessLive.LastFrame;
            if (frame == null) return string.Empty;

            var json = AwarenessProjection.BuildCloudProjection(frame);
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return string.Empty;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch (System.Text.Json.JsonException) { return json; }
        }

        private void RebuildKnown()
        {
            var next = new List<(string, Func<AwarenessChip>)>();
            var ledger = AwarenessLive.Ledger;
            if (ledger != null)
            {
                var ids = new List<string>(ledger.KnownAppIds);
                ids.AddRange(ledger.RecentTransitions.Select(t => t.AppId));
                var tip = Loc.Get("companion_awareness_forget_tip");
                foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (next.Count >= MaxAppChips) break;
                    var appId = id;
                    next.Add((appId + "\u0001" + tip, () => new AwarenessChip(appId, new RelayCommand(() => ForgetApp(appId)), tip)));
                }
            }
            RefillIfChanged(_known, _knownKeys, next);
        }

        private void ForgetApp(string appId)
        {
            try { AwarenessLive.Forget(appId); }
            catch (Exception ex) { Log.Warning(ex, "Awareness: forget app failed"); }
            Sync();
        }

        /// <summary>Refills a row only when its keys changed (WPF #1323: refilling every tick stalled the UI).</summary>
        internal static bool RefillIfChanged<T>(ObservableCollection<T> target, List<string> lastKeys,
            List<(string Key, Func<T> Make)> next)
        {
            if (lastKeys.Count == next.Count && target.Count == next.Count
                && lastKeys.SequenceEqual(next.Select(n => n.Key), StringComparer.Ordinal)) return false;
            lastKeys.Clear();
            target.Clear();
            foreach (var (key, make) in next) { lastKeys.Add(key); target.Add(make()); }
            return true;
        }

        // ------------------------------- list editing -------------------------------

        private void SaveDeny(List<string> list, string what, string entry)
        {
            var s = CoreSettings.Current;
            s.AwarenessDenyList = list;      // setter sanitises and de-duplicates
            s.AwarenessDenySeeded = true;    // the list is the user's from here on
            CoreSettings.Save();
            Log.Information("Awareness: {What} '{App}' ({Count} deny entries)", what, entry, s.AwarenessDenyList.Count);
            Sync();
        }

        // EFFECTIVE, not raw (WPF AddToDeny): building from the raw list before the seed ran would
        // silently drop the shipped password-manager / banking / email groups.
        private void AddToDeny(string entry)
        {
            var list = new List<string>(AwarenessPrivacyRules.EffectiveDenyList(CoreSettings.Current));
            if (!list.Contains(entry, StringComparer.OrdinalIgnoreCase)) list.Add(entry);
            SaveDeny(list, "hiding", entry);
        }

        private void RemoveFromDeny(string entry)
        {
            var list = new List<string>(AwarenessPrivacyRules.EffectiveDenyList(CoreSettings.Current));
            list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            SaveDeny(list, "stopped hiding", entry);
        }

        private void RemoveFromAllow(string entry)
        {
            var s = CoreSettings.Current;
            var list = new List<string>(s.AwarenessTitleAllowList ?? new List<string>());
            list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            s.AwarenessTitleAllowList = list;
            CoreSettings.Save();
            Log.Information("Awareness: page titles no longer allowed for '{App}'", entry);
            Sync();
        }

        private static void ClearTitleAllowList()
        {
            var s = CoreSettings.Current;
            if ((s.AwarenessTitleAllowList?.Count ?? 0) == 0) return;
            s.AwarenessTitleAllowList = new List<string>();
            CoreSettings.Save();
            Log.Information("Awareness: page titles hidden again for every app");
        }

        internal static IReadOnlyList<string> Candidates(IEnumerable<string> listed)
        {
            // WPF AwarenessPrivacyRuntimeVm: AwarenessAppCandidates.Gather(listed) - the window in front, this
            // session's app switches, the persisted per-app counters, then the trigger ring (the seams are
            // seeded in Platform/AwarenessHead). Before the head is wired the window in front still shows.
            var exclude = listed as IReadOnlyCollection<string> ?? listed.ToList();
            var gathered = AwarenessAppCandidates.Gather(exclude);
            if (AwarenessHost.CurrentServiceName != null) return gathered;
            var current = AwarenessText.SanitizeRuleEntry(AvApp.WindowAwareness.CurrentServiceName);
            if (current == null || exclude.Contains(current, StringComparer.OrdinalIgnoreCase)
                || gathered.Contains(current, StringComparer.OrdinalIgnoreCase)) return gathered;
            return new[] { current }.Concat(gathered).Take(AwarenessAppCandidates.MaxCandidates).ToList();
        }

        private async Task<List<string>?> PickAsync(AwarenessListKind kind, IReadOnlyList<string> listed)
        {
            if (_window() is not { } owner) return null;
            var dialog = new AwarenessAppPickerDialog(kind, listed, Candidates(listed));
            await dialog.ShowDialogSafe(owner);
            return dialog.Result;
        }

        private async Task EditDenyListAsync()
        {
            var picked = await PickAsync(AwarenessListKind.Deny, AwarenessPrivacyRules.EffectiveDenyList(CoreSettings.Current));
            if (picked == null) return;
            SaveDeny(picked, "deny list edited", picked.Count.ToString());
        }

        private async Task EditTitleAllowListAsync()
        {
            var s = CoreSettings.Current;
            var picked = await PickAsync(AwarenessListKind.TitleAllow, s.AwarenessTitleAllowList ?? new List<string>());
            if (picked != null)
            {
                s.AwarenessTitleAllowList = picked;
                CoreSettings.Save();
                Log.Information("Awareness: page titles allowed for {Count} app(s)", s.AwarenessTitleAllowList.Count);
            }
            Sync();
        }

        /// <summary>WPF ReviewV2Consent: asks directly (not the once-per-session upgrader prompt), and on
        /// yes bounces the on/off call site. The kill switch is never flipped here.</summary>
        private async Task ReviewV2ConsentAsync()
        {
            if (_window() is not { } owner) return;
            if (!await AwarenessConsentDialog.EnsureConsentAsync(owner, CoreSettings.Current))
                Log.Information("Awareness: v2 consent declined again from the privacy card");
            else
            {
                try { AvApp.WindowAwareness.Stop(); AvApp.WindowAwareness.Start(); }
                catch (Exception ex) { Log.Warning(ex, "Awareness: restart after v2 consent failed"); }
            }
            Sync();
        }

        /// <summary>A CHECK, never a shutdown: the poll tests the pause every tick, so the hour simply ends.</summary>
        private void TogglePause()
        {
            if (AwarenessPause.IsPaused()) AwarenessPause.Resume();
            else AwarenessPause.Pause(AwarenessPause.DefaultDuration);
            Sync();
        }

        /// <summary>
        /// Erases everything awareness keeps: the activity ledger (memory and file), the recent-line
        /// memory, the published frame and the pacing state (WPF <c>AwarenessLive.WipeEverything</c>).
        /// The two-step confirm ran first.
        /// </summary>
        private void Wipe()
        {
            // WPF Wipe: the live ledger, the ban-list memory, the published frame and the pacing state
            // go through the one erasure in AwarenessLive (it deletes the files directly when no ledger
            // was ever built).
            try { AwarenessLive.WipeEverything(); }
            catch (Exception ex) { Log.Warning(ex, "Awareness: wipe failed"); }
            Sync();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }
}
