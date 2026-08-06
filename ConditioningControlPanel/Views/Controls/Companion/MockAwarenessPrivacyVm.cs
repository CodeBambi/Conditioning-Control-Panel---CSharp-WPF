using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Design-time / state-gallery implementation of <see cref="IAwarenessPrivacyVm"/>.
    /// </summary>
    public sealed class MockAwarenessPrivacyVm : CompanionObservable, IAwarenessPrivacyVm
    {
        private AwarenessIntensity _intensity = AwarenessIntensity.BroadStrokes;
        private bool _allowPageTitles;

        /// <summary>Parameterless ctor for <c>d:DesignInstance IsDesignTimeCreatable=True</c>.</summary>
        public MockAwarenessPrivacyVm()
        {
            DenyList = new IDenyChipVm[]
            {
                new CompanionDenyChip(CompanionLocStaging.Resolve("companion_awareness_deny_passwords")),
                new CompanionDenyChip(CompanionLocStaging.Resolve("companion_awareness_deny_banking")),
                new CompanionDenyChip(CompanionLocStaging.Resolve("companion_awareness_deny_email"))
            };
            AddDenyCommand = CompanionRelayCommand.NoOp("awareness.addDeny");
            AllowPerAppCommand = CompanionRelayCommand.NoOp("awareness.allowPerApp");
            FineTuningCommand = new CompanionRelayCommand(() =>
            {
                CompanionRelayCommand.Note("awareness.fineTuning");
                Navigator?.RevealWorkshop(CompanionRoomAnchors.WorkshopAwarenessCell);
            });
        }

        /// <summary>
        /// Set by <see cref="MockCompanionRoomVm"/> so "fine-tuning ↓" lands on the Workshop's
        /// awareness pigeonhole, which is where the cooldown sliders live. Null standalone.
        /// </summary>
        public ICompanionRoomNavigator? Navigator { get; set; }

        public AwarenessIntensity Intensity
        {
            get => _intensity;
            set => Set(ref _intensity, value);
        }

        public bool IsEverythingAvailable { get; init; }
        public string WireLine { get; init; } = "[ fun · Chrome · 22m ]";
        public bool IsWireLive { get; init; } = true;
        public string WireCaption { get; init; } =
            CompanionLocStaging.Resolve("companion_awareness_wire_caption");
        public string DormantCopy { get; init; } =
            CompanionLocStaging.Resolve("companion_awareness_dormant_copy");
        public bool IsDormant { get; init; }

        public IReadOnlyList<IDenyChipVm> DenyList { get; init; }
        public string AddDenyLabel { get; init; } =
            CompanionLocStaging.Resolve("companion_awareness_add_deny");

        public bool AllowPageTitles
        {
            get => _allowPageTitles;
            set => Set(ref _allowPageTitles, value);
        }

        public string PageTitlesLabel { get; init; } =
            CompanionLocStaging.Resolve("companion_awareness_page_titles_hidden");

        public ICommand AddDenyCommand { get; }
        public ICommand AllowPerAppCommand { get; }
        public ICommand FineTuningCommand { get; }

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
            WireLine = "[ her eyes are closed ]"
        };

        /// <summary>Dial at Off — her eyes are closed, and the wire says exactly that.</summary>
        public static MockAwarenessPrivacyVm EyesClosed() => new()
        {
            IsEverythingAvailable = true,
            IsWireLive = false,
            WireLine = CompanionLocStaging.Resolve("companion_awareness_wire_closed"),
            Intensity = AwarenessIntensity.Off
        };
    }
}
