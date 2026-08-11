using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z7 — The Engine Room. Every scrap of provider plumbing, demoted into a collapsed drawer and
    /// styled deliberately unglamorous: flat, thin gray border, no glow, no gradient. The contrast
    /// with the room above IS the design — that is her, this is wiring.
    ///
    /// <para>Nothing is dropped here: the four provider radios, the cloud status line, the Ollama
    /// model/host + Setup + Test, the BYO endpoint/key/model + sampler + test, the daily request
    /// limit, and the live actions feed all land in this one drawer.</para>
    ///
    /// <para>The old full-card login veil is gone. Logged out is an inline row here; the sell lives
    /// in Z2's teaser, so the page never fully veils.</para>
    /// </summary>
    public interface IEngineRoomDrawerVm : INotifyPropertyChanged
    {
        /// <summary>Two-way. The hero's AI pill deep-links by setting this true and scrolling in.</summary>
        bool IsExpanded { get; set; }

        /// <summary>Two-way — the segmented provider row (the four legacy radios).</summary>
        CompanionProviderMode Provider { get; set; }

        /// <summary>"wiring lives here on purpose. she'd rather you didn't stare."</summary>
        string DrawerNote { get; }

        // ---- cloud ----
        bool IsLoggedIn { get; }
        /// <summary>The inline logged-out row's copy (replaces the old AiFeaturesLockOverlay).</summary>
        string LoginPrompt { get; }
        string LoginButtonLabel { get; }
        /// <summary>"● Connected — cloud proxy · logged in as … · purpose tiers: …".</summary>
        string StatusLine { get; }
        bool IsHealthy { get; }

        // ---- local (Ollama) ----
        string OllamaModel { get; set; }
        string OllamaHost { get; set; }

        // ---- custom (BYO) ----
        string CustomEndpoint { get; set; }
        string CustomApiKey { get; set; }
        string CustomModel { get; set; }

        /// <summary>"Daily limit: 200" — label on the limit button.</summary>
        string DailyLimitLabel { get; }

        // ---- live actions feed (local-only effects channel), docked at the drawer's bottom ----
        bool ShowLiveActions { get; }
        IReadOnlyList<string> LiveActions { get; }
        string LiveActionsPlaceholder { get; }

        ICommand LoginCommand { get; }
        ICommand TestConnectionCommand { get; }
        ICommand SetupLocalCommand { get; }
        ICommand SamplerSettingsCommand { get; }
        ICommand DailyLimitCommand { get; }
    }
}
