using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z7 — The Engine Room, over the real provider strategy settings.
    ///
    /// <para>Every control the old AI Brain card owned lands here: the four provider radios (as a
    /// segmented row), the cloud status line and the inline logged-out row that replaced the
    /// page-wide lock veil, the Ollama model/host + Setup + Test, the BYO endpoint/key/model +
    /// sampler + Test, the daily request limit, and the live-actions feed.</para>
    ///
    /// <para><b>The second wipe lives here.</b> Doc 01 §2.4 makes the diary's "Forget everything"
    /// THE wipe — facts, profile and conversation. The legacy Reset Memory button had a genuinely
    /// narrower job that survives it: forget the CONVERSATION only, for the user whose companion is
    /// stuck in an old pattern and who does not want to lose that she is level 41. That is
    /// <see cref="ClearConversationCommand"/>, calling <c>CompanionBrain.ForgetConversation</c> —
    /// the same method the old button called, in the room where the plumbing lives. No orphaned
    /// button, no duplicated wipe, and the two scopes are now distinguishable.</para>
    /// </summary>
    internal sealed class EngineRoomRuntimeVm : CompanionObservable, IEngineRoomDrawerVm
    {
        private readonly CompanionRuntimeContext _ctx;
        private readonly List<string> _liveActions = new();

        private bool _isExpanded;
        private CompanionProviderMode _provider = CompanionProviderMode.Cloud;
        private bool _isLoggedIn;
        private string _statusLine = string.Empty;
        private bool _isHealthy;
        private string _ollamaModel = string.Empty;
        private string _ollamaHost = string.Empty;
        private string _customEndpoint = string.Empty;
        private string _customApiKey = string.Empty;
        private string _customModel = string.Empty;
        private string _dailyLimitLabel = string.Empty;
        private bool _showLiveActions;
        private bool _suppressWrite;

        public EngineRoomRuntimeVm(CompanionRuntimeContext ctx)
        {
            _ctx = ctx;

            LoginCommand = new CompanionRelayCommand(() => _ctx.WithWindow(w => w.ShowTab("patreon")));
            TestConnectionCommand = new CompanionRelayCommand(TestConnection);
            SetupLocalCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w =>
                {
                    w.BtnSetupLocalAi_Click(this, CompanionRuntimeContext.Routed());
                    Sync();
                }));
            SamplerSettingsCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => w.BtnOpenAiSamplerSettings_Click(this, CompanionRuntimeContext.Routed())));
            DailyLimitCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => { w.PromptForDailyRequestLimit(); Sync(); }));
            ClearConversationCommand = new CompanionRelayCommand(
                () => _ctx.WithWindow(w => { w.ClearCompanionConversation(); Sync(); }));

            AttachLiveActions();
            Sync();
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public CompanionProviderMode Provider
        {
            get => _provider;
            set
            {
                if (_provider == value) return;
                _provider = value;
                Raise(nameof(Provider));
                if (_suppressWrite) return;
                // MainWindow owns the radio handlers' side effects (settings write, panel
                // visibility, live-actions clear, pill refresh); this is the same call they make.
                _ctx.WithWindow(w => w.SetAiProviderMode(value));
                Sync();
            }
        }

        public string DrawerNote => CompanionLocStaging.Resolve("companion_engine_drawer_note");

        // ---- cloud ----
        public bool IsLoggedIn { get => _isLoggedIn; private set => Set(ref _isLoggedIn, value); }
        public string LoginPrompt => CompanionLocStaging.Resolve("companion_engine_login_prompt");
        public string LoginButtonLabel => CompanionLocStaging.Resolve("companion_engine_login_button");
        public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }
        public bool IsHealthy { get => _isHealthy; private set => Set(ref _isHealthy, value); }

        // ---- local ----
        public string OllamaModel
        {
            get => _ollamaModel;
            set { if (Set(ref _ollamaModel, value ?? string.Empty) && !_suppressWrite) WriteField(s => s.AiModel = _ollamaModel.Trim()); }
        }

        public string OllamaHost
        {
            get => _ollamaHost;
            set { if (Set(ref _ollamaHost, value ?? string.Empty) && !_suppressWrite) WriteField(s => s.AiOllamaHost = _ollamaHost.Trim()); }
        }

        // ---- custom (BYO) ----
        public string CustomEndpoint
        {
            get => _customEndpoint;
            set { if (Set(ref _customEndpoint, value ?? string.Empty) && !_suppressWrite) WriteField(s => s.OpenAiCompatibleEndpoint = _customEndpoint.Trim()); }
        }

        /// <summary>
        /// Never round-trips the stored secret. The getter returns empty, so the key cannot be read
        /// back out of a binding, off a screenshot or out of a UI-automation dump; the setter only
        /// writes when the user actually typed something. The view backs this with a PasswordBox for
        /// the same reason.
        /// </summary>
        public string CustomApiKey
        {
            get => string.Empty;
            set
            {
                _customApiKey = value ?? string.Empty;
                if (_suppressWrite || _customApiKey.Length == 0) return;
                _ctx.WithWindow(w => w.SetCustomApiKey(_customApiKey));
            }
        }

        public string CustomModel
        {
            get => _customModel;
            set { if (Set(ref _customModel, value ?? string.Empty) && !_suppressWrite) WriteField(s => s.OpenAiCompatibleModel = _customModel.Trim()); }
        }

        public string DailyLimitLabel { get => _dailyLimitLabel; private set => Set(ref _dailyLimitLabel, value); }

        // ---- live actions ----
        public bool ShowLiveActions { get => _showLiveActions; private set => Set(ref _showLiveActions, value); }
        public IReadOnlyList<string> LiveActions => _liveActions;
        public string LiveActionsPlaceholder => CompanionLocStaging.Resolve("companion_engine_live_actions_placeholder");

        public ICommand LoginCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand SetupLocalCommand { get; }
        public ICommand SamplerSettingsCommand { get; }
        public ICommand DailyLimitCommand { get; }

        /// <summary>
        /// "Clear conversation" — the legacy Reset Memory button's narrower scope, rehoused.
        /// See the class remarks for why this is not the diary's wipe.
        /// </summary>
        public ICommand ClearConversationCommand { get; }

        /// <summary>Label for <see cref="ClearConversationCommand"/>.</summary>
        public string ClearConversationLabel => CompanionLocStaging.Resolve("companion_engine_clear_conversation");

        /// <summary>The one-line explanation of what that button does and does not touch.</summary>
        public string ClearConversationNote => CompanionLocStaging.Resolve("companion_engine_clear_conversation_note");

        // =====================================================================================

        /// <summary>Re-reads every provider field from settings. Never writes back.</summary>
        public void Sync()
        {
            _suppressWrite = true;
            try
            {
                CompanionRuntimeContext.Guarded(SyncCore, "engine sync");
            }
            finally
            {
                _suppressWrite = false;
            }
        }

        private void SyncCore()
        {
            var settings = App.Settings?.Current;
            var prompt = settings?.CompanionPrompt;

            Provider = ModeFor(settings?.AiChatEnabled == true, prompt?.AiProvider ?? AiProviderType.Cloud);

            OllamaModel = prompt?.AiModel ?? string.Empty;
            OllamaHost = prompt?.AiOllamaHost ?? string.Empty;
            CustomEndpoint = prompt?.OpenAiCompatibleEndpoint ?? string.Empty;
            CustomModel = prompt?.OpenAiCompatibleModel ?? string.Empty;

            int limit = prompt?.DailyRequestLimit ?? 0;
            DailyLimitLabel = limit > 0
                ? CompanionLocStaging.ResolveF("companion_engine_daily_limit_fmt", limit)
                : CompanionLocStaging.Resolve("companion_engine_daily_limit_none");

            IsLoggedIn = App.HasCloudIdentity;
            ShowLiveActions = _provider is CompanionProviderMode.LocalOllama or CompanionProviderMode.Custom;
            RefreshLiveActions();
            RefreshStatus();
        }

        /// <summary>
        /// Settings pair (enabled + provider) → the segmented row's one value. Static so the mapping
        /// is testable and so "AI off" cannot drift from "the Off segment".
        /// </summary>
        internal static CompanionProviderMode ModeFor(bool aiEnabled, AiProviderType provider)
        {
            if (!aiEnabled) return CompanionProviderMode.Off;
            return provider switch
            {
                AiProviderType.Local => CompanionProviderMode.LocalOllama,
                AiProviderType.OpenAiCompatible => CompanionProviderMode.Custom,
                _ => CompanionProviderMode.Cloud
            };
        }

        /// <summary>The inverse, for the write path. Off maps to Cloud + disabled.</summary>
        internal static (bool Enabled, AiProviderType Provider) SettingsFor(CompanionProviderMode mode) => mode switch
        {
            CompanionProviderMode.Off => (false, AiProviderType.Cloud),
            CompanionProviderMode.LocalOllama => (true, AiProviderType.Local),
            CompanionProviderMode.Custom => (true, AiProviderType.OpenAiCompatible),
            _ => (true, AiProviderType.Cloud)
        };

        /// <summary>
        /// The live "is she thinking?" readout. Absorbs the old <c>TxtAiStatus</c> line and the two
        /// per-provider health labels into one place, which is what the drawer draws.
        /// </summary>
        public void SetStatus(string text, bool healthy)
        {
            StatusLine = text ?? string.Empty;
            IsHealthy = healthy;
        }

        private void RefreshStatus()
        {
            if (_provider == CompanionProviderMode.Off)
            {
                SetStatus(CompanionLocStaging.Resolve("companion_engine_status_off"), healthy: false);
                return;
            }

            if (_provider == CompanionProviderMode.Cloud && !App.HasCloudIdentity)
            {
                SetStatus(CompanionLocStaging.Resolve("companion_engine_status_disconnected"), healthy: false);
                return;
            }

            bool available = App.Ai?.IsAvailable == true;
            int remaining = App.Ai?.DailyRequestsRemaining ?? -1;
            var text = !available
                ? Loc.Get("label_ai_initializing")
                : remaining >= 0
                    ? CompanionLocStaging.ResolveF("companion_engine_status_ready_fmt", remaining)
                    : CompanionLocStaging.Resolve("companion_engine_status_ready");
            SetStatus(text, healthy: available);
        }

        private void TestConnection() => _ctx.WithWindow(w =>
        {
            switch (_provider)
            {
                case CompanionProviderMode.LocalOllama:
                    w.BtnTestOllamaConnection_Click(this, CompanionRuntimeContext.Routed());
                    break;
                case CompanionProviderMode.Custom:
                    w.BtnTestOpenAiConnection_Click(this, CompanionRuntimeContext.Routed());
                    break;
                default:
                    w.TestCloudConnection();
                    break;
            }
        });

        private void WriteField(Action<CompanionPromptSettings> write) => CompanionRuntimeContext.Guarded(() =>
        {
            var prompt = App.Settings?.Current?.CompanionPrompt;
            if (prompt == null) return;
            write(prompt);
            App.Settings?.Save();
        }, "engine field write");

        // ---- live actions feed ----

        private void AttachLiveActions() => CompanionRuntimeContext.Guarded(() =>
        {
            if (App.AiLiveActions is INotifyCollectionChanged notifier)
                notifier.CollectionChanged += OnLiveActionsChanged;
        }, "attach live actions");

        private void OnLiveActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Fire-and-forget from a service thread: never touch UI state without a live dispatcher.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(RefreshLiveActions), DispatcherPriority.Normal);
        }

        private void RefreshLiveActions()
        {
            _liveActions.Clear();
            var source = App.AiLiveActions;
            if (source != null) _liveActions.AddRange(source.ToList());
            Raise(nameof(LiveActions));
        }
    }
}
