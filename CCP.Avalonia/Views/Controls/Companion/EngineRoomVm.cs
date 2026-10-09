using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;
using Providers = ConditioningControlPanel.Views.Controls.Companion.EngineRoomProviders;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// The Engine Room's viewmodel on this head: WPF EngineRoomRuntimeVm
    /// (ConditioningControlPanel/Views/Controls/Companion/Runtime/EngineRoomRuntimeVm.cs) plus the
    /// MainWindow handlers it called (SetAiProviderMode, TestCloudConnection, the two Test buttons in
    /// MainWindow.Patreon.cs). The mapping and both probes are Core <see cref="Providers"/>, so the two
    /// heads cannot drift; the active transport follows the setting through Core AiServiceStrategy.
    ///
    /// <para>ponytail: no Live actions feed (AiCommandService.LiveActionSink is unseeded on this head),
    /// so ShowLiveActions stays false and the placeholder shows. The BYO API key box saves through
    /// Platform/ApiKeyProtector (DPAPI as WPF, the secret store elsewhere). The "offer setup when
    /// Ollama is unreachable" prompt is not wired yet (OllamaSetupService.DetectAsync is in Core now). Login opens Settings &gt; Account.</para>
    /// </summary>
    public sealed class EngineRoomVm : INotifyPropertyChanged
    {
        private readonly Control _host;
        private bool _isExpanded, _isHealthy, _isLoggedIn, _suppressWrite;
        private Mode _provider = Mode.Cloud;
        private string _statusLine = "", _ollamaModel = "", _ollamaHost = "", _customEndpoint = "",
            _customModel = "", _dailyLimitLabel = "";

        public EngineRoomVm(Control host)
        {
            _host = host;
            TestConnectionCommand = new RelayCommand(() => _ = TestConnectionAsync());
            SetupLocalCommand = new RelayCommand(() => _ = SetupLocalAsync());
            SamplerSettingsCommand = new RelayCommand(() => _ = (host as EngineRoomDrawer)?.OpenSamplerSettingsAsync());
            DailyLimitCommand = new RelayCommand(() => _ = DailyLimitAsync());
            ClearConversationCommand = new RelayCommand(() => _ = ClearConversationAsync());
            LoginCommand = new RelayCommand(OpenAccountSettings);
            Sync();
        }

        private MainShellWindow? Window => TopLevel.GetTopLevel(_host) as MainShellWindow;

        /// <summary>WPF ShowAppInfoPopup -> ShowAccountSettings: the Settings page, scrolled to Account.</summary>
        internal void OpenAccountSettings()
        {
            if (Window is not { } w) return;
            w.ShowTab("appsettings");
            w.Named<Tabs.AppSettingsTabView>("AppSettingsTab")
                ?.FindControl<AppSettings.AccountSettingsSection>("SectionAccount")?.BringIntoView();
        }

        public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

        /// <summary>The segment. A user pick writes settings exactly as WPF SetAiProviderMode.</summary>
        public Mode Provider
        {
            get => _provider;
            set
            {
                if (!Set(ref _provider, value) || _suppressWrite) return;
                var s = CoreSettings.Current;
                if (s?.CompanionPrompt == null) return;
                var (enabled, provider) = Providers.SettingsFor(value);
                s.AiChatEnabled = enabled;
                if (enabled) s.CompanionPrompt.AiProvider = provider;
                CoreSettings.Save();
                Serilog.Log.Information("Companion room: provider set to {Mode}", value);
                Sync();
            }
        }

        public string DrawerNote => Loc.Get("companion_engine_drawer_note");
        public bool IsLoggedIn { get => _isLoggedIn; private set => Set(ref _isLoggedIn, value); }
        public string LoginPrompt => Loc.Get("companion_engine_login_prompt");
        public string LoginButtonLabel => Loc.Get("companion_engine_login_button");
        public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }
        public bool IsHealthy { get => _isHealthy; private set => Set(ref _isHealthy, value); }

        public string OllamaModel
        {
            get => _ollamaModel;
            set { if (Set(ref _ollamaModel, value ?? "") && !_suppressWrite) Write(p => p.AiModel = _ollamaModel.Trim()); }
        }

        public string OllamaHost
        {
            get => _ollamaHost;
            set { if (Set(ref _ollamaHost, value ?? "") && !_suppressWrite) Write(p => p.AiOllamaHost = _ollamaHost.Trim()); }
        }

        public string CustomEndpoint
        {
            get => _customEndpoint;
            set { if (Set(ref _customEndpoint, value ?? "") && !_suppressWrite) Write(p => p.OpenAiCompatibleEndpoint = _customEndpoint.Trim()); }
        }

        /// <summary>WPF EngineRoomRuntimeVm.CustomApiKey -> SetCustomApiKey (ai#8): one way, protected at rest
        /// (Platform/ApiKeyProtector), never read back. An emptied box revokes the key. The OneWayToSource
        /// binding pushes the box's initial "" on attach: only a CHANGE of the box writes, so that push can
        /// never wipe a stored key.</summary>
        public string CustomApiKey
        {
            get => "";
            set
            {
                var v = value ?? "";
                if (v == _customApiKeyBox) return;
                _customApiKeyBox = v;
                if (_suppressWrite) return;
                ConditioningControlPanel.Avalonia.Platform.ApiKeyProtector.SaveCustomKey(v);
            }
        }
        private string _customApiKeyBox = "";

        public string CustomModel
        {
            get => _customModel;
            set { if (Set(ref _customModel, value ?? "") && !_suppressWrite) Write(p => p.OpenAiCompatibleModel = _customModel.Trim()); }
        }

        public string DailyLimitLabel { get => _dailyLimitLabel; private set => Set(ref _dailyLimitLabel, value); }
        public bool ShowLiveActions => false;
        public IReadOnlyList<string> LiveActions { get; } = Array.Empty<string>();
        public string LiveActionsPlaceholder => Loc.Get("companion_engine_live_actions_placeholder");

        /// <summary>WPF LoginCommand = ShowTab("patreon"), which lands on Settings &gt; Account (ai#16).</summary>
        public ICommand? LoginCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand SetupLocalCommand { get; }
        public ICommand SamplerSettingsCommand { get; }
        public ICommand DailyLimitCommand { get; }
        public ICommand ClearConversationCommand { get; }
        public string ClearConversationLabel => Loc.Get("companion_engine_clear_conversation");
        public string ClearConversationNote => Loc.Get("companion_engine_clear_conversation_note");

        /// <summary>Re-reads every field from settings. Never writes back (WPF Sync).</summary>
        public void Sync()
        {
            _suppressWrite = true;
            try
            {
                var s = CoreSettings.Current;
                var p = s?.CompanionPrompt;
                Provider = Providers.ModeFor(s?.AiChatEnabled == true, p?.AiProvider ?? AiProviderType.Cloud);
                OllamaModel = p?.AiModel ?? "";
                OllamaHost = p?.AiOllamaHost ?? "";
                CustomEndpoint = p?.OpenAiCompatibleEndpoint ?? "";
                CustomModel = p?.OpenAiCompatibleModel ?? "";
                int limit = p?.DailyRequestLimit ?? 0;
                DailyLimitLabel = limit > 0
                    ? Loc.GetF("companion_engine_daily_limit_fmt", limit)
                    : Loc.Get("companion_engine_daily_limit_none");
                IsLoggedIn = !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);
                RefreshStatus();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "EngineRoomVm: sync failed"); }
            finally { _suppressWrite = false; }
        }

        public void SetStatus(string text, bool healthy) { StatusLine = text ?? ""; IsHealthy = healthy; }

        private void RefreshStatus()
        {
            if (_provider == Mode.Off) { SetStatus(Loc.Get("companion_engine_status_off"), false); return; }
            if (_provider == Mode.Cloud && !IsLoggedIn) { SetStatus(Loc.Get("companion_engine_status_disconnected"), false); return; }
            bool available = App.Ai?.IsAvailable == true;
            int remaining = App.Ai?.DailyRequestsRemaining ?? -1;
            SetStatus(!available ? Loc.Get("label_ai_initializing")
                : remaining >= 0 ? Loc.GetF("companion_engine_status_ready_fmt", remaining)
                : Loc.Get("companion_engine_status_ready"), available);
        }

        /// <summary>WPF EngineRoomRuntimeVm.TestConnection and the three handlers it dispatched to.</summary>
        internal async Task TestConnectionAsync()
        {
            switch (_provider)
            {
                case Mode.LocalOllama:
                    var host = CoreSettings.Current?.CompanionPrompt?.AiOllamaHost;
                    if (!string.IsNullOrWhiteSpace(host)) SetStatus(Loc.Get("label_status_testing"), false);
                    var (t1, h1) = await Providers.TestOllamaAsync(host);
                    SetStatus(t1, h1);
                    break;
                case Mode.Custom:
                    SetStatus(Loc.Get("label_status_testing"), false);
                    var (t2, h2) = await Providers.TestOpenAiCompatibleAsync();
                    SetStatus(t2, h2);
                    break;
                default:
                    bool available = App.Ai?.IsAvailable == true;
                    SetStatus(available ? Loc.Get("label_status_connected") : Loc.Get("label_login_required"), available);
                    break;
            }
        }

        /// <summary>WPF LaunchLocalAiSetupWizard: the wizard saves model + provider; a ready finish selects Local.</summary>
        private async Task SetupLocalAsync()
        {
            if (Window is not { IsVisible: true } owner) return;
            var wizard = new Views.Dialogs.LocalAiSetupWizard();
            if (await wizard.ShowDialogSafe<bool>(owner) && wizard.LocalAiReady) Provider = Mode.LocalOllama;
            Sync();
        }

        /// <summary>WPF EngineRoomRuntimeVm: the label is re-read once the dialog closes.</summary>
        private async Task DailyLimitAsync()
        {
            if (Window is { } w) await w.PromptForDailyRequestLimit();
            Sync();
        }

        private async Task ClearConversationAsync()
        {
            if (Window is { } w) await w.ClearCompanionConversationAsync();
            Sync();
        }

        private void Write(Action<CompanionPromptSettings> write)
        {
            var p = CoreSettings.Current?.CompanionPrompt;
            if (p == null) return;
            write(p);
            CoreSettings.Save();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }
}
