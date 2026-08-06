using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z5 — What she can see, over the real Awareness v2 privacy layer.
    ///
    /// <para><b>The dial, honestly mapped.</b> There is one capability switch (awareness on/off) and one
    /// breadth switch (whether any app's page title may travel), so the three stops are:</para>
    /// <list type="bullet">
    ///   <item><b>Off</b> → awareness off. Her eyes are closed; nothing is observed and nothing is
    ///   recorded.</item>
    ///   <item><b>Broad strokes</b> → awareness on with an EMPTY title allow list: categories, app names
    ///   and rounded numbers, never a page title. This is the shipped default and the inversion.</item>
    ///   <item><b>Everything</b> → awareness on with at least one app whose titles she may carry.
    ///   Selecting it opens the per-app editor rather than switching anything, and the dial only reports
    ///   "Everything" once an app is actually listed — a stop that silently meant nothing would be the
    ///   privacy failure that looks like a working feature.</item>
    /// </list>
    ///
    /// <para><b>Nothing here re-implements a rule.</b> The matching lives in
    /// <see cref="AwarenessPrivacyRules"/>, the sanitising in <see cref="AwarenessText"/> (via the
    /// settings setters), the erasure in <see cref="AwarenessLive"/> and the counters in
    /// <see cref="ActivityLedger"/>. This class reads them and offers buttons; a second dialect of any
    /// of those is how the panel would end up describing behaviour the code does not have.</para>
    ///
    /// <para><b>Nothing here is load-bearing for retention either.</b> Pruning happens when the ledger
    /// starts and on day rollover, whether or not this card is ever opened — changing the retention
    /// choice prunes immediately on top of that, it does not replace it.</para>
    /// </summary>
    internal sealed class AwarenessPrivacyRuntimeVm : CompanionObservable, IAwarenessPrivacyVm
    {
        /// <summary>How many chips each app row shows before it stops being a row and starts being a wall.</summary>
        private const int MaxAppChips = 8;

        private readonly CompanionRuntimeContext _ctx;

        // Observable, not plain lists: WPF suppresses an ItemsSource change whose new value is
        // reference-equal to the old one, and every one of these is refilled in place on Sync.
        private readonly ObservableCollection<IDenyChipVm> _deny = new();
        private readonly ObservableCollection<IDenyChipVm> _allow = new();
        private readonly ObservableCollection<IAwarenessAppChipVm> _seen = new();
        private readonly ObservableCollection<IAwarenessAppChipVm> _known = new();

        private AwarenessIntensity _intensity = AwarenessIntensity.Off;
        private string _wireLine = string.Empty;
        private string _wireJson = string.Empty;
        private bool _isWireLive;
        private bool _isJsonExpanded;
        private bool _isPaused;
        private int _retentionDays = 30;

        public AwarenessPrivacyRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;

            AddDenyCommand = new CompanionRelayCommand(EditDenyList);
            AllowPerAppCommand = new CompanionRelayCommand(EditTitleAllowList);
            ToggleJsonCommand = new CompanionRelayCommand(() => IsJsonExpanded = !IsJsonExpanded);
            PauseCommand = new CompanionRelayCommand(TogglePause);
            WipeCommand = new CompanionRelayCommand(Wipe);
            FineTuningCommand = new CompanionRelayCommand(
                () => _ctx.Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell));

            Sync();
        }

        // =====================================================================================
        //  the dial
        // =====================================================================================

        public AwarenessIntensity Intensity
        {
            get => _intensity;
            set
            {
                if (_intensity == value) return;

                switch (value)
                {
                    case AwarenessIntensity.Off:
                        _ctx.WithWindow(w => w.SetAwarenessEnabled(false));
                        break;

                    case AwarenessIntensity.BroadStrokes:
                        // "Broad strokes" is a promise that no page title travels, so selecting it
                        // empties the allow list. That is the privacy-forward direction of the change
                        // and it is what the label says; the list is one click away to rebuild.
                        _ctx.WithWindow(w =>
                        {
                            if (w.SetAwarenessEnabled(true)) ClearTitleAllowList();
                        });
                        break;

                    default:
                        // Everything: enable, then ASK. Nothing widens because a segment was pressed.
                        _ctx.WithWindow(w =>
                        {
                            if (w.SetAwarenessEnabled(true)) EditTitleAllowList();
                        });
                        break;
                }

                Sync();
                Raise(nameof(Intensity));
            }
        }

        /// <summary>Train 2 has landed: all three stops are real.</summary>
        public bool IsEverythingAvailable => true;

        /// <summary>Train 2 has landed: there is no promise block under the wire any more.</summary>
        public bool IsDormant => false;

        // =====================================================================================
        //  the wire
        // =====================================================================================

        public string WireLine { get => _wireLine; private set => Set(ref _wireLine, value); }

        public bool IsWireLive
        {
            get => _isWireLive;
            private set => Set(ref _isWireLive, value);
        }

        public string WireCaption => Loc.Get("companion_awareness_wire_caption");
        public string DormantCopy => Loc.Get("companion_awareness_dormant_copy");

        public string WireJson
        {
            get => _wireJson;
            private set { if (Set(ref _wireJson, value)) Raise(nameof(HasWireJson)); }
        }

        public bool HasWireJson => !string.IsNullOrWhiteSpace(WireJson);
        public string WireJsonEmptyCopy => Loc.Get("companion_awareness_wire_json_empty");

        public bool IsJsonExpanded
        {
            get => _isJsonExpanded;
            set { if (Set(ref _isJsonExpanded, value)) Raise(nameof(JsonToggleLabel)); }
        }

        public string JsonToggleLabel => Loc.Get(IsJsonExpanded
            ? "companion_awareness_wire_json_hide"
            : "companion_awareness_wire_json_show");

        // =====================================================================================
        //  lists
        // =====================================================================================

        public IReadOnlyList<IDenyChipVm> DenyList => _deny;
        public string AddDenyLabel => Loc.Get("companion_awareness_add_deny");

        public IReadOnlyList<IDenyChipVm> TitleAllowList => _allow;
        public string TitleAllowLabel => Loc.Get("companion_awareness_allow_label");

        public IReadOnlyList<IAwarenessAppChipVm> SeenApps => _seen;
        public string SeenAppsLabel => Loc.Get("companion_awareness_seen_label");

        public IReadOnlyList<IAwarenessAppChipVm> KnownApps => _known;
        public string KnownAppsLabel => Loc.Get("companion_awareness_known_label");

        /// <summary>
        /// True when at least one app is title-allow-listed. Not a global switch and never has been in
        /// v2: turning it on asks which app, turning it off empties the list.
        /// </summary>
        public bool AllowPageTitles
        {
            get => (App.Settings?.Current?.AwarenessTitleAllowList?.Count ?? 0) > 0;
            set
            {
                if (value == AllowPageTitles) return;
                if (value) EditTitleAllowList();
                else ClearTitleAllowList();
                Sync();
                Raise(nameof(AllowPageTitles));
            }
        }

        public string PageTitlesLabel
        {
            get
            {
                int count = App.Settings?.Current?.AwarenessTitleAllowList?.Count ?? 0;
                return count == 0
                    ? Loc.Get("companion_awareness_page_titles_hidden")
                    : Loc.GetF("companion_awareness_page_titles_allowed_fmt", count);
            }
        }

        // =====================================================================================
        //  ledger controls
        // =====================================================================================

        public int RetentionDays
        {
            get => _retentionDays;
            set
            {
                if (_retentionDays == value) return;
                CompanionRuntimeContext.Guarded(() =>
                {
                    var settings = App.Settings?.Current;
                    if (settings == null) return;

                    settings.AwarenessRetentionDays = value;   // setter clamps to 7..90
                    App.Settings?.Save();

                    // Shortening the window has to bite now, not at the next start-up: a user who just
                    // chose "7 days" has said something about the 8th.
                    AwarenessLive.Ledger?.PruneRetention(DateTime.Now);
                    App.Logger?.Information("Awareness: retention set to {Days}d", settings.AwarenessRetentionDays);
                }, "awareness retention");

                Sync();
                Raise(nameof(RetentionDays));
            }
        }

        public string RetentionLabel => Loc.GetF("companion_awareness_retention_fmt", RetentionDays);

        public bool IsPaused
        {
            get => _isPaused;
            private set { if (Set(ref _isPaused, value)) Raise(nameof(PauseLabel)); }
        }

        public string PauseLabel
        {
            get
            {
                if (!IsPaused) return Loc.Get("companion_awareness_pause");
                int minutes = (int)Math.Ceiling(AwarenessPause.Remaining().TotalMinutes);
                return Loc.GetF("companion_awareness_paused_fmt", Math.Max(minutes, 1));
            }
        }

        public string WipeLabel => Loc.Get("companion_awareness_wipe");

        public ICommand AddDenyCommand { get; }
        public ICommand AllowPerAppCommand { get; }
        public ICommand FineTuningCommand { get; }
        public ICommand ToggleJsonCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand WipeCommand { get; }

        // =====================================================================================
        //  sync
        // =====================================================================================

        /// <summary>
        /// Re-reads everything the card shows. Called by the room's <c>Sync</c> and by the view's own
        /// refresh tick while it is visible — the wire view is a live readout, and a stale one on this
        /// particular card would be the least trustworthy element on the page.
        /// </summary>
        public void Sync() => CompanionRuntimeContext.Guarded(() =>
        {
            var settings = App.Settings?.Current;
            bool on = settings?.AwarenessModeEnabled == true && settings?.AwarenessConsentGiven == true;
            bool paused = AwarenessPause.IsPaused();

            IsPaused = paused;
            _retentionDays = settings?.AwarenessRetentionDays ?? 30;
            Raise(nameof(RetentionDays));
            Raise(nameof(RetentionLabel));

            _intensity = !on
                ? AwarenessIntensity.Off
                : ((settings?.AwarenessTitleAllowList?.Count ?? 0) > 0
                    ? AwarenessIntensity.Everything
                    : AwarenessIntensity.BroadStrokes);
            Raise(nameof(Intensity));

            IsWireLive = on && !paused;
            WireLine = BuildWireLine(settings, on, paused);
            WireJson = BuildWireJson(on);

            RebuildDeny(settings);
            RebuildAllow(settings);
            RebuildSeen(settings);
            RebuildKnown();

            Raise(nameof(AllowPageTitles));
            Raise(nameof(PageTitlesLabel));
            Raise(nameof(PauseLabel));
        }, "awareness sync");

        // =====================================================================================
        //  wire view
        // =====================================================================================

        /// <summary>
        /// The projected ContextFrame, in the compact form the mockup draws
        /// (<c>[ fun · Chrome · 22m ]</c>).
        ///
        /// <para>Static and parameterised so the promise "this exact line is what she gets" is a thing a
        /// test can hold us to. The privacy decision about the title is made by the caller — this only
        /// renders what it was handed.</para>
        /// </summary>
        internal static string FormatWire(string? category, string? app, string? title, TimeSpan? duration)
        {
            var parts = new List<string>(4);
            if (!string.IsNullOrWhiteSpace(category)) parts.Add(category!.Trim());
            if (!string.IsNullOrWhiteSpace(app)) parts.Add(app!.Trim());
            if (!string.IsNullOrWhiteSpace(title) &&
                !string.Equals(title, app, StringComparison.OrdinalIgnoreCase)) parts.Add(title!.Trim());
            parts.Add(FrameFormatter.Duration(duration));
            return "[ " + string.Join(" · ", parts) + " ]";
        }

        /// <summary>
        /// The live line for the CURRENT window, run through the same privacy layer the observer uses —
        /// so a deny-listed or incognito window shows as dropped here exactly as it is dropped there,
        /// and the page title only ever appears when the app is allow-listed.
        /// </summary>
        private static string BuildWireLine(AppSettings? settings, bool on, bool paused)
        {
            if (!on) return Loc.Get("companion_awareness_wire_closed");
            if (paused) return Loc.Get("companion_awareness_wire_paused");

            var awareness = App.WindowAwareness;
            if (awareness == null || !awareness.IsRunning) return Loc.Get("companion_awareness_wire_idle");

            var app = string.IsNullOrEmpty(awareness.CurrentServiceName)
                ? awareness.CurrentDetectedName
                : awareness.CurrentServiceName;
            var rawTitle = string.IsNullOrEmpty(awareness.CurrentPageTitle)
                ? awareness.CurrentDetectedName
                : awareness.CurrentPageTitle;

            // The cluster is passed as null because the legacy service exposes no cluster property.
            // That only ever makes this readout MORE conservative — the adult-cluster rule can only
            // remove a title — so the line can under-report what she would get, never over-report it.
            var decision = AwarenessPrivacyRules.Evaluate(
                new AwarenessSightRequest(app, app, null, rawTitle), settings, DateTime.Now);

            if (!decision.Allowed) return Loc.Get("companion_awareness_wire_dropped");

            return FormatWire(awareness.CurrentActivity.ToString().ToLowerInvariant(), app,
                              decision.TitleForWire, awareness.CurrentActivityDuration);
        }

        /// <summary>
        /// The last frame she actually cut, as the actual cloud projection, pretty-printed. Empty when
        /// no frame has been cut — the card then says nothing has been sent rather than rendering a
        /// reconstruction and calling it the wire format.
        /// </summary>
        private static string BuildWireJson(bool on)
        {
            if (!on) return string.Empty;

            var frame = AwarenessLive.LastFrame;
            if (frame == null) return string.Empty;

            var json = AwarenessProjection.BuildCloudProjection(frame);
            if (string.IsNullOrWhiteSpace(json) || json == "{}") return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                // Showing the raw bytes is more honest than showing nothing; this is the wire view.
                return json;
            }
        }

        // =====================================================================================
        //  chips
        // =====================================================================================

        private void RebuildDeny(AppSettings? settings)
        {
            _deny.Clear();
            foreach (var entry in AwarenessPrivacyRules.EffectiveDenyList(settings))
            {
                var raw = entry;
                var labelKey = AwarenessPrivacyRules.ChipLabelKey(raw);
                var label = labelKey.Length > 0 ? Loc.Get(labelKey) : raw;
                _deny.Add(new CompanionDenyChip(label, AwarenessPrivacyRules.IsGroupToken(raw),
                    new CompanionRelayCommand(() => RemoveFromDeny(raw))));
            }
        }

        private void RebuildAllow(AppSettings? settings)
        {
            _allow.Clear();
            foreach (var entry in settings?.AwarenessTitleAllowList ?? new List<string>())
            {
                var raw = entry;
                _allow.Add(new CompanionDenyChip(raw, false,
                    new CompanionRelayCommand(() => RemoveFromAllow(raw))));
            }
        }

        /// <summary>
        /// Apps seen in the foreground recently, offered as one-click "hide this from her".
        ///
        /// <para>The names come from the Triggers engine's in-memory ring (never persisted, our own
        /// process excluded) — the same source the app-scope editor uses, because building a second one
        /// would mean two answers to "which apps have I used". Already-denied apps are dropped rather
        /// than shown inert: a chip that does nothing when clicked reads as broken.</para>
        /// </summary>
        private void RebuildSeen(AppSettings? settings)
        {
            _seen.Clear();

            var deny = AwarenessPrivacyRules.EffectiveDenyList(settings);
            var seen = new List<string>();

            try
            {
                var recent = App.KeywordTriggers?.GetRecentForegroundApps() ?? Array.Empty<string>();
                seen.AddRange(recent);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Awareness panel: seen-app ring unavailable ({E})", ex.Message);
            }

            var current = App.WindowAwareness?.CurrentServiceName;
            if (!string.IsNullOrWhiteSpace(current)) seen.Insert(0, current!);

            foreach (var app in seen.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_seen.Count >= MaxAppChips) break;
                if (string.IsNullOrWhiteSpace(app)) continue;
                if (AwarenessText.SanitizeRuleEntry(app) is not { } clean) continue;
                if (deny.Any(d => string.Equals(d, clean, StringComparison.OrdinalIgnoreCase))) continue;

                _seen.Add(new AwarenessAppChip(app, Loc.Get("companion_awareness_seen_tip"),
                    () => AddToDeny(clean)));
            }
        }

        /// <summary>
        /// The apps the ledger is actually counting this session, each with a forget button. Reads the
        /// live ledger's session ring — there is no second store, and when no ledger exists yet the row
        /// is simply empty rather than invented.
        /// </summary>
        private void RebuildKnown()
        {
            _known.Clear();

            var ledger = AwarenessLive.Ledger;
            if (ledger == null) return;

            foreach (var id in ledger.RecentTransitions
                         .Select(t => t.AppId)
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_known.Count >= MaxAppChips) break;
                var appId = id;
                _known.Add(new AwarenessAppChip(appId, Loc.Get("companion_awareness_forget_tip"),
                    () => ForgetApp(appId)));
            }
        }

        // =====================================================================================
        //  list editing
        // =====================================================================================

        private void AddToDeny(string entry) => CompanionRuntimeContext.Guarded(() =>
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var list = new List<string>(settings.AwarenessDenyList ?? new List<string>());
            if (!list.Contains(entry, StringComparer.OrdinalIgnoreCase)) list.Add(entry);
            settings.AwarenessDenyList = list;              // setter sanitises and de-duplicates
            settings.AwarenessDenySeeded = true;            // the list is the user's from here on
            App.Settings?.Save();

            App.Logger?.Information("Awareness: hiding '{App}' ({Count} deny entries)",
                entry, settings.AwarenessDenyList.Count);
            Sync();
        }, "awareness add deny");

        private void RemoveFromDeny(string entry) => CompanionRuntimeContext.Guarded(() =>
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Removing a chip must actually remove the rule, including a seeded group — which means the
            // seed has to be recorded as done, or the next read would put it straight back.
            var list = new List<string>(AwarenessPrivacyRules.EffectiveDenyList(settings));
            list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            settings.AwarenessDenyList = list;
            settings.AwarenessDenySeeded = true;
            App.Settings?.Save();

            App.Logger?.Information("Awareness: stopped hiding '{App}'", entry);
            Sync();
        }, "awareness remove deny");

        private void RemoveFromAllow(string entry) => CompanionRuntimeContext.Guarded(() =>
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var list = new List<string>(settings.AwarenessTitleAllowList ?? new List<string>());
            list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));
            settings.AwarenessTitleAllowList = list;
            App.Settings?.Save();

            App.Logger?.Information("Awareness: page titles no longer allowed for '{App}'", entry);
            Sync();
        }, "awareness remove allow");

        private void ClearTitleAllowList() => CompanionRuntimeContext.Guarded(() =>
        {
            var settings = App.Settings?.Current;
            if (settings == null || (settings.AwarenessTitleAllowList?.Count ?? 0) == 0) return;

            settings.AwarenessTitleAllowList = new List<string>();
            App.Settings?.Save();
            App.Logger?.Information("Awareness: page titles hidden again for every app");
        }, "awareness clear allow");

        /// <summary>
        /// The deny-list editor. Reuses <c>TextEditorDialog</c> — the same list editor the trigger
        /// phrases use — rather than growing a second one, and writes back through the settings setter
        /// so every entry is sanitised on the way in.
        /// </summary>
        private void EditDenyList() => _ctx.WithWindow(window =>
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var seed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in AwarenessPrivacyRules.EffectiveDenyList(settings)) seed[entry] = true;
            foreach (var chip in _seen) seed.TryAdd(chip.Label.ToLowerInvariant(), false);

            var dialog = new TextEditorDialog(Loc.Get("companion_awareness_deny_editor_title"), seed)
            {
                Owner = window
            };
            if (dialog.ShowDialog() != true || dialog.ResultData == null) return;

            settings.AwarenessDenyList = dialog.ResultData.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            settings.AwarenessDenySeeded = true;
            App.Settings?.Save();

            App.Logger?.Information("Awareness: deny list now has {Count} entries",
                settings.AwarenessDenyList.Count);
            Sync();
        });

        /// <summary>
        /// The per-app title allow editor. Same dialog, same sanitising, opposite meaning: every app
        /// ticked here may have its page title carried, and everything else keeps titles on this PC.
        /// </summary>
        private void EditTitleAllowList() => _ctx.WithWindow(window =>
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var seed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in settings.AwarenessTitleAllowList ?? new List<string>()) seed[entry] = true;
            foreach (var chip in _seen) seed.TryAdd(chip.Label.ToLowerInvariant(), false);

            var dialog = new TextEditorDialog(Loc.Get("companion_awareness_allow_editor_title"), seed)
            {
                Owner = window
            };
            if (dialog.ShowDialog() != true || dialog.ResultData == null) return;

            settings.AwarenessTitleAllowList =
                dialog.ResultData.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            App.Settings?.Save();

            App.Logger?.Information("Awareness: page titles allowed for {Count} app(s)",
                settings.AwarenessTitleAllowList.Count);
            Sync();
        });

        // =====================================================================================
        //  ledger controls
        // =====================================================================================

        private void ForgetApp(string appId) => CompanionRuntimeContext.Guarded(() =>
        {
            AwarenessLive.Forget(appId);
            Sync();
        }, "awareness forget app");

        /// <summary>
        /// Pauses her for an hour, or lifts a running pause. The pause is a hard drop in the privacy
        /// layer, and the legacy poll is stopped as well so nothing observes while it runs.
        /// </summary>
        private void TogglePause() => CompanionRuntimeContext.Guarded(() =>
        {
            if (AwarenessPause.IsPaused())
            {
                AwarenessPause.Resume();
                if (App.Settings?.Current?.AwarenessModeEnabled == true) App.WindowAwareness?.Start();
            }
            else
            {
                AwarenessPause.Pause(AwarenessPause.DefaultDuration);
                App.WindowAwareness?.Stop();
            }

            Sync();
        }, "awareness pause");

        /// <summary>
        /// Erases everything she has noticed: the ledger file, its <c>.tmp</c> sibling, the in-memory
        /// counters and session ring, the pending save, the last projected frame and her recent-lines
        /// list. The card's two-step confirm has already run by the time this does.
        /// </summary>
        private void Wipe() => CompanionRuntimeContext.Guarded(() =>
        {
            AwarenessLive.WipeEverything();
            Sync();
        }, "awareness wipe");
    }
}
