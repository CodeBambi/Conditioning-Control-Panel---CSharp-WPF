using System;
using System.Collections.Generic;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IAwarenessPrivacyVm"/>.
    /// </summary>
    public sealed class MockAwarenessPrivacyVm : CompanionObservable, IAwarenessPrivacyVm
    {
        private AwarenessIntensity _intensity = AwarenessIntensity.BroadStrokes;
        private bool _allowPageTitles;
        private bool _isJsonExpanded;
        private int _retentionDays = 30;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockAwarenessPrivacyVm()
        {
            DenyList = new IDenyChipVm[]
            {
                new CompanionDenyChip(Loc.Get("companion_awareness_deny_passwords")),
                new CompanionDenyChip(Loc.Get("companion_awareness_deny_banking")),
                new CompanionDenyChip(Loc.Get("companion_awareness_deny_email"))
            };
            TitleAllowList = Array.Empty<IDenyChipVm>();
            SeenApps = new IAwarenessAppChipVm[]
            {
                new AwarenessAppChip("Chrome", Loc.Get("companion_awareness_seen_tip")),
                new AwarenessAppChip("Discord", Loc.Get("companion_awareness_seen_tip")),
                new AwarenessAppChip("Steam", Loc.Get("companion_awareness_seen_tip"))
            };
            KnownApps = new IAwarenessAppChipVm[]
            {
                new AwarenessAppChip("YouTube", Loc.Get("companion_awareness_forget_tip")),
                new AwarenessAppChip("Discord", Loc.Get("companion_awareness_forget_tip"))
            };

            AddDenyCommand = CompanionRelayCommand.NoOp("awareness.addDeny");
            AllowPerAppCommand = CompanionRelayCommand.NoOp("awareness.allowPerApp");
            ToggleJsonCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("awareness.toggleJson");
                IsJsonExpanded = !IsJsonExpanded;
            });
            PauseCommand = CompanionRelayCommand.NoOp("awareness.pause");
            WipeCommand = CompanionRelayCommand.NoOp("awareness.wipe");
            FineTuningCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("awareness.fineTuning");
                Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell);
            });
        }

        /// <summary>
        /// Set by <see cref="MockCompanionRoomVm"/> so "fine-tuning ↓" lands on the Workshop's
        /// awareness pigeonhole, which is where the intensity dial lives. Null standalone.
        /// </summary>
        public ICompanionRoomNavigator? Navigator { get; set; }

        public AwarenessIntensity Intensity
        {
            get => _intensity;
            set { if (Set(ref _intensity, value)) Raise(nameof(DialHint)); }
        }

        public string DialHint => AwarenessDialCopy.HintFor(_intensity);

        public bool IsEverythingAvailable { get; init; }
        public string WireLine { get; init; } = "[ fun · Chrome · 22m ]";
        public bool IsWireLive { get; init; } = true;
        public string WireCaption { get; init; } =
            Loc.Get("companion_awareness_wire_caption");
        public string DormantCopy { get; init; } =
            Loc.Get("companion_awareness_dormant_copy");
        public bool IsDormant { get; init; }

        public string WireJson { get; init; } =
            "{\n  \"v\": 1,\n  \"cluster\": \"site_video\",\n  \"app\": \"YouTube\",\n" +
            "  \"visits_today\": 4,\n  \"minutes_today\": 45,\n  \"dwell\": \"15-30m\"\n}";
        public bool HasWireJson => !string.IsNullOrWhiteSpace(WireJson);
        public string WireJsonEmptyCopy { get; init; } =
            Loc.Get("companion_awareness_wire_json_empty");

        public bool IsJsonExpanded
        {
            get => _isJsonExpanded;
            set { if (Set(ref _isJsonExpanded, value)) Raise(nameof(JsonToggleLabel)); }
        }

        public string JsonToggleLabel => Loc.Get(IsJsonExpanded
            ? "companion_awareness_wire_json_hide"
            : "companion_awareness_wire_json_show");

        public IReadOnlyList<IDenyChipVm> DenyList { get; init; }
        public string AddDenyLabel { get; init; } = Loc.Get("companion_awareness_add_deny");

        public IReadOnlyList<IDenyChipVm> TitleAllowList { get; init; }
        public string TitleAllowLabel { get; init; } = Loc.Get("companion_awareness_allow_label");

        public IReadOnlyList<IAwarenessAppChipVm> SeenApps { get; init; }
        public string SeenAppsLabel { get; init; } = Loc.Get("companion_awareness_seen_label");

        public IReadOnlyList<IAwarenessAppChipVm> KnownApps { get; init; }
        public string KnownAppsLabel { get; init; } = Loc.Get("companion_awareness_known_label");

        public bool AllowPageTitles
        {
            get => _allowPageTitles;
            set => Set(ref _allowPageTitles, value);
        }

        public string PageTitlesLabel { get; init; } =
            Loc.Get("companion_awareness_page_titles_hidden");

        public int RetentionDays
        {
            get => _retentionDays;
            set { if (Set(ref _retentionDays, value)) Raise(nameof(RetentionLabel)); }
        }

        public string RetentionLabel => Loc.GetF("companion_awareness_retention_fmt", RetentionDays);

        public bool IsPaused { get; init; }
        public string PauseLabel => Loc.Get(IsPaused
            ? "companion_awareness_pause_resume"
            : "companion_awareness_pause");

        public string WipeLabel { get; init; } = Loc.Get("companion_awareness_wipe");

        public ICommand AddDenyCommand { get; }
        public ICommand AllowPerAppCommand { get; }
        public ICommand FineTuningCommand { get; }
        public ICommand ToggleJsonCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand WipeCommand { get; }

        // ------------------------------- state exhibits -------------------------------

        /// <summary>Train 2 landed: all three stops live, wire view showing a real frame.</summary>
        public static MockAwarenessPrivacyVm Live() => new()
        {
            IsEverythingAvailable = true
        };

        /// <summary>
        /// Pre-Train 2 bridge: only Off / Broad strokes are selectable (they drive today's single
        /// toggle), and the wire view is the shimmer placeholder.
        /// </summary>
        public static MockAwarenessPrivacyVm Dormant() => new()
        {
            IsEverythingAvailable = false,
            IsDormant = true,
            IsWireLive = false,
            WireLine = "[ her eyes are closed ]",
            WireJson = string.Empty
        };

        /// <summary>Dial at Off — her eyes are closed, and the wire says exactly that.</summary>
        public static MockAwarenessPrivacyVm EyesClosed() => new()
        {
            IsEverythingAvailable = true,
            IsWireLive = false,
            WireLine = Loc.Get("companion_awareness_wire_closed"),
            WireJson = string.Empty,
            Intensity = AwarenessIntensity.Off
        };

        /// <summary>Paused for an hour: she is on, and looking at nothing.</summary>
        public static MockAwarenessPrivacyVm Paused() => new()
        {
            IsEverythingAvailable = true,
            IsWireLive = false,
            IsPaused = true,
            WireLine = Loc.Get("companion_awareness_wire_paused"),
            WireJson = string.Empty
        };
    }
}
